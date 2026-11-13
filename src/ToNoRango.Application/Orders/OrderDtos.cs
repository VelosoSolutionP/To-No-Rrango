using ToNoRango.Domain.Orders;

namespace ToNoRango.Application.Orders;

public sealed record CreateOrderRequest(
    string CustomerName,
    string CustomerPhone,
    FulfillmentType FulfillmentType,
    PaymentMethod PaymentMethod,
    string? Notes,
    string? IdempotencyKey,
    DeliveryAddressRequest? DeliveryAddress,
    IReadOnlyList<CreateOrderItemRequest> Items,
    string? TableLabel = null);

public sealed record DeliveryAddressRequest(
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string PostalCode,
    string? Reference = null);

public sealed record CreateOrderItemRequest(
    Guid MenuItemId,
    int Quantity,
    string? Notes,
    IReadOnlyList<Guid>? OptionIds);

public sealed record UpdateOrderStatusRequest(
    OrderStatus Status,
    string? Reason,
    string? ChangedBy);

public sealed record OrderDto(
    Guid Id,
    string Code,
    Guid EstablishmentId,
    string CustomerName,
    string CustomerPhone,
    FulfillmentType FulfillmentType,
    OrderStatus Status,
    OrderChannel Channel,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    string? Notes,
    DeliveryAddressDto? DeliveryAddress,
    long SubtotalInCents,
    long DeliveryFeeInCents,
    long TotalInCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusHistoryDto> StatusHistory,
    DateTimeOffset? ScheduledFor = null,
    long DiscountInCents = 0,
    string? DiscountReason = null,
    string? TableLabel = null,
    string? DeliveryReference = null,
    bool IsRated = false,
    string? CourierName = null,
    string? WaiterName = null,
    string? DeliveryIssue = null,
    DateTimeOffset? DeliveryIssueAt = null)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Code,
        order.EstablishmentId,
        order.CustomerName,
        order.CustomerPhone,
        order.FulfillmentType,
        order.Status,
        order.Channel,
        order.PaymentMethod,
        order.PaymentStatus,
        order.Notes,
        order.FulfillmentType == FulfillmentType.Delivery
            ? new DeliveryAddressDto(
                order.DeliveryStreet!,
                order.DeliveryNumber!,
                order.DeliveryComplement,
                order.DeliveryDistrict!,
                order.DeliveryCity!,
                order.DeliveryState!,
                order.DeliveryPostalCode!)
            : null,
        order.SubtotalInCents,
        order.DeliveryFeeInCents,
        order.TotalInCents,
        order.CreatedAt,
        order.UpdatedAt,
        order.ConfirmedAt,
        order.ReadyAt,
        order.DeliveredAt,
        order.CancelledAt,
        order.CancelReason,
        order.Items.Select(OrderItemDto.From).ToList(),
        order.StatusHistory.OrderBy(history => history.ChangedAt).Select(OrderStatusHistoryDto.From).ToList(),
        order.ScheduledFor,
        order.DiscountInCents,
        order.DiscountReason,
        order.TableLabel,
        order.DeliveryReference,
        false,
        order.CourierName,
        order.WaiterName,
        order.DeliveryIssue,
        order.DeliveryIssueAt);
}

public sealed record OrderTrackingDto(
    string Code,
    FulfillmentType FulfillmentType,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    string CustomerName,
    string CustomerPhoneMasked,
    long SubtotalInCents,
    long DeliveryFeeInCents,
    long TotalInCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusHistoryDto> StatusHistory,
    OrderChannel Channel = OrderChannel.Web,
    DateTimeOffset? ScheduledFor = null,
    long DiscountInCents = 0,
    string? TableLabel = null,
    string? CourierName = null,
    bool HasDeliveryIssue = false,
    string? StorePrimaryColor = null,
    string? StoreSecondaryColor = null)
{
    public static OrderTrackingDto From(Order order) => new(
        order.Code,
        order.FulfillmentType,
        order.Status,
        order.PaymentMethod,
        order.PaymentStatus,
        order.CustomerName,
        MaskPhone(order.CustomerPhone),
        order.SubtotalInCents,
        order.DeliveryFeeInCents,
        order.TotalInCents,
        order.CreatedAt,
        order.ConfirmedAt,
        order.ReadyAt,
        order.DeliveredAt,
        order.CancelledAt,
        order.CancelReason,
        order.Items.Select(OrderItemDto.From).ToList(),
        order.StatusHistory.OrderBy(history => history.ChangedAt).Select(OrderStatusHistoryDto.From).ToList(),
        order.Channel,
        order.ScheduledFor,
        order.DiscountInCents,
        order.TableLabel,
        order.CourierName,
        order.DeliveryIssue is not null);

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(character => char.IsDigit(character)).ToArray());

        return digits.Length <= 4
            ? "****"
            : $"{new string('*', digits.Length - 4)}{digits[^4..]}";
    }
}

public sealed record DeliveryAddressDto(
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string PostalCode);

public sealed record OrderItemDto(
    Guid Id,
    Guid MenuItemId,
    string Name,
    long UnitPriceInCents,
    int Quantity,
    string? Notes,
    long TotalInCents,
    IReadOnlyList<OrderItemOptionDto> Options,
    PrepStation Station = PrepStation.Kitchen,
    ItemPrepStatus PrepStatus = ItemPrepStatus.Received,
    DateTimeOffset? PrepUpdatedAt = null)
{
    public static OrderItemDto From(OrderItem item) => new(
        item.Id,
        item.MenuItemId,
        item.Name,
        item.UnitPriceInCents,
        item.Quantity,
        item.Notes,
        item.TotalInCents,
        item.Options.Select(OrderItemOptionDto.From).ToList(),
        item.Station,
        item.PrepStatus,
        item.PrepUpdatedAt);
}

public sealed record AdvanceItemRequest(ItemPrepStatus Status);

public sealed record OrderItemOptionDto(string GroupName, string Name, long PriceInCents)
{
    public static OrderItemOptionDto From(OrderItemOption option) => new(
        option.GroupName,
        option.Name,
        option.PriceInCents);
}

public sealed record OrderStatusHistoryDto(
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    DateTimeOffset ChangedAt,
    string? ChangedBy,
    string? Reason)
{
    public static OrderStatusHistoryDto From(OrderStatusHistory history) => new(
        history.FromStatus,
        history.ToStatus,
        history.ChangedAt,
        history.ChangedBy,
        history.Reason);
}
