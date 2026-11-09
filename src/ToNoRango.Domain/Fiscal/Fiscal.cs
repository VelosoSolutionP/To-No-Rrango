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
    Rejected = 3
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
        DateTimeOffset now)
    {
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

    public void MarkUnanswered(string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Processing;
        Message = message;
        UpdatedAt = now;
    }
}
