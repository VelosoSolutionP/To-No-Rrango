using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Pdv;

public enum CashSessionStatus
{
    Open = 1,
    Closed = 2
}

public enum CashMovementType
{
    /// <summary>Money put in the drawer (suprimento).</summary>
    Supply = 1,

    /// <summary>Money taken from the drawer (sangria).</summary>
    Withdrawal = 2,

    /// <summary>A sale received at this register.</summary>
    Sale = 3,

    /// <summary>A sale undone at this register.</summary>
    SaleReversal = 4
}

/// <summary>
/// One shift of a register, from opening to closing. Only cash changes what should be in the
/// drawer; card and Pix are kept per method for the closing check.
/// </summary>
public sealed class CashSession : AggregateRoot
{
    private readonly List<CashMovement> _movements = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public CashSessionStatus Status { get; private set; }
    public string OpenedBy { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; private set; }
    public long OpeningAmountInCents { get; private set; }
    public string? ClosedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The cash the operator counted when closing.</summary>
    public long? CountedCashInCents { get; private set; }

    /// <summary>The cash the system expected when closing, kept so later changes do not rewrite the history.</summary>
    public long? ExpectedCashInCents { get; private set; }
    public string? ClosingNotes { get; private set; }
    public IReadOnlyList<CashMovement> Movements => _movements;

    private CashSession() { }

    public CashSession(Guid tenantId, Guid establishmentId, string openedBy, long openingAmountInCents, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OpenedBy = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(openedBy, nameof(OpenedBy)), 120, nameof(OpenedBy));
        OpeningAmountInCents = Ensure.NotNegative(openingAmountInCents, "valor de abertura");
        OpenedAt = now;
        Status = CashSessionStatus.Open;
    }

    /// <summary>What should be in the drawer now: opening + cash sales + supplies − withdrawals.</summary>
    public long ExpectedCash => OpeningAmountInCents + _movements.Sum(movement => movement.CashEffect);

    public long? DifferenceInCents => CountedCashInCents is long counted && ExpectedCashInCents is long expected ? counted - expected : null;

    public CashMovement Supply(long amountInCents, string reason, string operatorName, DateTimeOffset now) =>
        Add(CashMovementType.Supply, PaymentMethod.Cash, amountInCents, reason, operatorName, null, now);

    public CashMovement Withdraw(long amountInCents, string reason, string operatorName, DateTimeOffset now)
    {
        if (amountInCents > ExpectedCash)
        {
            throw new InvalidOperationException("A sangria não pode passar do dinheiro que está no caixa.");
        }

        return Add(CashMovementType.Withdrawal, PaymentMethod.Cash, amountInCents, reason, operatorName, null, now);
    }

    public CashMovement RegisterSale(Order order, string operatorName, DateTimeOffset now)
    {
        if (_movements.Any(movement => movement.OrderId == order.Id && movement.Type == CashMovementType.Sale))
        {
            throw new InvalidOperationException("Esta venda já está neste caixa.");
        }

        return Add(CashMovementType.Sale, order.PaymentMethod, order.TotalInCents, $"Venda {order.Code}", operatorName, order.Id, now);
    }

    public CashMovement ReverseSale(Order order, string operatorName, DateTimeOffset now)
    {
        var sale = _movements.FirstOrDefault(movement => movement.OrderId == order.Id && movement.Type == CashMovementType.Sale)
            ?? throw new InvalidOperationException("Esta venda não está neste caixa.");

        if (_movements.Any(movement => movement.OrderId == order.Id && movement.Type == CashMovementType.SaleReversal))
        {
            throw new InvalidOperationException("Esta venda já foi estornada.");
        }

        return Add(CashMovementType.SaleReversal, sale.Method, sale.AmountInCents, $"Estorno {order.Code}", operatorName, order.Id, now);
    }

    public void Close(long countedCashInCents, string closedBy, string? notes, DateTimeOffset now)
    {
        EnsureOpen();
        CountedCashInCents = Ensure.NotNegative(countedCashInCents, "dinheiro contado");
        ExpectedCashInCents = ExpectedCash;

        if (CountedCashInCents != ExpectedCashInCents && string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("Há diferença no caixa: explique no campo de observação antes de fechar.");
        }

        ClosedBy = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(closedBy, nameof(ClosedBy)), 120, nameof(ClosedBy));
        ClosingNotes = string.IsNullOrWhiteSpace(notes) ? null : Ensure.MaxLength(notes.Trim(), 300, nameof(ClosingNotes));
        ClosedAt = now;
        Status = CashSessionStatus.Closed;
    }

    private CashMovement Add(CashMovementType type, PaymentMethod method, long amountInCents, string reason, string operatorName, Guid? orderId, DateTimeOffset now)
    {
        EnsureOpen();

        if (amountInCents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountInCents), "O valor deve ser maior que zero.");
        }

        var movement = new CashMovement(TenantId, EstablishmentId, Id, type, method, amountInCents, reason, operatorName, orderId, now);
        _movements.Add(movement);
        return movement;
    }

    private void EnsureOpen()
    {
        if (Status != CashSessionStatus.Open)
        {
            throw new InvalidOperationException("Este caixa já está fechado.");
        }
    }
}

public sealed class CashMovement : Entity
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public CashMovementType Type { get; private set; }
    public PaymentMethod Method { get; private set; }
    public long AmountInCents { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string OperatorName { get; private set; } = string.Empty;
    public Guid? OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CashMovement() { }

    internal CashMovement(
        Guid tenantId,
        Guid establishmentId,
        Guid cashSessionId,
        CashMovementType type,
        PaymentMethod method,
        long amountInCents,
        string reason,
        string operatorName,
        Guid? orderId,
        DateTimeOffset now)
        : base(GuidV7.New())
    {
        TenantId = tenantId;
        EstablishmentId = establishmentId;
        CashSessionId = cashSessionId;
        Type = type;
        Method = method;
        AmountInCents = amountInCents;
        Reason = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(reason, "motivo"), 120, nameof(Reason));
        OperatorName = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(operatorName, nameof(OperatorName)), 120, nameof(OperatorName));
        OrderId = orderId;
        CreatedAt = now;
    }

    /// <summary>How this movement changes the cash in the drawer.</summary>
    public long CashEffect => Method != PaymentMethod.Cash
        ? 0
        : Type switch
        {
            CashMovementType.Supply or CashMovementType.Sale => AmountInCents,
            CashMovementType.Withdrawal or CashMovementType.SaleReversal => -AmountInCents,
            _ => 0
        };
}
