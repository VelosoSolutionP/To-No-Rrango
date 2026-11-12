namespace ToNoRango.Application.Fiscal;

/// <summary>The A1 certificate and, for NFC-e, the CSC of one establishment, already decrypted for this call only.</summary>
public sealed record DfeCredentials(byte[] Pfx, string Password, string? CscId, string? CscToken);

public sealed record DfeIssuer(
    string Cnpj,
    string LegalName,
    string? TradeName,
    string StateRegistration,
    string Street,
    string Number,
    string District,
    string CityIbgeCode,
    string City,
    string State,
    string PostalCode);

/// <summary>Who buys. Required for NF-e (model 55); optional CPF/CNPJ on NFC-e.</summary>
public sealed record DfeRecipient(
    string Document,
    string Name,
    string? StateRegistration,
    string? Street,
    string? Number,
    string? District,
    string? CityIbgeCode,
    string? City,
    string? State,
    string? PostalCode,
    string? Email);

public sealed record DfeLine(
    string Code,
    string Description,
    decimal Quantity,
    long TotalInCents,
    long FreightInCents,
    long DiscountInCents,
    string Ncm,
    string Cfop,
    string? Cest,
    string IcmsCst,
    decimal IcmsRate,
    string PisCofinsCst,
    decimal PisRate,
    decimal CofinsRate,
    bool HasPisCofinsValues);

public enum DfePaymentKind
{
    Cash = 1,
    CreditCard = 3,
    DebitCard = 4,
    Pix = 17,
    Other = 99
}

public sealed record DfePayment(DfePaymentKind Kind, long AmountInCents);

/// <summary>Everything one note needs, independent of the library that talks to the SEFAZ.</summary>
public sealed record DfeIssue(
    int Model,
    int Environment,
    int Series,
    int Number,
    int NumericCode,
    DateTimeOffset IssuedAt,
    DfeIssuer Issuer,
    DfeRecipient? Recipient,
    IReadOnlyList<DfeLine> Lines,
    IReadOnlyList<DfePayment> Payments,
    bool IsDelivery,
    string? AdditionalInfo,
    DfeCredentials Credentials,
    bool OfflineContingency = false,
    DateTimeOffset? ContingencyAt = null,
    string? ContingencyReason = null,
    int Purpose = 1,
    string? Nature = null,
    IReadOnlyList<string>? ReferencedKeys = null,
    bool IsEntry = false,
    bool FinalConsumer = true);

public enum DfeOutcome
{
    Authorized = 1,
    Rejected = 2,

    /// <summary>No answer: the note may or may not have reached the SEFAZ.</summary>
    Unanswered = 3,

    /// <summary>Signed offline, not sent (NFC-e contingency).</summary>
    SignedOffline = 4
}

public sealed record DfeResult(DfeOutcome Outcome, string Message, string? AccessKey = null, string? Protocol = null, string? Xml = null);

public sealed record DfeCancel(int Model, int Environment, string State, string Cnpj, string AccessKey, string Protocol, string Reason, DfeCredentials Credentials);

/// <summary>Correction letter (CC-e, event 110110): fixes text fields of an authorized NF-e, never values or parties.</summary>
public sealed record DfeCorrection(int Environment, string State, string Cnpj, string AccessKey, int Sequence, string Text, DfeCredentials Credentials);

/// <summary>One document from the distribution: the XML already unzipped and its schema name (resNFe, procNFe, resEvento, procEventoNFe).</summary>
public sealed record DfeDistributedDocument(long Nsu, string Schema, string Xml);

/// <summary>Status 137 = nothing new (wait an hour); 138 = documents found; 656 = asked too often (blocked for an hour).</summary>
public sealed record DfeDistribution(int Status, string Message, long LastNsu, long MaxNsu, IReadOnlyList<DfeDistributedDocument> Documents);

public sealed record DfeNumberGap(int Model, int Environment, string State, string Cnpj, int Series, int FromNumber, int ToNumber, int Year, string Reason, DfeCredentials Credentials);

public sealed record DfeEventResult(bool Accepted, string Message, string? Protocol = null, string? Xml = null);

/// <summary>Talks to the SEFAZ directly with the store's own A1 certificate (no ACBr).</summary>
public interface IDfeEmitter
{
    /// <summary>Builds, signs and validates against the official schemas, without sending.</summary>
    DfeResult Sign(DfeIssue issue);

    Task<DfeResult> AuthorizeAsync(DfeIssue issue, CancellationToken cancellationToken);

    /// <summary>Sends a note signed earlier in offline contingency.</summary>
    Task<DfeResult> SendSignedAsync(int model, int environment, string state, string signedXml, DfeCredentials credentials, CancellationToken cancellationToken);

    Task<DfeEventResult> CancelAsync(DfeCancel request, CancellationToken cancellationToken);

    /// <summary>Downloads documents addressed to the CNPJ after <paramref name="lastNsu"/> (up to 50 per call).</summary>
    Task<DfeDistribution> DistributeAsync(int environment, string state, string cnpj, long lastNsu, DfeCredentials credentials, CancellationToken cancellationToken);

    /// <summary>Ciência da operação (210210): lets the SEFAZ release the full XML of a note addressed to the store.</summary>
    Task<DfeEventResult> AcknowledgeAsync(int environment, string state, string cnpj, string accessKey, DfeCredentials credentials, CancellationToken cancellationToken);

    Task<DfeEventResult> CorrectAsync(DfeCorrection request, CancellationToken cancellationToken);

    Task<DfeEventResult> GiveUpNumbersAsync(DfeNumberGap request, CancellationToken cancellationToken);

    Task<DfeEventResult> StatusAsync(int model, int environment, string state, DfeCredentials credentials, CancellationToken cancellationToken);

    /// <summary>Reads the certificate: subject, CNPJ found in it and expiry. Throws when the file or password is wrong.</summary>
    (string Subject, string? Cnpj, DateTimeOffset ExpiresAt) ReadCertificate(byte[] pfx, string password);
}
