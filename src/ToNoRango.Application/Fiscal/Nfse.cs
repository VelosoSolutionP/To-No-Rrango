using System.Globalization;
using System.Xml.Linq;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

// ---------- Contract ----------

/// <summary>Who takes (pays for) the service. The address is optional, but some cities require it.</summary>
public sealed record ServiceTaker(
    string Document,
    string Name,
    string? Email = null,
    string? Street = null,
    string? Number = null,
    string? District = null,
    string? CityIbgeCode = null,
    string? PostalCode = null);

/// <param name="ServiceCode">Código de tributação nacional (cTribNac): 6 digits, item + subitem + detail of LC 116.</param>
/// <param name="IssRate">ISS rate in percent. Leave empty when the city's rate comes from the national system.</param>
public sealed record ServiceNoteRequest(
    string ExternalId,
    ServiceTaker Taker,
    string ServiceCode,
    string Description,
    long AmountInCents,
    decimal? IssRate = null,
    bool IssWithheld = false,
    string? MunicipalServiceCode = null,
    string? NbsCode = null,
    string? ServiceCityIbgeCode = null,
    DateOnly? Competence = null);

public sealed record CancelServiceNoteRequest(string Reason);

public sealed record NfseSettingsRequest(string? MunicipalRegistration, int Series, long NextNumber);

public sealed record NfseSettingsDto(string? MunicipalRegistration, int Series, long NextNumber, int Environment, bool HasCertificate, string? CityIbgeCode);

public sealed record ServiceNoteDto(
    Guid Id,
    string ExternalId,
    int Series,
    long Number,
    string Environment,
    bool Simulation,
    string Status,
    string? AccessKey,
    string? Message,
    string TakerDocument,
    string TakerName,
    string ServiceCode,
    string Description,
    long AmountInCents,
    decimal? IssRate,
    bool IssWithheld,
    DateOnly Competence,
    string? CancelReason,
    bool HasXml,
    string? DanfseUrl,
    DateTimeOffset CreatedAt)
{
    public static ServiceNoteDto From(ServiceInvoice note) => new(
        note.Id, note.ExternalId, note.Series, note.Number, note.Environment == 1 ? "production" : "homologation", note.IsSimulation,
        FiscalApiRules.StatusName(note.Status), note.AccessKey, note.Message, note.TakerDocument, note.TakerName, note.ServiceCode,
        note.Description, note.AmountInCents, note.IssRate, note.IssWithheld, note.Competence, note.CancelReason, note.Xml is not null,
        note.AccessKey is { Length: 50 } key && !note.IsSimulation ? NfseXml.DanfseUrl(note.Environment, key) : null, note.CreatedAt);
}

public sealed record NfseResult(DfeOutcome Outcome, string Message, string? AccessKey = null, string? Xml = null);

/// <summary>Sefin Nacional: signs the XML with the store's A1 and talks over mutual TLS.</summary>
public interface INfseGateway
{
    Task<NfseResult> IssueAsync(int environment, string dpsXml, DfeCredentials credentials, CancellationToken cancellationToken);

    Task<DfeEventResult> CancelAsync(int environment, string accessKey, string eventXml, DfeCredentials credentials, CancellationToken cancellationToken);
}

// ---------- XML of the national layout (version 1.00) ----------

public static class NfseXml
{
    public const string Namespace = "http://www.sped.fazenda.gov.br/nfse";
    public const string Version = "1.00";
    public const string AppVersion = "ZEST-Fiscal-1.0";
    private static readonly XNamespace Ns = Namespace;
    private static readonly TimeSpan Brazil = TimeSpan.FromHours(-3);

    /// <summary>"DPS" + city (7) + kind of registration (1 CPF, 2 CNPJ) + registration (14) + series (5) + number (15).</summary>
    public static string DpsId(string cityIbgeCode, string cnpj, int series, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"DPS{cityIbgeCode}2{Digits(cnpj).PadLeft(14, '0')}{series:00000}{number:000000000000000}");

