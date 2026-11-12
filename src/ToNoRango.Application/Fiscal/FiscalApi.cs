using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

// ---------- Contract of the fiscal API (what partner systems send and receive) ----------

/// <summary>Who receives the note. NF-e needs the address; NFC-e takes only CPF/CNPJ and name.</summary>
public sealed record ApiRecipient(
    string Document,
    string Name,
    string? StateRegistration = null,
    string? Street = null,
    string? Number = null,
    string? District = null,
    string? CityIbgeCode = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Email = null);

/// <summary>One line with its own taxation: the partner system is the one that knows the product.</summary>
public sealed record ApiNoteLine(
    string Code,
    string Description,
    decimal Quantity,
    long TotalInCents,
    string Ncm,
    string Cfop,
    string IcmsCst,
    decimal IcmsRate,
    string PisCofinsCst,
    decimal PisRate,
    decimal CofinsRate,
    string? Cest = null,
    long FreightInCents = 0,
    long DiscountInCents = 0);

/// <summary>Method: cash, credit, debit, pix or other.</summary>
public sealed record ApiNotePayment(string Method, long AmountInCents);

public sealed record ApiNoteRequest(
    string ExternalId,
    int Model,
    IReadOnlyList<ApiNoteLine> Lines,
    IReadOnlyList<ApiNotePayment>? Payments = null,
    ApiRecipient? Recipient = null,
    int Purpose = 1,
    string? Nature = null,
    bool IsEntry = false,
    string? ReferencedKey = null,
    bool IsDelivery = false,
    bool FinalConsumer = true,
    string? AdditionalInfo = null);

public sealed record ApiNoteDto(
    Guid Id,
    string ExternalId,
    int Model,
    int Series,
    int Number,
    string Environment,
    bool Simulation,
    string Status,
    string? AccessKey,
    string? Protocol,
    string? Message,
    long TotalInCents,
    string? RecipientDocument,
    string? RecipientName,
    int Purpose,
    string? CancelReason,
    int CorrectionSequence,
    string? CorrectionText,
    bool HasXml,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ApiNoteDto From(FiscalDocument document) => new(
        document.Id,
        document.OrderCode,
        document.Model,
        document.Series,
        document.Number,
        document.Environment == 1 ? "production" : "homologation",
        document.IsSimulation,
        FiscalApiRules.StatusName(document.Status),
        document.AccessKey,
        document.Protocol,
        document.Message,
        document.TotalInCents,
        document.RecipientDocument,
        document.RecipientName,
        document.Purpose,
        document.CancelReason,
        document.CorrectionSequence,
        document.CorrectionText,
        document.Xml is not null,
        document.CreatedAt,
        document.UpdatedAt);
}

/// <summary>The key that called, as resolved by the API. Every query is fenced to its store.</summary>
public sealed record FiscalApiCaller(Guid KeyId, Guid TenantId, Guid EstablishmentId, string Name, string Prefix, bool IsTest, bool CanIssue)
{
    public string Actor => $"API: {Name} ({Prefix}…)";
}

// ---------- Panel side: managing keys and webhooks ----------

public sealed record CreateApiKeyRequest(string Name, bool IsTest, bool CanIssue);

public sealed record FiscalApiKeyDto(Guid Id, string Name, string Prefix, bool IsTest, bool CanIssue, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, Guid? PartnerFirmId = null, string? Partner = null);

/// <summary>The only moment the full key exists outside the caller's hands.</summary>
public sealed record CreatedApiKeyDto(FiscalApiKeyDto Key, string Secret);

public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string> Events);

public sealed record FiscalWebhookDto(Guid Id, string Url, IReadOnlyList<string> Events, bool IsActive, DateTimeOffset CreatedAt, Guid? PartnerFirmId = null, string? Partner = null);

public sealed record CreatedWebhookDto(FiscalWebhookDto Webhook, string Secret);

public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid WebhookId,
    string Event,
    Guid? DocumentId,
    WebhookDeliveryStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

public sealed record FiscalApiOverviewDto(
    IReadOnlyList<FiscalApiKeyDto> Keys,
    IReadOnlyList<FiscalWebhookDto> Webhooks,
    IReadOnlyList<WebhookDeliveryDto> Deliveries,
    IReadOnlyList<string> Events);

