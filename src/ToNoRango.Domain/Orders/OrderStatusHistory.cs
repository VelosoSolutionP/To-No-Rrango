using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Orders;

public sealed class OrderStatusHistory : Entity
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangedBy { get; private set; }
    public string? Reason { get; private set; }

    private OrderStatusHistory() { }

    internal OrderStatusHistory(
        Guid tenantId,
        Guid establishmentId,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        DateTimeOffset changedAt,
        string? changedBy,
        string? reason)
        : base(GuidV7.New())
    {
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderId = Ensure.NotEmpty(orderId, nameof(OrderId));
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAt;
        ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? null : Ensure.MaxLength(changedBy.Trim(), 120, nameof(ChangedBy));
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Ensure.MaxLength(reason.Trim(), 300, nameof(Reason));
    }
}
