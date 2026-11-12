using ToNoRango.Domain.Common;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Fiscal;

/// <summary>The fiscal modules serve only these regimes; any other company is refused.</summary>
public enum TaxRegime
{
    LucroPresumido = 1,
    LucroReal = 2
}

public enum FiscalDocumentStatus
{
    /// <summary>Numbered and possibly sent; the SEFAZ answer is not known yet.</summary>
    Processing = 1,
    Authorized = 2,
    Rejected = 3,
    Cancelled = 4,

    /// <summary>NFC-e issued offline while the SEFAZ was out of reach; it must be sent within 24 hours.</summary>
    Contingency = 5
}

/// <summary>Issuer data of one establishment. The A1 certificate and the CSC stay in ACBrMonitor.</summary>
public sealed class FiscalProfile : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string Cnpj { get; private set; } = string.Empty;
    public string StateRegistration { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string CityIbgeCode { get; private set; } = string.Empty;
    public int Series { get; private set; } = 1;
    public int NextNumber { get; private set; } = 1;
    public TaxRegime? TaxRegime { get; private set; }
    public string? AccountantName { get; private set; }
    public string? AccountantCpf { get; private set; }
    public string? AccountantCrc { get; private set; }
    public string? AccountantEmail { get; private set; }
    public string? SpedProfile { get; private set; }

    /// <summary>A1 certificate (PFX), encrypted. With it the system talks to the SEFAZ itself, without ACBr.</summary>
    public string? EncryptedCertificate { get; private set; }
    public string? EncryptedCertificatePassword { get; private set; }
    public string? CertificateSubject { get; private set; }
    public DateTimeOffset? CertificateExpiresAt { get; private set; }

    /// <summary>NFC-e security code (CSC) given by the SEFAZ: id and the encrypted token.</summary>
    public string? CscId { get; private set; }
    public string? EncryptedCscToken { get; private set; }

    /// <summary>1 = production, 2 = homologation. Starts in homologation.</summary>
    public int Environment { get; private set; } = 2;
    public int NfeSeries { get; private set; } = 1;
    public int NfeNextNumber { get; private set; } = 1;

    /// <summary>NFS-e (padrão nacional): municipal registration, DPS series and the next DPS number.</summary>
    public string? MunicipalRegistration { get; private set; }
    public int ServiceSeries { get; private set; } = 1;
    public long NextServiceNumber { get; private set; } = 1;

    public long TakeServiceNumber() => NextServiceNumber++;

    public void SetServiceSettings(string? municipalRegistration, int series, long nextNumber)
    {
        // DPS series issued by the taxpayer's own system go from 1 to 49999 (50000 and up belong to the government apps).
        if (series is < 1 or > 49_999)
        {
            throw new ArgumentException("A série da DPS vai de 1 a 49999.", nameof(series));
        }

        if (nextNumber is < 1 or > 999_999_999_999_999)
        {
            throw new ArgumentException("Próximo número da DPS inválido.", nameof(nextNumber));
        }

        MunicipalRegistration = string.IsNullOrWhiteSpace(municipalRegistration) ? null : Ensure.MaxLength(municipalRegistration.Trim(), 15, nameof(municipalRegistration));
        ServiceSeries = series;
        NextServiceNumber = nextNumber;
    }

    /// <summary>Último NSU baixado da distribuição DF-e e quando a SEFAZ pode ser consultada de novo.</summary>
    public long LastNsu { get; private set; }
    public DateTimeOffset? NextDistributionAt { get; private set; }

    public void SetDistribution(long lastNsu, DateTimeOffset? nextAt)
    {
        LastNsu = Math.Max(LastNsu, lastNsu);
        NextDistributionAt = nextAt;
    }

    public bool HasCertificate => EncryptedCertificate is not null && EncryptedCertificatePassword is not null;

    private FiscalProfile() { }

    public FiscalProfile(Guid tenantId, Guid establishmentId)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
    }

    public void Update(
        string legalName,
        string? tradeName,
        string cnpj,
        string stateRegistration,
        string street,
        string number,
        string district,
        string city,
        string state,
        string postalCode,
        string cityIbgeCode,
        int series,
        int nextNumber)
    {
        var digits = Digits(cnpj);

        if (!DocumentValidator.IsValidCnpj(digits))
        {
            throw new ArgumentException("CNPJ inválido.", nameof(cnpj));
        }

        var registration = Digits(stateRegistration);
        var cep = Digits(postalCode);
        var ibge = Digits(cityIbgeCode);

        if (registration.Length is < 2 or > 14)
        {
            throw new ArgumentException("Informe a inscrição estadual.", nameof(stateRegistration));
        }

        if (cep.Length != 8)
        {
            throw new ArgumentException("CEP inválido.", nameof(postalCode));
        }

        if (ibge.Length != 7)
        {
            throw new ArgumentException("O código IBGE do município tem 7 dígitos.", nameof(cityIbgeCode));
        }

        if (state.Trim().Length != 2)
        {
            throw new ArgumentException("Informe a UF com 2 letras.", nameof(state));
        }

        if (series is < 1 or > 889)
        {
            throw new ArgumentException("A série da NFC-e vai de 1 a 889.", nameof(series));
        }

        if (nextNumber is < 1 or > 999_999_999)
        {
            throw new ArgumentException("Próximo número inválido.", nameof(nextNumber));
        }

        LegalName = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(legalName, nameof(LegalName)).Trim(), 60, nameof(LegalName));
        TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : Ensure.MaxLength(tradeName.Trim(), 60, nameof(TradeName));
        Cnpj = digits;
        StateRegistration = registration;
        Street = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(street, nameof(Street)).Trim(), 60, nameof(Street));
        Number = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(number, nameof(Number)).Trim(), 60, nameof(Number));
        District = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(district, nameof(District)).Trim(), 60, nameof(District));
        City = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(city, nameof(City)).Trim(), 60, nameof(City));
        State = state.Trim().ToUpperInvariant();
        PostalCode = cep;
        CityIbgeCode = ibge;
        Series = series;
        NextNumber = nextNumber;
    }

    public void SetRegime(TaxRegime regime)
    {
        if (regime is not (Fiscal.TaxRegime.LucroPresumido or Fiscal.TaxRegime.LucroReal))
        {
            throw new ArgumentException("O módulo fiscal atende somente Lucro Presumido e Lucro Real.", nameof(regime));
        }

        TaxRegime = regime;
    }

    public void SetAccounting(string? name, string? cpf, string? crc, string? email, string? spedProfile)
    {
        var profile = string.IsNullOrWhiteSpace(spedProfile) ? null : spedProfile.Trim().ToUpperInvariant();

        if (profile is not (null or "A" or "B" or "C"))
        {
            throw new ArgumentException("O perfil do SPED é A, B ou C.", nameof(spedProfile));
        }

        var digits = string.IsNullOrWhiteSpace(cpf) ? null : Digits(cpf);

        if (digits is not null && !DocumentValidator.IsValidCpf(digits))
        {
            throw new ArgumentException("CPF do contador inválido.", nameof(cpf));
        }

        AccountantName = Clean(name, 100);
        AccountantCpf = digits;
        AccountantCrc = Clean(crc, 15);
        AccountantEmail = Clean(email, 115);
        SpedProfile = profile;
    }

    public bool HasAccounting => AccountantName is not null && AccountantCpf is not null && AccountantCrc is not null && AccountantEmail is not null && SpedProfile is not null;

    public int TakeNumber() => NextNumber++;

    public int TakeNfeNumber() => NfeNextNumber++;

    public void SetCertificate(string encryptedPfx, string encryptedPassword, string subject, DateTimeOffset expiresAt)
    {
        EncryptedCertificate = Ensure.NotNullOrWhiteSpace(encryptedPfx, nameof(encryptedPfx));
        EncryptedCertificatePassword = Ensure.NotNullOrWhiteSpace(encryptedPassword, nameof(encryptedPassword));
        CertificateSubject = Ensure.MaxLength(subject, 300, nameof(subject));
        CertificateExpiresAt = expiresAt;
    }

    public void RemoveCertificate()
    {
        EncryptedCertificate = null;
        EncryptedCertificatePassword = null;
        CertificateSubject = null;
        CertificateExpiresAt = null;
    }

    public void SetCsc(string? cscId, string? encryptedToken)
    {
        var id = string.IsNullOrWhiteSpace(cscId) ? null : Digits(cscId);

        if (id is not null && (id.Length is < 1 or > 6))
        {
            throw new ArgumentException("O identificador do CSC tem até 6 dígitos.", nameof(cscId));
        }

        CscId = id;
        EncryptedCscToken = id is null ? null : encryptedToken ?? EncryptedCscToken;
    }

    public void SetEnvironment(int environment, int nfeSeries, int nfeNextNumber)
    {
        if (environment is not (1 or 2))
        {
            throw new ArgumentException("Ambiente fiscal: 1 = produção, 2 = homologação.", nameof(environment));
        }

        if (nfeSeries is < 1 or > 889)
        {
            throw new ArgumentException("A série da NF-e vai de 1 a 889.", nameof(nfeSeries));
        }

        if (nfeNextNumber is < 1 or > 999_999_999)
        {
            throw new ArgumentException("Próximo número da NF-e inválido.", nameof(nfeNextNumber));
        }

        Environment = environment;
        NfeSeries = nfeSeries;
        NfeNextNumber = nfeNextNumber;
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Ensure.MaxLength(value.Trim(), max, nameof(value));

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}