public sealed record WebhookSendResult(bool Delivered, int? StatusCode, string? Error);

/// <summary>Posts one signed event. The implementation refuses addresses inside the server's own network.</summary>
public interface IWebhookSender
{
    /// <summary>Checks the address before it is saved: https and a public host.</summary>
    string? Validate(string url);

    Task<WebhookSendResult> SendAsync(string url, string eventName, Guid deliveryId, string body, string signature, long timestamp, CancellationToken cancellationToken);
}

// ---------- Rules shared by the service, the endpoints and the tests ----------

public static class FiscalApiRules
{
    /// <summary>Series of simulated notes: far from any series a store really uses.</summary>
    public const int SimulationSeries = 999;

    private static readonly string[] Methods = ["cash", "credit", "debit", "pix", "other"];

    public static string StatusName(FiscalDocumentStatus status) => status switch
    {
        FiscalDocumentStatus.Authorized => "authorized",
        FiscalDocumentStatus.Rejected => "rejected",
        FiscalDocumentStatus.Cancelled => "cancelled",
        FiscalDocumentStatus.Contingency => "contingency",
        _ => "processing",
    };

    public static DfePaymentKind PaymentKind(string method) => method.Trim().ToLowerInvariant() switch
    {
        "cash" => DfePaymentKind.Cash,
        "credit" => DfePaymentKind.CreditCard,
        "debit" => DfePaymentKind.DebitCard,
        "pix" => DfePaymentKind.Pix,
        _ => DfePaymentKind.Other,
    };

