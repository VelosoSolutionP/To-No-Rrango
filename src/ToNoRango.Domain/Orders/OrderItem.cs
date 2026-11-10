using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Orders;

public sealed class OrderItem : Entity
{
    private readonly List<OrderItemOption> _options = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public long UnitPriceInCents { get; private set; }
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }
    public long TotalInCents { get; private set; }
    public PrepStation Station { get; private set; } = PrepStation.Kitchen;
    public ItemPrepStatus PrepStatus { get; private set; } = ItemPrepStatus.Received;
    public DateTimeOffset? PrepUpdatedAt { get; private set; }
    public IReadOnlyList<OrderItemOption> Options => _options;

    private OrderItem() { }

    internal OrderItem(
        Guid tenantId,
        Guid establishmentId,
        Guid orderId,
        Guid menuItemId,
        string name,
        long unitPriceInCents,
        int quantity,
        string? notes,
        PrepStation station = PrepStation.Kitchen)
        : base(GuidV7.New())
    {
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderId = Ensure.NotEmpty(orderId, nameof(OrderId));
        MenuItemId = Ensure.NotEmpty(menuItemId, nameof(MenuItemId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        UnitPriceInCents = Ensure.NotNegative(unitPriceInCents, nameof(UnitPriceInCents));
        Quantity = quantity is < 1 or > 99
            ? throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 99.")
            : quantity;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Ensure.MaxLength(notes.Trim(), 500, nameof(Notes));
        TotalInCents = checked(UnitPriceInCents * Quantity);
        Station = station;
    }

    public static bool IsStepOf(PrepStation station, ItemPrepStatus status) =>
        station == PrepStation.Kitchen || status is not (ItemPrepStatus.Queued or ItemPrepStatus.Finishing);

    /// <summary>Moves the item forward. Going back is refused, so the customer never sees a step undone.</summary>
    internal void MoveTo(ItemPrepStatus target, DateTimeOffset now)
    {
        if (target < PrepStatus)
        {
            throw new InvalidOperationException($"O item '{Name}' já passou desta etapa.");
        }

        if (!IsStepOf(Station, target))
        {
            throw new InvalidOperationException($"Esta etapa não existe para itens do bar.");
        }

        if (target == PrepStatus)
        {
            return;
        }

        PrepStatus = target;
        PrepUpdatedAt = now;
    }

    /// <summary>Raises the item to at least the given step, used when the whole order moves.</summary>
    internal void RaiseTo(ItemPrepStatus floor, DateTimeOffset now)
    {
        var target = floor;

        while (!IsStepOf(Station, target))
        {
            target--;
        }

        if (target > PrepStatus)
        {
            PrepStatus = target;
            PrepUpdatedAt = now;
        }
    }

    internal void AddOption(string groupName, string name, long priceInCents)
    {
        _options.Add(new OrderItemOption(TenantId, EstablishmentId, Id, groupName, name, priceInCents));
    }
}