    public static string DanfseUrl(int environment, string accessKey) =>
        environment == 1 ? $"https://adn.nfse.gov.br/danfse/{accessKey}" : $"https://adn.producaorestrita.nfse.gov.br/danfse/{accessKey}";

    public static string Dps(FiscalProfile profile, ServiceInvoice note, ServiceNoteRequest request, DateTimeOffset now)
    {
        var taker = request.Taker;
        var takerDigits = Digits(taker.Document);
        var serviceCity = string.IsNullOrWhiteSpace(request.ServiceCityIbgeCode) ? profile.CityIbgeCode : request.ServiceCityIbgeCode;

        var takerElement = new XElement(Ns + "toma",
            new XElement(Ns + (takerDigits.Length == 14 ? "CNPJ" : "CPF"), takerDigits),
            new XElement(Ns + "xNome", Clip(taker.Name, 150)),
            taker is { CityIbgeCode.Length: 7, PostalCode: not null, Street: not null }
                ? new XElement(Ns + "end",
                    new XElement(Ns + "endNac", new XElement(Ns + "cMun", taker.CityIbgeCode), new XElement(Ns + "CEP", Digits(taker.PostalCode))),
                    new XElement(Ns + "xLgr", Clip(taker.Street, 255)),
                    new XElement(Ns + "nro", Clip(string.IsNullOrWhiteSpace(taker.Number) ? "S/N" : taker.Number, 60)),
                    new XElement(Ns + "xBairro", Clip(string.IsNullOrWhiteSpace(taker.District) ? "-" : taker.District, 60)))
                : null,
            string.IsNullOrWhiteSpace(taker.Email) ? null : new XElement(Ns + "email", Clip(taker.Email.Trim(), 80)));

        var serviceCode = new XElement(Ns + "cServ",
            new XElement(Ns + "cTribNac", request.ServiceCode),
            string.IsNullOrWhiteSpace(request.MunicipalServiceCode) ? null : new XElement(Ns + "cTribMun", request.MunicipalServiceCode.Trim()),
            new XElement(Ns + "xDescServ", Clip(request.Description.Trim(), 2000)),
            string.IsNullOrWhiteSpace(request.NbsCode) ? null : new XElement(Ns + "cNBS", Digits(request.NbsCode)));

        var municipalTax = new XElement(Ns + "tribMun",
            new XElement(Ns + "tribISSQN", 1),
            new XElement(Ns + "tpRetISSQN", request.IssWithheld ? 2 : 1),
            request.IssRate is { } rate ? new XElement(Ns + "pAliq", rate.ToString("0.00", CultureInfo.InvariantCulture)) : null);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "DPS", new XAttribute("versao", Version),
                new XElement(Ns + "infDPS", new XAttribute("Id", note.DpsId),
                    new XElement(Ns + "tpAmb", note.Environment),
                    new XElement(Ns + "dhEmi", now.ToOffset(Brazil).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
                    new XElement(Ns + "verAplic", AppVersion),
                    new XElement(Ns + "serie", note.Series),
                    new XElement(Ns + "nDPS", note.Number),
                    new XElement(Ns + "dCompet", note.Competence.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement(Ns + "tpEmit", 1),
                    new XElement(Ns + "cLocEmi", profile.CityIbgeCode),
                    new XElement(Ns + "prest",
                        new XElement(Ns + "CNPJ", Digits(profile.Cnpj)),
                        profile.MunicipalRegistration is null ? null : new XElement(Ns + "IM", profile.MunicipalRegistration),
                        // Presumido and Real only: never Simples Nacional.
                        new XElement(Ns + "regTrib", new XElement(Ns + "opSimpNac", 1), new XElement(Ns + "regEspTrib", 0))),
                    takerElement,
                    new XElement(Ns + "serv", new XElement(Ns + "locPrest", new XElement(Ns + "cLocPrestacao", serviceCity)), serviceCode),
                    new XElement(Ns + "valores",
                        new XElement(Ns + "vServPrest", new XElement(Ns + "vServ", Money(note.AmountInCents))),
                        new XElement(Ns + "trib", municipalTax, new XElement(Ns + "totTrib", new XElement(Ns + "indTotTrib", 0)))))));

        return document.Declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Cancellation request (event 101101). Id: "PRE" + access key (50) + event (6) + request number (3).</summary>
    public static string CancelEvent(int environment, string cnpj, string accessKey, string reason, DateTimeOffset now, int requestNumber = 1)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "pedRegEvento", new XAttribute("versao", Version),
                new XElement(Ns + "infPedReg", new XAttribute("Id", string.Create(CultureInfo.InvariantCulture, $"PRE{accessKey}101101{requestNumber:000}")),
                    new XElement(Ns + "tpAmb", environment),
                    new XElement(Ns + "verAplic", AppVersion),
                    new XElement(Ns + "dhEvento", now.ToOffset(Brazil).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
                    new XElement(Ns + "CNPJAutor", Digits(cnpj)),
                    new XElement(Ns + "chNFSe", accessKey),
                    new XElement(Ns + "nPedRegEvento", requestNumber),
                    new XElement(Ns + "e101101",
                        new XElement(Ns + "xDesc", "Cancelamento de NFS-e"),
                        new XElement(Ns + "cMotivo", 1),
                        new XElement(Ns + "xMotivo", Clip(reason, 255))))));