    /// <summary>
    /// The note's identity for an external id. Same store + same id + same kind of key always lands on
    /// the same record, which is what makes a repeated call safe. Test and live never collide.
    /// </summary>
    public static Guid DocumentKey(Guid establishmentId, string externalId, bool simulate)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"fiscal-api|{establishmentId:N}|{(simulate ? "test" : "live")}|{externalId.Trim()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string? Validate(ApiNoteRequest request)
    {
        var externalId = request.ExternalId?.Trim() ?? string.Empty;

        if (externalId.Length is < 1 or > 40 || !externalId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/'))
        {
            return "externalId é obrigatório: até 40 caracteres, só letras, números, hífen, ponto, barra e sublinhado.";
        }

        if (request.Model is not (FiscalDocument.NfeModel or FiscalDocument.NfceModel))
        {
            return "model deve ser 55 (NF-e) ou 65 (NFC-e).";
        }

        var nfe = request.Model == FiscalDocument.NfeModel;

        if (request.Purpose is < 1 or > 4 || (!nfe && request.Purpose != 1))
        {
            return "purpose: 1 normal, 2 complementar, 3 ajuste, 4 devolução. NFC-e só aceita 1.";
        }

        if (!nfe && request.IsEntry)
        {
            return "NFC-e é sempre de saída.";
        }

        if (request.Nature is { Length: > 60 })
        {
            return "nature tem até 60 caracteres.";
        }

        var key = new string((request.ReferencedKey ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (request.Purpose is 2 or 4 && key.Length != 44)
        {
            return "Devolução e nota complementar precisam de referencedKey (44 dígitos) da nota de origem.";
        }

        if (key.Length is not (0 or 44))
        {
            return "referencedKey tem 44 dígitos.";
        }

        if (request.Recipient is { } recipient)
        {
            var digits = new string((recipient.Document ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

            if (!(digits.Length == 14 ? DocumentValidator.IsValidCnpj(digits) : digits.Length == 11 && DocumentValidator.IsValidCpf(digits)))
            {
                return "recipient.document: CPF ou CNPJ inválido.";
            }

            if (string.IsNullOrWhiteSpace(recipient.Name))
            {
                return "recipient.name é obrigatório.";
            }

            if (nfe && (string.IsNullOrWhiteSpace(recipient.Street) || string.IsNullOrWhiteSpace(recipient.City)
                || recipient.CityIbgeCode is not { Length: 7 } || !recipient.CityIbgeCode.All(char.IsAsciiDigit) || recipient.State is not { Length: 2 }))
            {
                return "NF-e precisa do endereço do destinatário: street, city, cityIbgeCode (7 dígitos) e state.";
            }
        }
        else if (nfe)
        {
            return "NF-e precisa de recipient.";
        }

        if (request.Lines is not { Count: > 0 } || request.Lines.Count > 990)
        {
            return "lines: de 1 a 990 itens.";
        }

        foreach (var line in request.Lines)
        {
            var label = string.IsNullOrWhiteSpace(line.Description) ? "item sem descrição" : line.Description;

            if (string.IsNullOrWhiteSpace(line.Code) || string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.TotalInCents <= 0)
            {
                return $"Cada item precisa de code, description, quantity e totalInCents acima de zero ({label}).";
            }

            if (line.FreightInCents < 0 || line.DiscountInCents < 0 || line.DiscountInCents > line.TotalInCents + line.FreightInCents)
            {
                return $"Frete e desconto não podem ser negativos, nem o desconto passar do item ({label}).";
            }

            if (line.Ncm is not { Length: 8 } || !line.Ncm.All(char.IsAsciiDigit))
            {
                return $"NCM inválido em {label}: são 8 dígitos.";
            }

            if (!FiscalTables.IsCfop(line.Cfop))
            {
                return $"CFOP {line.Cfop} não existe na tabela ({label}).";
            }

            if (request.IsEntry != line.Cfop[0] is '1' or '2' or '3')
            {
                return $"CFOP {line.Cfop} não combina com nota de {(request.IsEntry ? "entrada" : "saída")} ({label}).";
            }

            if (!nfe && line.Cfop[0] != '5')
            {
                return $"NFC-e só aceita CFOP de venda dentro do estado, 5xxx ({label}).";
            }

            if (string.IsNullOrWhiteSpace(line.IcmsCst) || string.IsNullOrWhiteSpace(line.PisCofinsCst))
            {
                return $"Informe icmsCst e pisCofinsCst ({label}).";
            }

            if (line.IcmsRate is < 0 or > 100 || line.PisRate is < 0 or > 100 || line.CofinsRate is < 0 or > 100)
            {
                return $"Alíquotas vão de 0 a 100 ({label}).";
            }
        }

        var total = request.Lines.Sum(line => line.TotalInCents + line.FreightInCents - line.DiscountInCents);
        var payments = request.Payments ?? [];

        if (payments.Any(payment => !Methods.Contains(payment.Method?.Trim().ToLowerInvariant()) || payment.AmountInCents <= 0))
        {
            return "payments: method é cash, credit, debit, pix ou other, com amountInCents acima de zero.";
        }

        if (!nfe && payments.Count == 0)
        {
            return "NFC-e precisa de payments.";
        }

        var paid = payments.Sum(payment => payment.AmountInCents);

        // Only cash may exceed the total (the rest is change); anything else must match to the cent.
        if (payments.Count > 0 && (paid < total || (paid > total && !payments.Any(payment => PaymentKind(payment.Method) == DfePaymentKind.Cash))))
        {
            return $"A soma dos pagamentos ({paid}) não fecha com o total da nota ({total}).";
        }

        return null;
    }

    /// <summary>A 44-digit key with a valid check digit, for simulated notes. It never existed at the SEFAZ.</summary>
    public static string SimulatedAccessKey(FiscalProfile profile, FiscalDocument document, DateTimeOffset now)
    {
        var state = profile.CityIbgeCode is { Length: >= 2 } city ? city[..2] : "99";
        var cnpj = new string(profile.Cnpj.Where(char.IsAsciiDigit).ToArray()).PadLeft(14, '0');
        var body = string.Create(CultureInfo.InvariantCulture, $"{state}{now:yyMM}{cnpj}{document.Model:00}{document.Series:000}{document.Number:000000000}1{document.NumericCode:00000000}");
        return body + CheckDigit(body);
    }

    public static int CheckDigit(string digits)
    {
        var sum = 0;
        var weight = 2;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            sum += (digits[index] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    public static string HashKey(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string NewSecret(string prefix) =>
        prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');

    /// <summary>Signature the receiver checks: HMAC-SHA256 of "timestamp.body" with the webhook secret.</summary>
    public static string Sign(string secret, long timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
}

// ---------- Keys ----------

public sealed class FiscalApiKeyService(IRepository<FiscalApiKey> keys, IUnitOfWork unitOfWork, TimeProvider clock)
{
    private const int MaxActiveKeys = 20;

    /// <param name="partnerFirmId">The software house creating it in the partner portal; null for the store itself.</param>
    public Task<Result<CreatedApiKeyDto>> CreateAsync(Guid tenantId, Guid establishmentId, CreateApiKeyRequest request, string actor, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<CreatedApiKeyDto>(async () =>
        {
            var name = (request.Name ?? string.Empty).Trim();

            if (name.Length is < 2 or > 60)
            {
                return Result<CreatedApiKeyDto>.Failure(AppErrors.Validation("Dê um nome de 2 a 60 caracteres para saber quem usa a chave."));
            }

            if (await keys.CountAsync(key => key.EstablishmentId == establishmentId && key.RevokedAt == null, cancellationToken) >= MaxActiveKeys)
            {
                return Result<CreatedApiKeyDto>.Failure(AppErrors.Conflict("fiscal_api.too_many_keys", $"Limite de {MaxActiveKeys} chaves ativas. Revogue uma que não usa mais."));
            }

            var secret = FiscalApiRules.NewSecret(request.IsTest ? FiscalApiKey.TestPrefix : FiscalApiKey.LivePrefix);
            var key = new FiscalApiKey(tenantId, establishmentId, name, secret[..16], FiscalApiRules.HashKey(secret), request.IsTest, request.CanIssue, actor, clock.GetUtcNow(), partnerFirmId);
            keys.Add(key);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new CreatedApiKeyDto(ToDto(key), secret);
        });

    public Task<Result<FiscalApiKeyDto>> RevokeAsync(Guid establishmentId, Guid keyId, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<FiscalApiKeyDto>(async () =>
        {
            // A partner touches only its own keys; the store touches all of them.
            var key = await keys.FirstOrDefaultAsync(item => item.Id == keyId && item.EstablishmentId == establishmentId && (partnerFirmId == null || item.PartnerFirmId == partnerFirmId), cancellationToken);

            if (key is null)
            {
                return Result<FiscalApiKeyDto>.Failure(AppErrors.NotFound("fiscal_api.key_not_found", "Chave não encontrada."));
            }

            key.Revoke(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(key);
        });

    /// <summary>Resolves the raw key of a request. Runs before the tenant is known, so it reads by hash only.</summary>
    public async Task<FiscalApiCaller?> AuthenticateAsync(string? rawKey, CancellationToken cancellationToken)
    {
        rawKey = rawKey?.Trim();

        if (string.IsNullOrEmpty(rawKey) || rawKey.Length > 100
            || !(rawKey.StartsWith(FiscalApiKey.LivePrefix, StringComparison.Ordinal) || rawKey.StartsWith(FiscalApiKey.TestPrefix, StringComparison.Ordinal)))
        {
            return null;
        }

        var hash = FiscalApiRules.HashKey(rawKey);
        var key = await keys.FirstOrDefaultAsync(item => item.Hash == hash, cancellationToken);

        if (key is not { IsActive: true })
        {
            return null;
        }

        var before = key.LastUsedAt;
        key.Touch(clock.GetUtcNow());

        if (key.LastUsedAt != before)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new FiscalApiCaller(key.Id, key.TenantId, key.EstablishmentId, key.Name, key.Prefix, key.IsTest, key.CanIssue);
    }

    public async Task<IReadOnlyList<FiscalApiKeyDto>> ListAsync(Guid establishmentId, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        (await keys.ListAsync(key => key.EstablishmentId == establishmentId && (partnerFirmId == null || key.PartnerFirmId == partnerFirmId), key => key.CreatedAt, cancellationToken, descending: true, take: 100))
            .Select(ToDto)
            .ToList();

    private static FiscalApiKeyDto ToDto(FiscalApiKey key) =>
        new(key.Id, key.Name, key.Prefix, key.IsTest, key.CanIssue, key.CreatedBy, key.CreatedAt, key.LastUsedAt, key.RevokedAt, key.PartnerFirmId);
}

// ---------- Notes through the API ----------

public sealed class FiscalApiService(IRepository<FiscalDocument> documents, DfeService dfe)
{
    public async Task<Result<ApiNoteDto>> IssueAsync(FiscalApiCaller caller, ApiNoteRequest request, CancellationToken cancellationToken)
    {
        if (!caller.CanIssue)
        {
            return Result<ApiNoteDto>.Failure(Error.Forbidden("fiscal_api.read_only", "Esta chave só consulta. Crie uma chave que emite no painel."));
        }

        var issued = await dfe.IssueExternalAsync(caller.EstablishmentId, request, caller.IsTest, cancellationToken);
        return issued.IsSuccess ? await GetAsync(caller, issued.Value.Id, cancellationToken) : Result<ApiNoteDto>.Failure(issued.Error);
    }

    public async Task<IReadOnlyList<ApiNoteDto>> ListAsync(FiscalApiCaller caller, DateTimeOffset? from, DateTimeOffset? to, string? externalId, int? take, CancellationToken cancellationToken)
    {
        var start = from ?? DateTimeOffset.MinValue;
        var end = to ?? DateTimeOffset.MaxValue;
        var code = externalId?.Trim();
        var notes = await documents.ListAsync(
            item => item.EstablishmentId == caller.EstablishmentId && item.IsSimulation == caller.IsTest
                && item.CreatedAt >= start && item.CreatedAt <= end && (code == null || item.OrderCode == code),
            item => item.CreatedAt,
            cancellationToken,
            descending: true,
            take: Math.Clamp(take ?? 50, 1, 500));

        return notes.Select(ApiNoteDto.From).ToList();
    }

    public async Task<Result<ApiNoteDto>> GetAsync(FiscalApiCaller caller, Guid id, CancellationToken cancellationToken) =>
        await FindAsync(caller, id, cancellationToken) is { } document
            ? ApiNoteDto.From(document)
            : Result<ApiNoteDto>.Failure(NotFound);

    public async Task<Result<string>> GetXmlAsync(FiscalApiCaller caller, Guid id, CancellationToken cancellationToken) =>
        await FindAsync(caller, id, cancellationToken) switch
        {
            null => Result<string>.Failure(NotFound),
            { Xml: { } xml } => xml,
            _ => Result<string>.Failure(AppErrors.NotFound("fiscal_api.no_xml", "Esta nota não tem XML (simulada ou ainda sem resposta da SEFAZ).")),
        };

    public async Task<Result<ApiNoteDto>> CancelAsync(FiscalApiCaller caller, Guid id, CancelNoteRequest request, CancellationToken cancellationToken) =>
        await ChangeAsync(caller, id, () => dfe.CancelAsync(caller.EstablishmentId, id, request, cancellationToken), cancellationToken);

    public async Task<Result<ApiNoteDto>> CorrectAsync(FiscalApiCaller caller, Guid id, CorrectionRequest request, CancellationToken cancellationToken) =>
        await ChangeAsync(caller, id, () => dfe.CorrectAsync(caller.EstablishmentId, id, request, cancellationToken), cancellationToken);

    private async Task<Result<ApiNoteDto>> ChangeAsync(FiscalApiCaller caller, Guid id, Func<Task<Result<FiscalDocumentDto>>> change, CancellationToken cancellationToken)
    {
        if (!caller.CanIssue)
        {
            return Result<ApiNoteDto>.Failure(Error.Forbidden("fiscal_api.read_only", "Esta chave só consulta."));
        }

        // A test key touches only simulated notes, a live key only real ones.
        if (await FindAsync(caller, id, cancellationToken) is null)
        {
            return Result<ApiNoteDto>.Failure(NotFound);
        }

        var changed = await change();
        return changed.IsSuccess ? await GetAsync(caller, id, cancellationToken) : Result<ApiNoteDto>.Failure(changed.Error);
    }

    private Task<FiscalDocument?> FindAsync(FiscalApiCaller caller, Guid id, CancellationToken cancellationToken) =>
        documents.FirstOrDefaultAsync(item => item.Id == id && item.EstablishmentId == caller.EstablishmentId && item.IsSimulation == caller.IsTest, cancellationToken);

    private static readonly Error NotFound = AppErrors.NotFound("fiscal_api.note_not_found", "Nota não encontrada para esta chave.");
}

// ---------- Webhooks ----------

public sealed class FiscalWebhookService(
    IRepository<FiscalWebhook> webhooks,
    IRepository<FiscalWebhookDelivery> deliveries,
    IRepository<FiscalDocument> documents,
    IRepository<AccountantFirm> partners,
    FiscalApiKeyService keys,
    IWebhookSender sender,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const int MaxWebhooks = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="partnerFirmId">A software house sees only what it created; null shows the store everything.</param>
    public async Task<FiscalApiOverviewDto> GetOverviewAsync(Guid establishmentId, CancellationToken cancellationToken, Guid? partnerFirmId = null)
    {
        var hooks = await webhooks.ListAsync(
            item => item.EstablishmentId == establishmentId && item.IsActive && (partnerFirmId == null || item.PartnerFirmId == partnerFirmId),
            item => item.CreatedAt,
            cancellationToken,
            take: 100);
        var hookIds = hooks.Select(item => item.Id).ToList();
        var recent = await deliveries.ListAsync(
            item => item.EstablishmentId == establishmentId && (partnerFirmId == null || hookIds.Contains(item.WebhookId)),
            item => item.CreatedAt,
            cancellationToken,
            descending: true,
            take: 50);
        var keyList = await keys.ListAsync(establishmentId, cancellationToken, partnerFirmId);
        var firmIds = keyList.Select(item => item.PartnerFirmId).Concat(hooks.Select(item => item.PartnerFirmId)).OfType<Guid>().Distinct().ToList();
        var names = (await partners.ListAsync(firm => firmIds.Contains(firm.Id), firm => firm.Name, cancellationToken)).ToDictionary(firm => firm.Id, firm => firm.Name);

        return new FiscalApiOverviewDto(
            keyList.Select(item => item.PartnerFirmId is { } id ? item with { Partner = names.GetValueOrDefault(id) } : item).ToList(),
            hooks.Select(item => ToDto(item) with { Partner = item.PartnerFirmId is { } id ? names.GetValueOrDefault(id) : null }).ToList(),
            recent.Select(ToDto).ToList(),
            FiscalWebhook.Events);
    }

    public Task<Result<CreatedWebhookDto>> CreateAsync(Guid tenantId, Guid establishmentId, CreateWebhookRequest request, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<CreatedWebhookDto>(async () =>
        {
            var url = (request.Url ?? string.Empty).Trim();

            if (sender.Validate(url) is { } problem)
            {
                return Result<CreatedWebhookDto>.Failure(AppErrors.Validation(problem));
            }

            if (request.Events is not { Count: > 0 } || !request.Events.Any(FiscalWebhook.Events.Contains))
            {
                return Result<CreatedWebhookDto>.Failure(AppErrors.Validation("Escolha pelo menos um evento."));
            }

            if (await webhooks.CountAsync(item => item.EstablishmentId == establishmentId && item.IsActive, cancellationToken) >= MaxWebhooks)
            {
                return Result<CreatedWebhookDto>.Failure(AppErrors.Conflict("fiscal_api.too_many_webhooks", $"Limite de {MaxWebhooks} webhooks por loja."));
            }

            var secret = FiscalApiRules.NewSecret("whsec_");
            var webhook = new FiscalWebhook(tenantId, establishmentId, url, protector.Protect(secret), request.Events, clock.GetUtcNow(), partnerFirmId);
            webhooks.Add(webhook);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new CreatedWebhookDto(ToDto(webhook), secret);
        });

    public Task<Result<FiscalWebhookDto>> RemoveAsync(Guid establishmentId, Guid webhookId, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<FiscalWebhookDto>(async () =>
        {
            var webhook = await webhooks.FirstOrDefaultAsync(item => item.Id == webhookId && item.EstablishmentId == establishmentId && (partnerFirmId == null || item.PartnerFirmId == partnerFirmId), cancellationToken);

            if (webhook is null)
            {
                return Result<FiscalWebhookDto>.Failure(AppErrors.NotFound("fiscal_api.webhook_not_found", "Webhook não encontrado."));
            }

            webhook.Deactivate();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(webhook);
        });

    /// <summary>Queues a "ping" so the partner can check the address and the signature.</summary>
    public Task<Result<WebhookDeliveryDto>> TestAsync(Guid establishmentId, Guid webhookId, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<WebhookDeliveryDto>(async () =>
        {
            var webhook = await webhooks.FirstOrDefaultAsync(item => item.Id == webhookId && item.EstablishmentId == establishmentId && item.IsActive && (partnerFirmId == null || item.PartnerFirmId == partnerFirmId), cancellationToken);

            if (webhook is null)
            {
                return Result<WebhookDeliveryDto>.Failure(AppErrors.NotFound("fiscal_api.webhook_not_found", "Webhook não encontrado."));
            }

            var now = clock.GetUtcNow();
            var id = GuidV7.New();
            var delivery = new FiscalWebhookDelivery(id, webhook, $"ping:{id:N}", FiscalWebhook.Ping, null, Payload(id, FiscalWebhook.Ping, now, null), now);
            deliveries.Add(delivery);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(delivery);
        });

    public Task<Result<WebhookDeliveryDto>> RetryAsync(Guid establishmentId, Guid deliveryId, CancellationToken cancellationToken, Guid? partnerFirmId = null) =>
        Guarded.RunAsync<WebhookDeliveryDto>(async () =>
        {
            var delivery = await deliveries.FirstOrDefaultAsync(item => item.Id == deliveryId && item.EstablishmentId == establishmentId, cancellationToken);

            if (delivery is not null && partnerFirmId is not null
                && !await webhooks.AnyAsync(item => item.Id == delivery.WebhookId && item.PartnerFirmId == partnerFirmId, cancellationToken))
            {
                delivery = null;
            }

            if (delivery is null)
            {
                return Result<WebhookDeliveryDto>.Failure(AppErrors.NotFound("fiscal_api.delivery_not_found", "Envio não encontrado."));
            }

            delivery.Retry(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(delivery);
        });

    /// <summary>
    /// Turns note changes into events. Reading the notes (instead of hooking every place that issues)
    /// means a note from the panel, the order flow, the contingency batch or the API all notify alike.
    /// </summary>
    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var active = await webhooks.ListAsync(item => item.IsActive, item => item.CreatedAt, cancellationToken, take: 1000);

        foreach (var listed in active)
        {
            // Lists come untracked: the webhook is read again so moving its cursor is saved.
            if (await webhooks.FirstOrDefaultAsync(item => item.Id == listed.Id, cancellationToken) is not { } webhook)
            {
                continue;
            }

            var since = webhook.ScannedUntil;
            var changed = await documents.ListAsync(
                item => item.EstablishmentId == webhook.EstablishmentId && item.UpdatedAt >= since,
                item => item.UpdatedAt,
                cancellationToken,
                take: 200);

            if (changed.Count == 0)
            {
                continue;
            }

            var candidates = changed
                .SelectMany(document => EventsOf(document).Select(item => (Document: document, item.Name, item.Key)))
                .Where(item => webhook.Wants(item.Name))
                .ToList();
            var keysToCheck = candidates.Select(item => item.Key).ToList();
            var known = (await deliveries.ListAsync(item => item.WebhookId == webhook.Id && keysToCheck.Contains(item.EventKey), item => item.CreatedAt, cancellationToken, take: 1000))
                .Select(item => item.EventKey)
                .ToHashSet(StringComparer.Ordinal);
            var now = clock.GetUtcNow();

            foreach (var (document, name, key) in candidates.Where(item => !known.Contains(item.Key)))
            {
                var id = GuidV7.New();
                // One microsecond apart (the database precision): the queue is read by time, so "authorized" leaves first.
                var at = now.AddMicroseconds(created);
                deliveries.Add(new FiscalWebhookDelivery(id, webhook, key, name, document.Id, Payload(id, name, at, document), at));
                created++;
            }

            webhook.Advance(changed[^1].UpdatedAt);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    /// <summary>Sends what is due. Each delivery is saved right after its attempt, so a crash never resends a batch.</summary>
    public async Task<int> DeliverDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await deliveries.ListAsync(item => item.Status == WebhookDeliveryStatus.Pending && item.NextAttemptAt <= now, item => item.NextAttemptAt, cancellationToken, take: 50);
        var hooks = new Dictionary<Guid, FiscalWebhook?>();
        var sent = 0;

        foreach (var listed in due)
        {
            // Read tracked, so the outcome of the attempt is saved (lists come untracked).
            if (await deliveries.FirstOrDefaultAsync(item => item.Id == listed.Id && item.Status == WebhookDeliveryStatus.Pending, cancellationToken) is not { } delivery)
            {
                continue;
            }

            if (!hooks.TryGetValue(delivery.WebhookId, out var webhook))
            {
                webhook = await webhooks.FirstOrDefaultAsync(item => item.Id == delivery.WebhookId, cancellationToken);
                hooks[delivery.WebhookId] = webhook;
            }

            if (webhook is not { IsActive: true })
            {
                delivery.Abandon("Webhook removido.");

                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            WebhookSendResult result;

            try
            {
                var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
                var signature = FiscalApiRules.Sign(protector.Unprotect(webhook.EncryptedSecret), timestamp, delivery.Payload);
                result = await sender.SendAsync(webhook.Url, delivery.Event, delivery.Id, delivery.Payload, signature, timestamp, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One broken delivery must not hold the queue of every other store.
                result = new WebhookSendResult(false, null, "Erro interno ao preparar o envio.");
            }

            if (result.Delivered)
            {
                delivery.Succeed(result.StatusCode ?? 200, clock.GetUtcNow());
                sent++;
            }
            else
            {
                delivery.Fail(result.StatusCode, result.Error ?? "Falha no envio.", clock.GetUtcNow());
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        return sent;
    }

    /// <summary>
    /// Events a note's current state implies, oldest first. A note authorized and cancelled between two
    /// scans still yields "authorized" before "cancelled"; the keys stop any of them going twice.
    /// </summary>
    public static IReadOnlyList<(string Name, string Key)> EventsOf(FiscalDocument document)
    {
        var events = new List<(string Name, string Key)>();
        var id = document.Id.ToString("N");

        switch (document.Status)
        {
            case FiscalDocumentStatus.Contingency:
                events.Add((FiscalWebhook.NoteContingency, $"{id}:contingency"));
                break;
            // A note may be refused, fixed and refused again: each refusal is its own event.
            case FiscalDocumentStatus.Rejected:
                events.Add((FiscalWebhook.NoteRejected, $"{id}:rejected:{document.UpdatedAt.ToUnixTimeMilliseconds()}"));
                break;
            case FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Cancelled:
                events.Add((FiscalWebhook.NoteAuthorized, $"{id}:authorized"));

                for (var sequence = 1; sequence <= document.CorrectionSequence; sequence++)
                {
                    events.Add((FiscalWebhook.NoteCorrected, $"{id}:cce:{sequence}"));
                }

                if (document.Status == FiscalDocumentStatus.Cancelled)
                {
                    events.Add((FiscalWebhook.NoteCancelled, $"{id}:cancelled"));
                }

                break;
        }

        return events;
    }

    private static string Payload(Guid deliveryId, string eventName, DateTimeOffset now, FiscalDocument? document) =>
        JsonSerializer.Serialize(new { id = deliveryId, @event = eventName, createdAt = now, note = document is null ? null : ApiNoteDto.From(document) }, Json);

    private static FiscalWebhookDto ToDto(FiscalWebhook webhook) => new(webhook.Id, webhook.Url, webhook.SubscribedEvents, webhook.IsActive, webhook.CreatedAt, webhook.PartnerFirmId);

    private static WebhookDeliveryDto ToDto(FiscalWebhookDelivery delivery) =>
        new(delivery.Id, delivery.WebhookId, delivery.Event, delivery.DocumentId, delivery.Status, delivery.Attempts, delivery.NextAttemptAt, delivery.LastStatusCode, delivery.LastError, delivery.CreatedAt, delivery.DeliveredAt);
}
