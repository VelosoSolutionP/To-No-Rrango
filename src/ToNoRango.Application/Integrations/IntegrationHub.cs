using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Catalog;
using ToNoRango.Application.Contracts;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Payments;
using ToNoRango.Domain.Zest;

namespace ToNoRango.Application.Integrations;

/// <summary>ok: working · warn: working with something to look at · bad: broken · off: not set up.</summary>
public sealed record IntegrationStatusDto(
    string Code,
    string Name,
    string Category,
    string Status,
    string Detail,
    string? Module,
    string Target,
    DateTimeOffset? LastActivityAt);

/// <summary>
/// One screen with every connection of the store: payments, WhatsApp, ZEST, SEFAZ, fiscal API,
/// webhooks, accountant, software houses and the barcode base. It only reads; each item opens its screen.
/// </summary>
public sealed class IntegrationHubService(
    IRepository<PaymentSettings> payments,
    IRepository<WhatsAppSettings> whatsapp,
    IRepository<OutboundMessage> outbound,
    IRepository<ZestConnection> zest,
    IRepository<FiscalProfile> profiles,
    IRepository<FiscalApiKey> apiKeys,
    IRepository<FiscalWebhook> webhooks,
    IRepository<FiscalWebhookDelivery> deliveries,
    IRepository<AccountantLink> partners,
    IRepository<AccountantMessage> partnerMessages,
    IGtinCatalog gtin,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<IntegrationStatusDto>> GetAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var items = new List<IntegrationStatusDto>();

        var payment = await payments.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
        items.Add(payment is { CanChargeCheckout: true }
            ? new("pagamentos", "Mercado Pago", "Vendas", "ok", $"Recebendo {string.Join(" e ", new[] { payment.IsPixEnabled ? "Pix" : null, payment.IsCardEnabled ? "cartão" : null }.OfType<string>())} on-line.", Modules.Pagamentos, "settings", payment.UpdatedAt)
            : new("pagamentos", "Mercado Pago", "Vendas", "off", "Sem credencial: o cliente só paga na entrega ou no balcão.", Modules.Pagamentos, "settings", payment?.UpdatedAt));

        var phone = await whatsapp.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
        var since = now.AddDays(-1);
        var failed = phone is { IsReady: true }
            ? await outbound.CountAsync(item => item.EstablishmentId == establishmentId && item.Status == OutboundStatus.Failed && item.CreatedAt >= since, cancellationToken)
            : 0;
        items.Add(phone is not { IsReady: true }
            ? new("whatsapp", "WhatsApp da loja", "Atendimento", "off", "Número não conectado: avisos e respostas automáticas estão parados.", Modules.WhatsApp, "zest", null)
            : failed > 0
                ? new("whatsapp", "WhatsApp da loja", "Atendimento", "warn", $"Conectado, mas {failed} mensagem(ns) falharam nas últimas 24 horas.", Modules.WhatsApp, "zest", null)
                : new("whatsapp", "WhatsApp da loja", "Atendimento", "ok", $"Conectado{(phone.AutoReply ? ", com resposta automática" : string.Empty)}.", Modules.WhatsApp, "zest", null));

        var ai = await zest.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
        items.Add(ai is null
            ? new("zest", "ZEST IA", "Atendimento", "off", "A IA do ZEST ainda não foi ligada a esta loja.", Modules.Ia, "zest", null)
            : new("zest", "ZEST IA", "Atendimento", ai.LastUsedAt is null ? "warn" : "ok",
                ai.LastUsedAt is null ? $"Chave criada (final {ai.KeyHint}), ainda sem mensagens." : $"{ai.MessagesRelayed} mensagem(ns) atendidas.", Modules.Ia, "zest", ai.LastUsedAt ?? ai.CreatedAt));

        var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
        items.Add(Sefaz(profile, now));

        var keys = await apiKeys.ListAsync(item => item.EstablishmentId == establishmentId && item.RevokedAt == null, item => item.CreatedAt, cancellationToken, take: 100);
        items.Add(keys.Count == 0
            ? new("fiscal_api", "API fiscal", "Fiscal", "off", "Nenhuma chave: nenhum outro sistema emite ou consulta notas desta loja.", Modules.Fiscal, "fiscal", null)
            : new("fiscal_api", "API fiscal", "Fiscal", "ok",
                $"{keys.Count(item => !item.IsTest)} chave(s) de produção e {keys.Count(item => item.IsTest)} de teste.", Modules.Fiscal, "fiscal", keys.Max(item => item.LastUsedAt)));

        var hooks = await webhooks.CountAsync(item => item.EstablishmentId == establishmentId && item.IsActive, cancellationToken);
        var gaveUp = await deliveries.CountAsync(item => item.EstablishmentId == establishmentId && item.Status == WebhookDeliveryStatus.Failed && item.CreatedAt >= now.AddDays(-7), cancellationToken);
        var retrying = await deliveries.CountAsync(item => item.EstablishmentId == establishmentId && item.Status == WebhookDeliveryStatus.Pending && item.Attempts > 0, cancellationToken);
        items.Add(hooks == 0
            ? new("webhooks", "Webhooks fiscais", "Fiscal", "off", "Nenhum sistema recebe avisos das notas.", Modules.Fiscal, "fiscal", null)
            : gaveUp > 0
                ? new("webhooks", "Webhooks fiscais", "Fiscal", "bad", $"{gaveUp} aviso(s) não entregues nos últimos 7 dias. Confira o endereço e reenvie.", Modules.Fiscal, "fiscal", null)
                : retrying > 0
                    ? new("webhooks", "Webhooks fiscais", "Fiscal", "warn", $"{retrying} aviso(s) esperando nova tentativa.", Modules.Fiscal, "fiscal", null)
                    : new("webhooks", "Webhooks fiscais", "Fiscal", "ok", $"{hooks} endereço(s) recebendo avisos.", Modules.Fiscal, "fiscal", null));

        var links = await partners.ListAsync(item => item.EstablishmentId == establishmentId && item.Status != AccountantLinkStatus.Revoked, item => item.CreatedAt, cancellationToken, take: 50);
        var openRequests = await partnerMessages.CountAsync(item => item.EstablishmentId == establishmentId && item.IsRequest && item.ResolvedAt == null, cancellationToken);
        items.Add(Partner(links, PartnerKind.Accounting, openRequests));
        items.Add(Partner(links, PartnerKind.SoftwareHouse, 0));

        items.Add(gtin.IsConfigured
            ? new("gtin", "Base de produtos por código de barras", "Cadastro", "ok", "Busca nome, NCM e foto pelo código de barras (2 consultas por minuto).", Modules.Cardapio, "products", null)
            : new("gtin", "Base de produtos por código de barras", "Cadastro", "off", "Usuário da base GTIN não configurado no servidor.", Modules.Cardapio, "products", null));

        return items;
    }

    private static IntegrationStatusDto Sefaz(FiscalProfile? profile, DateTimeOffset now)
    {
        const string name = "SEFAZ (certificado A1)";

        if (profile is not { HasCertificate: true })
        {
            return new("sefaz", name, "Fiscal", "off", "Sem certificado A1: a loja não emite NF-e nem NFC-e.", Modules.FiscalNfce, "fiscal", null);
        }

        var environment = profile.Environment == 1 ? "produção" : "homologação (sem valor fiscal)";

        if (profile.CertificateExpiresAt is { } expires && expires <= now)
        {
            return new("sefaz", name, "Fiscal", "bad", $"Certificado vencido em {expires:dd/MM/yyyy}. Nenhuma nota sai até trocar.", Modules.FiscalNfce, "fiscal", null);
        }

        if (profile.CertificateExpiresAt is { } soon && soon <= now.AddDays(30))
        {
            return new("sefaz", name, "Fiscal", "warn", $"Certificado vence em {soon:dd/MM/yyyy}. Renove antes. Ambiente: {environment}.", Modules.FiscalNfce, "fiscal", null);
        }

        return profile.Environment == 1
            ? new("sefaz", name, "Fiscal", "ok", $"Emitindo em produção. Certificado válido até {profile.CertificateExpiresAt:dd/MM/yyyy}.", Modules.FiscalNfce, "fiscal", null)
            : new("sefaz", name, "Fiscal", "warn", "Certificado ok, mas em homologação: as notas não têm valor fiscal.", Modules.FiscalNfce, "fiscal", null);
    }

    private static IntegrationStatusDto Partner(IReadOnlyList<AccountantLink> links, PartnerKind kind, int openRequests)
    {
        var (code, name, nobody) = kind == PartnerKind.Accounting
            ? ("contador", "Contador", "Nenhum contador com acesso. Convide em Fiscal > Contador e parceiros.")
            : ("software_house", "Software houses", "Nenhuma software house conectada.");
        var mine = links.Where(item => item.Kind == kind).ToList();
        var active = mine.Count(item => item.Status == AccountantLinkStatus.Active);
        var waiting = mine.Count(item => item.Status == AccountantLinkStatus.Invited);

        if (active == 0)
        {
            return new(code, name, "Parceiros", waiting > 0 ? "warn" : "off", waiting > 0 ? $"{waiting} convite(s) aguardando aceite." : nobody, Modules.Fiscal, "fiscal", null);
        }

        var detail = $"{active} com acesso{(waiting > 0 ? $", {waiting} convite(s) aguardando" : string.Empty)}.";

        return openRequests > 0
            ? new(code, name, "Parceiros", "warn", $"{detail} {openRequests} pedido(s) do contador em aberto.", Modules.Fiscal, "fiscal", mine.Max(item => item.AcceptedAt))
            : new(code, name, "Parceiros", "ok", detail, Modules.Fiscal, "fiscal", mine.Max(item => item.AcceptedAt));
    }
}