        return document.Declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    public static string? Validate(ServiceNoteRequest request)
    {
        var externalId = request.ExternalId?.Trim() ?? string.Empty;

        if (externalId.Length is < 1 or > 40 || !externalId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/'))
        {
            return "externalId é obrigatório: até 40 caracteres, só letras, números, hífen, ponto, barra e sublinhado.";
        }

        if (request.Taker is null)
        {
            return "Informe o tomador do serviço.";
        }

        var document = Digits(request.Taker.Document);

        if (!(document.Length == 14 ? DocumentValidator.IsValidCnpj(document) : document.Length == 11 && DocumentValidator.IsValidCpf(document)))
        {
            return "CPF ou CNPJ do tomador inválido.";
        }

        if (string.IsNullOrWhiteSpace(request.Taker.Name))
        {
            return "Informe o nome do tomador.";
        }

        if (request.Taker.CityIbgeCode is { Length: > 0 } city && (city.Length != 7 || !city.All(char.IsAsciiDigit)))
        {
            return "Código IBGE da cidade do tomador tem 7 dígitos.";
        }

        if (request.ServiceCode is not { Length: 6 } || !request.ServiceCode.All(char.IsAsciiDigit))
        {
            return "Código de tributação nacional (cTribNac) tem 6 dígitos: item, subitem e desdobro da LC 116 (ex.: 010101).";
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 2000)
        {
            return "Descreva o serviço (até 2000 caracteres).";
        }

        if (request.AmountInCents <= 0)
        {
            return "O valor do serviço precisa ser maior que zero.";
        }

        // ISS: 2% minimum and 5% maximum (LC 116, art. 8 and 8-A).
        if (request.IssRate is { } rate && (rate < 2 || rate > 5))
        {
            return "A alíquota do ISS vai de 2% a 5%.";
        }

        if (request.ServiceCityIbgeCode is { Length: > 0 } place && (place.Length != 7 || !place.All(char.IsAsciiDigit)))
        {
            return "Código IBGE do local da prestação tem 7 dígitos.";
        }

        return null;
    }

    public static string Money(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}

// ---------- Service ----------

public sealed class NfseService(
    IRepository<FiscalProfile> profiles,
    IRepository<ServiceInvoice> notes,
    INfseGateway gateway,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<NfseSettingsDto>> GetSettingsAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken) is { } profile
            ? Settings(profile)
            : Result<NfseSettingsDto>.Failure(AppErrors.Conflict("fiscal.no_profile", "Preencha os dados do emitente antes."));

    public Task<Result<NfseSettingsDto>> SaveSettingsAsync(Guid establishmentId, NfseSettingsRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<NfseSettingsDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            profile.SetServiceSettings(request.MunicipalRegistration, request.Series, request.NextNumber);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Settings(profile);
        });

