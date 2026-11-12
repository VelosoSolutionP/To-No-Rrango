using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Fiscal;

/// <summary>
/// NFS-e of the national standard (Sefin Nacional). The store sends a DPS (declaration of the service
/// provided); the government answers with the NFS-e and its 50-digit access key.
/// </summary>
public sealed class ServiceInvoice : AggregateRoot
{
    public const string SimulatedProtocol = FiscalDocument.SimulatedProtocol;

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }

    /// <summary>Id given by the system that asked (API or panel); repeating it returns the same note.</summary>
    public string ExternalId { get; private set; } = string.Empty;
    public int Series { get; private set; }
    public long Number { get; private set; }
    public int Environment { get; private set; }
    public bool IsSimulation { get; private set; }
    public FiscalDocumentStatus Status { get; private set; }
    public string DpsId { get; private set; } = string.Empty;
    public string? AccessKey { get; private set; }
    public string? Xml { get; private set; }
    public string? Message { get; private set; }
    public string TakerDocument { get; private set; } = string.Empty;
    public string TakerName { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public long AmountInCents { get; private set; }
    public decimal? IssRate { get; private set; }
    public bool IssWithheld { get; private set; }
    public DateOnly Competence { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ServiceInvoice() { }

    public ServiceInvoice(
        Guid tenantId, Guid establishmentId, string externalId, int series, long number, int environment, string dpsId,
        string takerDocument, string takerName, string serviceCode, string description, long amountInCents,
        decimal? issRate, bool issWithheld, DateOnly competence, DateTimeOffset now, bool isSimulation = false)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(tenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(establishmentId));
        ExternalId = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(externalId, nameof(externalId)).Trim(), 40, nameof(externalId));
        Series = series;
        Number = number;
        Environment = environment is 1 or 2 ? environment : throw new ArgumentException("Ambiente inválido.", nameof(environment));
        DpsId = Ensure.NotNullOrWhiteSpace(dpsId, nameof(dpsId));
        TakerDocument = Ensure.NotNullOrWhiteSpace(takerDocument, nameof(takerDocument));
        TakerName = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(takerName, nameof(takerName)).Trim(), 150, nameof(takerName));
        ServiceCode = Ensure.NotNullOrWhiteSpace(serviceCode, nameof(serviceCode));
        Description = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(description, nameof(description)).Trim(), 2000, nameof(description));
        AmountInCents = amountInCents > 0 ? amountInCents : throw new ArgumentException("Valor do serviço inválido.", nameof(amountInCents));
        IssRate = issRate;
        IssWithheld = issWithheld;
        Competence = competence;
        IsSimulation = isSimulation;
        Status = FiscalDocumentStatus.Processing;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Authorize(string accessKey, string? xml, string? message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Authorized;
        AccessKey = Ensure.NotNullOrWhiteSpace(accessKey, nameof(accessKey));
        Xml = xml;
        Message = message;
        UpdatedAt = now;
    }

    public void Reject(string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Rejected;
        Message = message.Length <= 2000 ? message : message[..2000];
        UpdatedAt = now;
    }

    public void MarkUnanswered(string message, DateTimeOffset now)
    {
        Status = FiscalDocumentStatus.Processing;
        Message = message.Length <= 2000 ? message : message[..2000];
        UpdatedAt = now;
    }

    /// <summary>Sends again after a refusal: same DPS number and id, so the government never sees two notes for one service.</summary>
    public void Restart(DateTimeOffset now)
    {
        if (Status is FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Cancelled)
        {
            throw new InvalidOperationException("Esta NFS-e já foi autorizada.");
        }

        Status = FiscalDocumentStatus.Processing;
        Message = null;
        UpdatedAt = now;
    }

    public void Cancel(string reason, string? eventXml, DateTimeOffset now)
    {
        if (Status != FiscalDocumentStatus.Authorized)
        {
            throw new InvalidOperationException("Só uma NFS-e autorizada pode ser cancelada.");
        }

        Status = FiscalDocumentStatus.Cancelled;
        CancelReason = reason;
        CancelledAt = now;
        Message = "Cancelada.";
        Xml = eventXml is null ? Xml : $"{Xml}\n<!-- evento de cancelamento -->\n{eventXml}";
        UpdatedAt = now;
    }
}
