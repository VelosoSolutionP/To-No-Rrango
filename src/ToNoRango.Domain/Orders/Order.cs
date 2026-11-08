using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Common;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Orders;

public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusHistory> _statusHistory = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string CustomerPhoneKey { get; private set; } = string.Empty;
    public FulfillmentType FulfillmentType { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderChannel Channel { get; private set; } = OrderChannel.Web;
    public PaymentMethod PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string? Notes { get; private set; }
    public string? DeliveryStreet { get; private set; }
    public string? DeliveryNumber { get; private set; }
    public string? DeliveryComplement { get; private set; }
    public string? DeliveryDistrict { get; private set; }
    public string? DeliveryCity { get; private set; }
    public string? DeliveryState { get; private set; }
    public string? DeliveryPostalCode { get; private set; }
    public long SubtotalInCents { get; private set; }
    public long DeliveryFeeInCents { get; private set; }
    public long TotalInCents { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? StockDeductedAt { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderStatusHistory> StatusHistory => _statusHistory;

    private Order() { }

    public static Order Create(
        Guid tenantId,
        Guid establishmentId,
        string code,
        string customerName,
        string customerPhone,
        FulfillmentType fulfillmentType,
        PaymentMethod paymentMethod,
        string? deliveryStreet,
        string? deliveryNumber,
        string? deliveryComplement,
        string? deliveryDistrict,
        string? deliveryCity,
        string? deliveryState,
        string? deliveryPostalCode,
        string? notes,
        string? idempotencyKey,
        long deliveryFeeInCents,
        string? createdBy,
        DateTimeOffset now,
        OrderChannel channel = OrderChannel.Web)
    {
        var order = new Order
        {
            Id = GuidV7.New(),
            TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId)),
            EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId)),
            Code = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(code, nameof(Code)), 20, nameof(Code)),
            CustomerName = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(customerName, nameof(CustomerName)), 120, nameof(CustomerName)),
            CustomerPhone = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(customerPhone, nameof(CustomerPhone)), 30, nameof(CustomerPhone)),
            CustomerPhoneKey = PhoneNumber.Key(customerPhone),
            FulfillmentType = fulfillmentType,
            PaymentMethod = paymentMethod,
            Status = OrderStatus.Pending,
            Channel = channel,
            PaymentStatus = PaymentStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : Ensure.MaxLength(notes.Trim(), 500, nameof(Notes)),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : Ensure.MaxLength(idempotencyKey.Trim(), 100, nameof(IdempotencyKey)),
            CreatedAt = now,
            UpdatedAt = now
        };

        if (fulfillmentType == FulfillmentType.Delivery)
        {
            order.DeliveryStreet = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryStreet, nameof(DeliveryStreet)), 200, nameof(DeliveryStreet));
            order.DeliveryNumber = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryNumber, nameof(DeliveryNumber)), 20, nameof(DeliveryNumber));
            order.DeliveryComplement = string.IsNullOrWhiteSpace(deliveryComplement) ? null : Ensure.MaxLength(deliveryComplement.Trim(), 100, nameof(DeliveryComplement));
            order.DeliveryDistrict = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryDistrict, nameof(DeliveryDistrict)), 80, nameof(DeliveryDistrict));
            order.DeliveryCity = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryCity, nameof(DeliveryCity)), 80, nameof(DeliveryCity));
            order.DeliveryState = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryState, nameof(DeliveryState)), 2, nameof(DeliveryState)).ToUpperInvariant();
            order.DeliveryPostalCode = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(deliveryPostalCode, nameof(DeliveryPostalCode)), 10, nameof(DeliveryPostalCode));
            order.DeliveryFeeInCents = Ensure.NotNegative(deliveryFeeInCents, nameof(DeliveryFeeInCents));
        }

        order.TotalInCents = order.DeliveryFeeInCents;
        order._statusHistory.Add(new OrderStatusHistory(
            order.TenantId,
            order.EstablishmentId,
            order.Id,
            null,
            OrderStatus.Pending,
            now,
            createdBy,
            "Pedido criado."));

        return order;
    }

    public OrderItem AddItem(MenuItem item, int quantity, string? notes, IReadOnlyCollection<Guid>? optionIds)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.TenantId != TenantId || item.EstablishmentId != EstablishmentId)
        {
            throw new InvalidOperationException("Item does not belong to this establishment.");
        }

        if (!item.IsActive || !item.IsAvailable)
        {
            throw new InvalidOperationException($"Item '{item.Name}' is not available.");
        }

        var selectedIds = optionIds?.ToHashSet() ?? new HashSet<Guid>();
        var knownOptionIds = new HashSet<Guid>();
        var selectedOptions = new List<(string GroupName, string OptionName, long PriceInCents)>();

        foreach (var group in item.OptionGroups)
        {
            knownOptionIds.UnionWith(group.Options.Select(option => option.Id));
            var chosen = group.Options.Where(option => selectedIds.Contains(option.Id)).ToList();

            if (chosen.Count < group.MinSelections || chosen.Count > group.MaxSelections)
            {
                throw new InvalidOperationException(
                    $"Option group '{group.Name}' requires between {group.MinSelections} and {group.MaxSelections} selections.");
            }

            foreach (var option in chosen)
            {
                if (!option.IsAvailable)
                {
                    throw new InvalidOperationException($"Option '{option.Name}' is not available.");
                }

                selectedOptions.Add((group.Name, option.Name, option.PriceInCents));
            }
        }

        if (selectedIds.Any(id => !knownOptionIds.Contains(id)))
        {
            throw new InvalidOperationException("One or more selected options do not belong to the item.");
        }

        var unitPrice = item.PriceInCents + selectedOptions.Sum(option => option.PriceInCents);
        var orderItem = new OrderItem(
            TenantId,
            EstablishmentId,
            Id,
            item.Id,
            item.Name,
            unitPrice,
            quantity,
            notes);

        foreach (var option in selectedOptions)
        {
            orderItem.AddOption(option.GroupName, option.OptionName, option.PriceInCents);
        }

        _items.Add(orderItem);
        RecalculateTotals();

        return orderItem;
    }

    public bool CanTransitionTo(OrderStatus target) => target switch
    {
        OrderStatus.Confirmed => Status == OrderStatus.Pending,
        OrderStatus.Preparing => Status == OrderStatus.Confirmed,
        OrderStatus.Ready => Status == OrderStatus.Preparing,
        OrderStatus.OutForDelivery => Status == OrderStatus.Ready && FulfillmentType == FulfillmentType.Delivery,
        OrderStatus.Delivered => (Status == OrderStatus.Ready && FulfillmentType != FulfillmentType.Delivery)
            || (Status == OrderStatus.OutForDelivery && FulfillmentType == FulfillmentType.Delivery),
        OrderStatus.Cancelled => Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready,
        _ => false
    };

    public void ChangeStatus(OrderStatus target, string? changedBy, string? reason, DateTimeOffset now)
    {
        if (!CanTransitionTo(target))
        {
            throw new InvalidOperationException($"Cannot change order status from {Status} to {target}.");
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : Ensure.MaxLength(reason.Trim(), 300, nameof(reason));

        if (target == OrderStatus.Cancelled && normalizedReason is null)
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        var previous = Status;
        Status = target;
        UpdatedAt = now;

        switch (target)
        {
            case OrderStatus.Confirmed:
                ConfirmedAt = now;
                break;
            case OrderStatus.Ready:
                ReadyAt = now;
                break;
            case OrderStatus.Delivered:
                DeliveredAt = now;
                break;
            case OrderStatus.Cancelled:
                CancelledAt = now;
                CancelReason = normalizedReason;
                CancelledBy = string.IsNullOrWhiteSpace(changedBy) ? null : Ensure.MaxLength(changedBy.Trim(), 120, nameof(changedBy));
                break;
        }

        _statusHistory.Add(new OrderStatusHistory(
            TenantId,
            EstablishmentId,
            Id,
            previous,
            target,
            now,
            changedBy,
            normalizedReason));
    }

    public void SetPaymentStatus(PaymentStatus status, DateTimeOffset now)
    {
        PaymentStatus = status;
        PaidAt = status == PaymentStatus.Paid ? now : PaidAt;
        UpdatedAt = now;
    }

    public void MarkStockDeducted(DateTimeOffset now) => StockDeductedAt = now;

    public void MarkStockRestored() => StockDeductedAt = null;

    private void RecalculateTotals()
    {
        SubtotalInCents = _items.Sum(item => item.TotalInCents);
        TotalInCents = checked(SubtotalInCents + DeliveryFeeInCents);
    }
}