    public async Task<IReadOnlyList<ServiceNoteDto>> ListAsync(Guid establishmentId, bool simulation, CancellationToken cancellationToken, int take = 100) =>
        (await notes.ListAsync(item => item.EstablishmentId == establishmentId && item.IsSimulation == simulation, item => item.CreatedAt, cancellationToken, descending: true, take: Math.Clamp(take, 1, 500)))
            .Select(ServiceNoteDto.From)
            .ToList();

    public async Task<Result<ServiceNoteDto>> GetAsync(Guid establishmentId, Guid id, bool simulation, CancellationToken cancellationToken) =>
        await notes.FirstOrDefaultAsync(item => item.Id == id && item.EstablishmentId == establishmentId && item.IsSimulation == simulation, cancellationToken) is { } note
            ? ServiceNoteDto.From(note)
            : Result<ServiceNoteDto>.Failure(NotFound);

    public async Task<Result<string>> GetXmlAsync(Guid establishmentId, Guid id, bool simulation, CancellationToken cancellationToken) =>
        await notes.FirstOrDefaultAsync(item => item.Id == id && item.EstablishmentId == establishmentId && item.IsSimulation == simulation, cancellationToken) switch
        {
            null => Result<string>.Failure(NotFound),
            { Xml: { } xml } => xml,
            _ => Result<string>.Failure(AppErrors.NotFound("nfse.no_xml", "Esta NFS-e não tem XML (simulada ou ainda não autorizada).")),
        };

    /// <summary>
    /// Issues one NFS-e. The external id makes it idempotent; a simulation (test key) never reaches the
    /// government and uses its own numbering, outside the store's sequence.
    /// </summary>
    public Task<Result<ServiceNoteDto>> IssueAsync(Guid establishmentId, ServiceNoteRequest request, bool simulate, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ServiceNoteDto>(async () =>
        {
            if (NfseXml.Validate(request) is { } error)
            {
                return Result<ServiceNoteDto>.Failure(AppErrors.Validation(error));
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var externalId = request.ExternalId.Trim();
            var existing = await notes.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.ExternalId == externalId && item.IsSimulation == simulate, cancellationToken);

            if (existing is { Status: FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Cancelled })
            {
                return ServiceNoteDto.From(existing);
            }

            if (!simulate && !profile.HasCertificate)
            {
                return Result<ServiceNoteDto>.Failure(AppErrors.Conflict("nfse.no_certificate", "Envie o certificado A1 da loja antes de emitir NFS-e."));
            }

            var now = clock.GetUtcNow();
            var note = existing;

            if (note is null)
            {
                var environment = simulate ? 2 : profile.Environment;
                var series = simulate ? FiscalApiRules.SimulationSeries : profile.ServiceSeries;
                var number = simulate
                    ? await notes.CountAsync(item => item.EstablishmentId == establishmentId && item.IsSimulation, cancellationToken) + 1
                    : profile.TakeServiceNumber();
                var competence = request.Competence ?? DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-3)).DateTime);

                note = new ServiceInvoice(
                    profile.TenantId, establishmentId, externalId, series, number, environment, NfseXml.DpsId(profile.CityIbgeCode, profile.Cnpj, series, number),
                    new string(request.Taker.Document.Where(char.IsAsciiDigit).ToArray()), request.Taker.Name, request.ServiceCode, request.Description,
                    request.AmountInCents, request.IssRate, request.IssWithheld, competence, now, simulate);
                notes.Add(note);
            }
            else
            {
                note.Restart(now);
            }