/// <summary>Tax classification of a menu item for Lucro Presumido or Real, filled in with the accountant.</summary>
public sealed class ProductFiscalData : AggregateRoot
{
    private static readonly string[] IcmsCodes = ["00", "40", "41", "60"];
    private static readonly string[] PisCofinsCodes = ["01", "02", "04", "06", "07", "08", "09"];

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Ncm { get; private set; } = string.Empty;
    public string Cfop { get; private set; } = string.Empty;
    public string? Cest { get; private set; }
    public string IcmsCst { get; private set; } = string.Empty;
    public decimal IcmsRate { get; private set; }
    public string PisCofinsCst { get; private set; } = string.Empty;
    public decimal PisRate { get; private set; }
    public decimal CofinsRate { get; private set; }

    public bool HasPisCofinsValues => PisCofinsCst is "01" or "02";

    private ProductFiscalData() { }

    public ProductFiscalData(Guid tenantId, Guid establishmentId, Guid menuItemId)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        MenuItemId = Ensure.NotEmpty(menuItemId, nameof(MenuItemId));
    }

    public void Update(string ncm, string cfop, string? cest, string icmsCst, decimal icmsRate, string pisCofinsCst, decimal pisRate, decimal cofinsRate)
    {
        var cleanNcm = (ncm ?? string.Empty).Replace(".", string.Empty).Trim();
        var cleanCfop = (cfop ?? string.Empty).Replace(".", string.Empty).Trim();
        var cleanCest = (cest ?? string.Empty).Replace(".", string.Empty).Trim();
        var cleanIcms = (icmsCst ?? string.Empty).Trim();
        var cleanPisCofins = (pisCofinsCst ?? string.Empty).Trim();

        if (cleanNcm.Length != 8 || !cleanNcm.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("O NCM tem 8 dígitos.", nameof(ncm));
        }

        // A NFC-e só acoberta venda dentro do estado.
        if (cleanCfop.Length != 4 || cleanCfop[0] != '5' || !cleanCfop.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Na NFC-e o CFOP tem 4 dígitos e começa com 5.", nameof(cfop));
        }

        if (cleanCest.Length != 0 && (cleanCest.Length != 7 || !cleanCest.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("O CEST tem 7 dígitos.", nameof(cest));
        }

        if (!IcmsCodes.Contains(cleanIcms))
        {
            throw new ArgumentException("CST de ICMS aceito: 00, 40, 41 ou 60.", nameof(icmsCst));
        }

        if (!PisCofinsCodes.Contains(cleanPisCofins))
        {
            throw new ArgumentException("CST de PIS/COFINS aceito: 01, 02, 04, 06, 07, 08 ou 09.", nameof(pisCofinsCst));
        }

        if (icmsRate is < 0 or > 100 || pisRate is < 0 or > 100 || cofinsRate is < 0 or > 100)
        {
            throw new ArgumentException("A alíquota vai de 0 a 100.", nameof(icmsRate));
        }

        if ((cleanIcms == "00") != (icmsRate > 0))
        {
            throw new ArgumentException("Com CST 00 informe a alíquota de ICMS; nos demais ela fica zerada.", nameof(icmsRate));
        }

        if (cleanPisCofins is "01" or "02" ? pisRate <= 0 || cofinsRate <= 0 : pisRate != 0 || cofinsRate != 0)
        {
            throw new ArgumentException("Com CST 01 ou 02 informe PIS e COFINS; nos demais as alíquotas ficam zeradas.", nameof(pisRate));
        }

        Ncm = cleanNcm;
        Cfop = cleanCfop;
        Cest = cleanCest.Length == 0 ? null : cleanCest;
        IcmsCst = cleanIcms;
        IcmsRate = icmsRate;
        PisCofinsCst = cleanPisCofins;
        PisRate = pisRate;
        CofinsRate = cofinsRate;
    }
}

