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
        string? notes)
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
    }

    internal void AddOption(string groupName, string name, long priceInCents)
    {
        _options.Add(new OrderItemOption(TenantId, EstablishmentId, Id, groupName, name, priceInCents));
    }
}