            // The number is saved before sending: if the call falls in the middle, the retry keeps it.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (simulate)
            {
                note.Authorize(SimulatedKey(note), null, "Simulação: nada foi enviado ao Sefin Nacional.", clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return ServiceNoteDto.From(note);
            }

            var xml = NfseXml.Dps(profile, note, request, now);
            var result = await gateway.IssueAsync(note.Environment, xml, Credentials(profile), CancellationToken.None);

            switch (result.Outcome)
            {
                case DfeOutcome.Authorized:
                    note.Authorize(result.AccessKey!, result.Xml, result.Message, clock.GetUtcNow());
                    break;
                case DfeOutcome.Rejected:
                    note.Reject(result.Message, clock.GetUtcNow());
                    break;
                default:
                    note.MarkUnanswered(result.Message, clock.GetUtcNow());
                    break;
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return ServiceNoteDto.From(note);
        });

    public Task<Result<ServiceNoteDto>> CancelAsync(Guid establishmentId, Guid id, CancelServiceNoteRequest request, bool simulation, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ServiceNoteDto>(async () =>
        {
            var reason = (request.Reason ?? string.Empty).Trim();

            if (reason.Length is < 15 or > 255)
            {
                return Result<ServiceNoteDto>.Failure(AppErrors.Validation("A justificativa do cancelamento precisa ter de 15 a 255 caracteres."));
            }

            var note = await notes.FirstOrDefaultAsync(item => item.Id == id && item.EstablishmentId == establishmentId && item.IsSimulation == simulation, cancellationToken);

            if (note is not { Status: FiscalDocumentStatus.Authorized, AccessKey: not null })
            {
                return Result<ServiceNoteDto>.Failure(AppErrors.Conflict("nfse.not_cancellable", "Só uma NFS-e autorizada pode ser cancelada."));
            }

            if (note.IsSimulation)
            {
                note.Cancel(reason, null, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return ServiceNoteDto.From(note);
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var eventXml = NfseXml.CancelEvent(note.Environment, profile.Cnpj, note.AccessKey, reason, clock.GetUtcNow());
            var answer = await gateway.CancelAsync(note.Environment, note.AccessKey, eventXml, Credentials(profile), CancellationToken.None);

            if (!answer.Accepted)
            {
                return Result<ServiceNoteDto>.Failure(AppErrors.Conflict("nfse.cancel_refused", $"O Sefin Nacional não aceitou o cancelamento: {answer.Message}"));
            }

            note.Cancel(reason, answer.Xml, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return ServiceNoteDto.From(note);
        });

    /// <summary>50 digits that never existed at the government: city + environment + made-up rest from the DPS.</summary>
    public static string SimulatedKey(ServiceInvoice note)
    {
        var city = note.DpsId.Length >= 10 ? note.DpsId[3..10] : "0000000";
        var tail = string.Create(CultureInfo.InvariantCulture, $"{note.Number:000000000000000}{note.Series:00000}");
        var body = $"{city}2{note.DpsId[11..25]}{tail}".PadRight(49, '9')[..49];
        return body + FiscalApiRules.CheckDigit(body);
    }

    private static NfseSettingsDto Settings(FiscalProfile profile) =>
        new(profile.MunicipalRegistration, profile.ServiceSeries, profile.NextServiceNumber, profile.Environment, profile.HasCertificate, profile.CityIbgeCode);

    private DfeCredentials Credentials(FiscalProfile profile) =>
        new(
            Convert.FromBase64String(protector.Unprotect(profile.EncryptedCertificate!)),
            protector.Unprotect(profile.EncryptedCertificatePassword!),
            null,
            null);

    private async Task<FiscalProfile> RequireProfileAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken)
            ?? throw new InvalidOperationException("Preencha os dados do emitente antes.");

    private static readonly Error NotFound = AppErrors.NotFound("nfse.not_found", "NFS-e não encontrada.");
}