/// <summary>The NFC-e of one order. Retries keep the number, so a lost answer never issues a second note.</summary>
public sealed class FiscalDocument : AggregateRoot
{
    public const int NfceModel = 65;
    public const int NfeModel = 55;

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderCode { get; private set; } = string.Empty;
    public int Model { get; private set; } = NfceModel;
    public int Series { get; private set; }
    public int Number { get; private set; }
    public int Environment { get; private set; }
    public FiscalDocumentStatus Status { get; private set; }
    public string? AccessKey { get; private set; }
    public string? Protocol { get; private set; }
    public string? Xml { get; private set; }
    public string? Message { get; private set; }
    public long TotalInCents { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? RecipientDocument { get; private set; }
    public string? RecipientName { get; private set; }
    public string? CancelProtocol { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>When the note was signed offline (NFC-e contingency); the SEFAZ counts the 24 hours from here.</summary>
    public DateTimeOffset? ContingencyAt { get; private set; }

    /// <summary>How many correction letters (CC-e) the SEFAZ registered; each one replaces the previous.</summary>
    public int CorrectionSequence { get; private set; }

    /// <summary>finNFe: 1 normal, 2 complementar, 3 ajuste, 4 devolução.</summary>
    public int Purpose { get; private set; } = 1;
    public string? ReferencedKey { get; private set; }

    /// <summary>Issued with a test key of the fiscal API: never sent to the SEFAZ and outside every book.</summary>
    public bool IsSimulation { get; private set; }
    public string? CorrectionText { get; private set; }
    public string? CorrectionProtocol { get; private set; }
    public DateTimeOffset? CorrectedAt { get; private set; }

    /// <summary>The SEFAZ accepts at most 20 correction letters per note.</summary>
    public const int MaxCorrections = 20;

    private FiscalDocument() { }

    public FiscalDocument(
        Guid tenantId,
        Guid establishmentId,
        Guid orderId,
        string orderCode,
        int series,
        int number,
        int environment,
        long totalInCents,
        DateTimeOffset now,
        int model = NfceModel,
        string? recipientDocument = null,
        string? recipientName = null,
        int purpose = 1,
        string? referencedKey = null)
    {
        if (purpose is < 1 or > 4)
        {
            throw new ArgumentException("Finalidade da nota inválida.", nameof(purpose));
        }

        Purpose = purpose;
        ReferencedKey = referencedKey;
        if (model is not (NfceModel or NfeModel))
        {
            throw new ArgumentException("Modelo fiscal inválido.", nameof(model));
        }

        Model = model;
        RecipientDocument = recipientDocument;
        RecipientName = recipientName;
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderId = Ensure.NotEmpty(orderId, nameof(OrderId));
        OrderCode = orderCode;
        Series = series;
        Number = number;
        Environment = environment;
        TotalInCents = totalInCents;
        Status = FiscalDocumentStatus.Processing;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Stable across retries: the access key must be the same every time this note is sent.</summary>
    public int NumericCode => 10_000_000 + (int)(BitConverter.ToUInt32(Id.ToByteArray(), 12) % 89_999_999);

    public void Restart(int environment, long totalInCents, DateTimeOffset now)
    {
        if (Status == FiscalDocumentStatus.Authorized)
        {
            throw new InvalidOperationException("Este pedido já tem NFC-e autorizada.");
        }

        Environment = environment;
        TotalInCents = totalInCents;
        Status = FiscalDocumentStatus.Processing;
        Message = null;
        UpdatedAt = now;
    }

    public void Authorize(string? accessKey, string? protocol, string? xml, string? message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Authorized;
        AccessKey = accessKey;
        Protocol = protocol;
        Xml = xml;
        Message = message;
        UpdatedAt = now;
    }

    public void Reject(string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Rejected;
        Message = message;
        UpdatedAt = now;
    }

    /// <summary>Signed offline (NFC-e contingency). The key and XML are final; only the SEFAZ protocol is missing.</summary>
    public void MarkContingency(string accessKey, string xml, string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Contingency;
        AccessKey = accessKey;
        Xml = xml;
        Message = message;
        ContingencyAt ??= now;
        UpdatedAt = now;
    }

    public void Cancel(string protocol, string reason, string? eventXml, DateTimeOffset now)
    {
        if (Status != FiscalDocumentStatus.Authorized)
        {
            throw new InvalidOperationException("Só uma nota autorizada pode ser cancelada.");
        }

        Status = FiscalDocumentStatus.Cancelled;
        CancelProtocol = protocol;
        CancelReason = reason;
        CancelledAt = now;
        Message = "Cancelada.";
        Xml = eventXml is null ? Xml : $"{Xml}\n<!-- evento de cancelamento -->\n{eventXml}";
        UpdatedAt = now;
    }

    /// <summary>A correction letter registered at the SEFAZ (NF-e only). The newest text is the one that counts.</summary>
    public void Correct(int sequence, string text, string protocol, string? eventXml, DateTimeOffset now)
    {
        if (Model != NfeModel || Status != FiscalDocumentStatus.Authorized)
        {
            throw new InvalidOperationException("Carta de correção só vale para NF-e autorizada.");
        }

        if (sequence != CorrectionSequence + 1 || sequence > MaxCorrections)
        {
            throw new InvalidOperationException("Sequência da carta de correção fora de ordem.");
        }

        CorrectionSequence = sequence;
        CorrectionText = text;
        CorrectionProtocol = protocol;
        CorrectedAt = now;
        Xml = eventXml is null ? Xml : $"{Xml}\n<!-- carta de correcao {sequence} -->\n{eventXml}";
        UpdatedAt = now;
    }

    public const string SimulatedProtocol = "SIMULACAO";

    /// <summary>A test-key note: authorized on the spot with a made-up key, nothing reaches the SEFAZ.</summary>
    public void Simulate(string accessKey, DateTimeOffset now)
    {
        if (Environment != 2)
        {
            throw new InvalidOperationException("Nota simulada só existe em homologação.");
        }

        IsSimulation = true;
        Authorize(accessKey, SimulatedProtocol, null, "Simulação: nada foi enviado à SEFAZ.", now);
    }

    public void MarkUnanswered(string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Processing;
        Message = message;
        UpdatedAt = now;
    }
}

public enum NumberGapStatus
{
    Pending = 1,
    Done = 2,
    Rejected = 3
}

/// <summary>
/// A range of numbers given up at the SEFAZ (inutilização): numbers skipped by a failure that
/// will never be used, so the sequence has no unexplained holes.
/// </summary>
public sealed class FiscalNumberGap : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public int Model { get; private set; }
    public int Series { get; private set; }
    public int FromNumber { get; private set; }
    public int ToNumber { get; private set; }
    public int Environment { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public NumberGapStatus Status { get; private set; }
    public string? Protocol { get; private set; }
    public string? Message { get; private set; }
    public string? Xml { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private FiscalNumberGap() { }

    public FiscalNumberGap(Guid tenantId, Guid establishmentId, int model, int series, int fromNumber, int toNumber, int environment, string reason, DateTimeOffset now)
    {
        if (model is not (FiscalDocument.NfceModel or FiscalDocument.NfeModel))
        {
            throw new ArgumentException("Modelo fiscal inválido.", nameof(model));
        }

        if (fromNumber < 1 || toNumber < fromNumber || toNumber > 999_999_999)
        {
            throw new ArgumentException("Faixa de numeração inválida.", nameof(fromNumber));
        }

        var why = Ensure.NotNullOrWhiteSpace(reason, nameof(reason)).Trim();

        if (why.Length is < 15 or > 255)
        {
            throw new ArgumentException("A justificativa precisa ter de 15 a 255 caracteres.", nameof(reason));
        }

        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Model = model;
        Series = series;
        FromNumber = fromNumber;
        ToNumber = toNumber;
        Environment = environment;
        Reason = why;
        Status = NumberGapStatus.Pending;
        CreatedAt = now;
    }

    public void Finish(bool accepted, string? protocol, string message, string? xml)
    {
        Status = accepted ? NumberGapStatus.Done : NumberGapStatus.Rejected;
        Protocol = protocol;
        Message = message;
        Xml = xml;
    }
}

public enum ReceivedNoteStatus
{
    /// <summary>Só o resumo chegou; a SEFAZ libera o XML completo depois da ciência da operação.</summary>
    Summary = 1,
    Full = 2,
    Imported = 3
}

/// <summary>
/// NF-e emitida contra o CNPJ da loja, baixada da SEFAZ (distribuição DF-e) só com o certificado.
/// A loja confere e leva para Compras; nada entra no estoque sozinho.
/// </summary>
public sealed class ReceivedNote : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public long Nsu { get; private set; }
    public string AccessKey { get; private set; } = string.Empty;
    public string IssuerDocument { get; private set; } = string.Empty;
    public string IssuerName { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public long TotalInCents { get; private set; }

    /// <summary>cSitNFe do resumo: 1 autorizada, 2 denegada, 3 cancelada.</summary>
    public int Situation { get; private set; } = 1;
    public ReceivedNoteStatus Status { get; private set; }
    public string? Xml { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? LastEvent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ReceivedNote() { }

    public ReceivedNote(Guid tenantId, Guid establishmentId, long nsu, string accessKey, string issuerDocument, string issuerName, DateTimeOffset issuedAt, long totalInCents, int situation, string? fullXml, DateTimeOffset now)
    {
        if (accessKey.Length != 44)
        {
            throw new ArgumentException("Chave de acesso inválida.", nameof(accessKey));
        }

        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Nsu = nsu;
        AccessKey = accessKey;
        IssuerDocument = issuerDocument;
        IssuerName = issuerName.Length > 120 ? issuerName[..120] : issuerName;
        IssuedAt = issuedAt;
        TotalInCents = totalInCents;
        Situation = situation;
        Xml = fullXml;
        Status = fullXml is null ? ReceivedNoteStatus.Summary : ReceivedNoteStatus.Full;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void AttachXml(long nsu, string xml, DateTimeOffset now)
    {
        Nsu = Math.Max(Nsu, nsu);
        Xml = xml;

        if (Status == ReceivedNoteStatus.Summary)
        {
            Status = ReceivedNoteStatus.Full;
        }

        UpdatedAt = now;
    }

    public void Acknowledge(DateTimeOffset now)
    {
        AcknowledgedAt = now;
        UpdatedAt = now;
    }

    public void RecordEvent(string description, bool cancelled, DateTimeOffset now)
    {
        LastEvent = description.Length > 120 ? description[..120] : description;

        if (cancelled)
        {
            Situation = 3;
        }

        UpdatedAt = now;
    }

    public void MarkImported(DateTimeOffset now)
    {
        Status = ReceivedNoteStatus.Imported;
        UpdatedAt = now;
    }
}

/// <summary>
/// Who did what in the fiscal area and when: emission, cancellation, correction, number gaps,
/// certificate, files for the tax authority and the accountant package. Never updated, never deleted.
/// </summary>
public sealed class FiscalAuditEntry : AggregateRoot
{
    public const string FileCategory = "arquivo";

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? Target { get; private set; }

    /// <summary>Competence (yyyy-MM) when the operation refers to a month.</summary>
    public string? Period { get; private set; }
    public string? Details { get; private set; }
    public bool Succeeded { get; private set; }
    public int StatusCode { get; private set; }

    private FiscalAuditEntry() { }

    public FiscalAuditEntry(Guid tenantId, Guid establishmentId, DateTimeOffset at, string actor, string category, string action, string? target, string? period, string? details, bool succeeded, int statusCode)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        At = at;
        Actor = Clip(string.IsNullOrWhiteSpace(actor) ? "desconhecido" : actor.Trim(), 160)!;
        Category = Clip(Ensure.NotNullOrWhiteSpace(category, nameof(category)).Trim(), 40)!;
        Action = Clip(Ensure.NotNullOrWhiteSpace(action, nameof(action)).Trim(), 120)!;
        Target = Clip(target, 120);
        Period = Clip(period, 7);
        Details = Clip(details, 1000);
        Succeeded = succeeded;
        StatusCode = statusCode;
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
