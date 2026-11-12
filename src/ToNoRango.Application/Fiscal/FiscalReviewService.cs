using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Inventory;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public enum FindingLevel
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>Um apontamento da conferência: o que foi achado e o que fazer. Sempre para revisão de profissional habilitado.</summary>
public sealed record FiscalFinding(FindingLevel Level, string Area, string Title, string Detail, string Action);

public sealed record FiscalReviewDto(DateTimeOffset CheckedAt, int Critical, int Warnings, IReadOnlyList<FiscalFinding> Findings);

/// <summary>
/// Assistente fiscal da ZEST: confere cadastro, notas e prazos e explica rejeições. Não decide tributação,
/// só aponta para conferência; toda mudança passa por quem responde pela escrita fiscal.
/// </summary>
public sealed class FiscalReviewService(
    IRepository<FiscalProfile> profiles,
    IRepository<MenuItem> menuItems,
    IRepository<ProductFiscalData> productData,
    IRepository<FiscalDocument> documents,
    IRepository<ReceivedNote> received,
    IRepository<PurchaseEntry> purchases,
    TimeProvider clock)
{
    /// <summary>Rejeições mais comuns da SEFAZ, em português de balcão.</summary>
    private static readonly Dictionary<string, string> Rejections = new()
    {
        ["204"] = "Essa nota já foi autorizada antes (mesmo número). Consulte a chave na SEFAZ antes de emitir de novo; se for outra venda, use o próximo número.",
        ["539"] = "O número já foi usado com outra chave. Não reaproveite número: emita com o próximo e inutilize o que ficou pulado.",
        ["225"] = "O XML não passou no esquema da SEFAZ: algum campo está vazio ou fora do formato (CEP, IE, NCM, endereço). Confira o cadastro do emitente e dos produtos.",
        ["778"] = "NCM inexistente na tabela da Receita. Corrija o NCM do produto na tributação.",
        ["301"] = "Uso denegado por irregularidade do emitente na SEFAZ. Fale com o contador: a inscrição estadual precisa ser regularizada antes de emitir.",
        ["302"] = "Uso denegado por irregularidade do destinatário. Confira a inscrição estadual do cliente ou emita sem IE se ele não for contribuinte.",
        ["704"] = "NFC-e com data de emissão atrasada. Venda offline deve ir em contingência e ser enviada em até 24 horas.",
        ["280"] = "Certificado do transmissor inválido. Confira se o certificado A1 enviado é o da loja e se está dentro da validade.",
        ["999"] = "Erro não catalogado na SEFAZ, geralmente instabilidade. Tente de novo em alguns minutos.",
    };

    public Task<Result<FiscalReviewDto>> ReviewAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalReviewDto>(async () =>
        {
            var now = clock.GetUtcNow();
            var findings = new List<FiscalFinding>();
            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile is null || profile.Cnpj.Length == 0)
            {
                findings.Add(new(FindingLevel.Critical, "Emitente", "Dados do emitente não preenchidos", "Sem CNPJ, IE e endereço não há como emitir nem gerar SPED.", "Preencha em Configuração."));
            }
            else
            {
                if (!profile.HasCertificate)
                {
                    findings.Add(new(FindingLevel.Warning, "Certificado", "Sem certificado A1", "Sem ele não dá para emitir direto nem baixar as notas contra o CNPJ.", "Envie o .pfx em Configuração."));
                }
                else if (profile.CertificateExpiresAt is { } expires && expires - now < TimeSpan.FromDays(30))
                {
                    var days = Math.Max(0, (int)(expires - now).TotalDays);
                    findings.Add(new(expires < now ? FindingLevel.Critical : FindingLevel.Warning, "Certificado", expires < now ? "Certificado vencido" : $"Certificado vence em {days} dia(s)",
                        $"Validade: {expires:dd/MM/yyyy}.", "Renove com a certificadora e envie o novo .pfx."));
                }

                if (profile.Environment == 2)
                {
                    findings.Add(new(FindingLevel.Info, "Ambiente", "Emitindo em homologação", "As notas não têm valor fiscal.", "Passe para Produção quando os testes terminarem."));
                }

                if (string.IsNullOrWhiteSpace(profile.AccountantEmail) || string.IsNullOrWhiteSpace(profile.AccountantCrc))
                {
                    findings.Add(new(FindingLevel.Info, "Contador", "Contador não cadastrado", "O SPED pede os dados do contador (bloco 0100).", "Cadastre nome, CPF, CRC e e-mail do contador em Configuração."));
                }
            }

            var items = await menuItems.ListAsync(item => item.EstablishmentId == establishmentId && item.IsActive, item => item.Name, cancellationToken, take: 5000);
            var taxes = (await productData.ListAsync(item => item.EstablishmentId == establishmentId, item => item.MenuItemId, cancellationToken, take: 5000)).ToDictionary(item => item.MenuItemId);
            var missing = items.Where(item => !taxes.ContainsKey(item.Id)).Select(item => item.Name).ToList();

            if (missing.Count > 0)
            {
                findings.Add(new(FindingLevel.Warning, "Produtos", $"{missing.Count} produto(s) sem tributação", string.Join(", ", missing.Take(12)) + (missing.Count > 12 ? "…" : string.Empty),
                    "Preencha NCM, CFOP e CST em Tributação dos produtos. Sem isso a nota não sai."));
            }

            foreach (var item in items)
            {
                if (!taxes.TryGetValue(item.Id, out var tax))
                {
                    continue;
                }

                if (!FiscalTables.IsCfop(tax.Cfop))
                {
                    findings.Add(new(FindingLevel.Critical, "Produtos", $"CFOP {tax.Cfop} inexistente em {item.Name}", "O código não está na tabela de CFOP.", "Escolha um CFOP da tabela (venda dentro do estado começa com 5)."));
                }
                else if (tax.Cfop[0] is '1' or '2' or '3')
                {
                    findings.Add(new(FindingLevel.Critical, "Produtos", $"CFOP de entrada na venda de {item.Name}", $"{tax.Cfop}: {FiscalTables.CfopDescription(tax.Cfop)}.", "Na venda use CFOP de saída (5, 6 ou 7)."));
                }
                else if (tax.Cfop[0] == '6')
                {
                    findings.Add(new(FindingLevel.Warning, "Produtos", $"CFOP interestadual em {item.Name}", "A NFC-e só aceita operação dentro do estado.", "Para o balcão e o delivery local, use o CFOP 5 equivalente."));
                }

                if (tax.IcmsCst == "60" && tax.IcmsRate > 0)
                {
                    findings.Add(new(FindingLevel.Warning, "Produtos", $"ICMS com alíquota em CST 60 ({item.Name})", "No CST 60 o ICMS já foi recolhido por substituição: não há destaque.", "Confira com o contador se o CST ou a alíquota está certo."));
                }

                if (tax.Cfop is "5405" or "6404" && tax.IcmsCst is not ("60" or "10" or "70"))
                {
                    findings.Add(new(FindingLevel.Warning, "Produtos", $"CFOP de ST com CST sem ST ({item.Name})", $"CFOP {tax.Cfop} é de mercadoria com substituição tributária, mas o CST é {tax.IcmsCst}.", "Revise o par CFOP/CST com o contador."));
                }
            }

            var recent = await documents.ListAsync(item => item.EstablishmentId == establishmentId && item.CreatedAt >= now.AddDays(-45), item => item.CreatedAt, cancellationToken, descending: true, take: 2000);

            foreach (var note in recent.Where(item => item.Status == FiscalDocumentStatus.Rejected).Take(20))
            {
                var code = (note.Message ?? string.Empty).Split(' ', 2)[0];
                findings.Add(new(FindingLevel.Warning, "Rejeições", $"{(note.Model == FiscalDocument.NfeModel ? "NF-e" : "NFC-e")} {note.Number} rejeitada", note.Message ?? "Sem mensagem da SEFAZ.",
                    Rejections.GetValueOrDefault(code, "Leia o motivo da SEFAZ, corrija o cadastro e emita de novo.")));
            }

            foreach (var note in recent.Where(item => item.Status == FiscalDocumentStatus.Contingency && item.ContingencyAt is { } at && now - at > TimeSpan.FromHours(20)))
            {
                findings.Add(new(FindingLevel.Critical, "Contingência", $"NFC-e {note.Number} em contingência há mais de 20 h", "O prazo da SEFAZ para enviar é de 24 horas.", "Clique em Enviar contingência agora."));
            }

            foreach (var note in recent.Where(item => item.Status == FiscalDocumentStatus.Processing && now - item.UpdatedAt > TimeSpan.FromMinutes(30)))
            {
                findings.Add(new(FindingLevel.Warning, "Notas", $"Nota {note.Number} sem resposta da SEFAZ", "Pode ter sido autorizada sem a resposta chegar.", "Consulte a chave antes de emitir de novo para não duplicar."));
            }

            var incoming = await received.ListAsync(item => item.EstablishmentId == establishmentId, item => item.IssuedAt, cancellationToken, descending: true, take: 5000);
            var stuck = incoming.Count(item => item.Status == ReceivedNoteStatus.Summary && item.AcknowledgedAt is { } at && now - at > TimeSpan.FromHours(24));
            var pending = incoming.Where(item => item.Status == ReceivedNoteStatus.Full && item.Situation == 1).ToList();
            var imported = (await purchases.ListAsync(item => item.EstablishmentId == establishmentId && item.AccessKey != null, item => item.IssuedOn, cancellationToken, take: 20_000))
                .Select(item => item.AccessKey!).ToHashSet();

            if (stuck > 0)
            {
                findings.Add(new(FindingLevel.Info, "Notas recebidas", $"{stuck} nota(s) só com resumo há mais de um dia", "A ciência foi dada mas o XML completo ainda não veio.", "Busque de novo em Notas recebidas."));
            }

            var notPosted = pending.Where(item => !imported.Contains(item.AccessKey)).ToList();

            if (notPosted.Count > 0)
            {
                findings.Add(new(FindingLevel.Warning, "Compras", $"{notPosted.Count} nota(s) de compra não lançada(s)", $"Total {notPosted.Sum(item => item.TotalInCents) / 100m:C}. Sem o lançamento, estoque, contas a pagar e SPED ficam incompletos.",
                    "Abra Notas recebidas e clique em Lançar em Compras."));
            }

            var cancelledButPosted = incoming.Where(item => item.Situation == 3 && imported.Contains(item.AccessKey)).ToList();

            foreach (var note in cancelledButPosted)
            {
                findings.Add(new(FindingLevel.Critical, "Compras", $"Compra lançada com nota cancelada pelo fornecedor", $"{note.IssuerName} · {note.AccessKey}.", "Estorne a entrada no estoque e peça a nota correta ao fornecedor."));
            }

            var ordered = findings.OrderByDescending(item => item.Level).ToList();
            return new FiscalReviewDto(now, ordered.Count(item => item.Level == FindingLevel.Critical), ordered.Count(item => item.Level == FindingLevel.Warning), ordered);
        });
}
