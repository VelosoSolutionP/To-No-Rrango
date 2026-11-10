using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Finance;

public enum FinancialType
{
    Receivable = 1,
    Payable = 2
}

public enum FinancialStatus
{
    Open = 1,
    Settled = 2,
    Cancelled = 3
}

public enum FinancialSource
{
    Manual = 1,
    Order = 2,
    Purchase = 3
}

public sealed class FinancialEntry : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public FinancialType Type { get; private set; }
    public FinancialStatus Status { get; private set; }
    public FinancialSource Source { get; private set; }
    public Guid? SourceId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public long AmountInCents { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Who pays or receives (customer, supplier) and the document number, as in an ERP title.</summary>
    public string? Counterparty { get; private set; }
    public string? DocumentNumber { get; private set; }
    public string? SettlementMethod { get; private set; }
    public DateOnly? SettledOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private FinancialEntry() { }

    public FinancialEntry(
        Guid tenantId,
        Guid establishmentId,
        FinancialType type,
        FinancialSource source,
        Guid? sourceId,
        string category,
        string description,
        long amountInCents,
        DateOnly dueOn,
        DateTimeOffset now)
    {
        if (amountInCents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountInCents), "O valor do lançamento deve ser maior que zero.");
        }

        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Type = type;
        Status = FinancialStatus.Open;
        Source = source;
        SourceId = sourceId;
        Category = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(category, nameof(Category)), 60, nameof(Category));
        Description = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(description, nameof(Description)), 200, nameof(Description));
        AmountInCents = amountInCents;
        DueOn = dueOn;
        CreatedAt = now;
    }

    public void Settle(DateTimeOffset now) => Settle(now, null, null);

    public void Settle(DateTimeOffset now, string? method, DateOnly? on)
    {
        if (Status != FinancialStatus.Open)
        {
            throw new InvalidOperationException("Somente lançamentos em aberto podem ser baixados.");
        }

        Status = FinancialStatus.Settled;
        SettledAt = now;
        SettlementMethod = string.IsNullOrWhiteSpace(method) ? null : Ensure.MaxLength(method.Trim(), 40, nameof(SettlementMethod));
        SettledOn = on ?? DateOnly.FromDateTime(now.UtcDateTime);
    }

    public void Describe(string? counterparty, string? documentNumber)
    {
        Counterparty = string.IsNullOrWhiteSpace(counterparty) ? null : Ensure.MaxLength(counterparty.Trim(), 120, nameof(Counterparty));
        DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : Ensure.MaxLength(documentNumber.Trim(), 40, nameof(DocumentNumber));
    }

    /// <summary>Reopens a settled title (estorno da baixa).</summary>
    public void Reopen()
    {
        if (Status != FinancialStatus.Settled || Source != FinancialSource.Manual)
        {
            throw new InvalidOperationException("Só a baixa de um título manual pode ser estornada aqui.");
        }

        Status = FinancialStatus.Open;
        SettledAt = null;
        SettledOn = null;
        SettlementMethod = null;
    }

    public void Cancel()
    {
        if (Status == FinancialStatus.Cancelled)
        {
            return;
        }

        Status = FinancialStatus.Cancelled;
        SettledAt = null;
    }
}
