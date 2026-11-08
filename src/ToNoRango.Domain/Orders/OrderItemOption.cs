using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Orders;

public sealed class OrderItemOption : Entity
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public string GroupName { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public long PriceInCents { get; private set; }

    private OrderItemOption() { }

    internal OrderItemOption(
        Guid tenantId,
        Guid establishmentId,
        Guid orderItemId,
        string groupName,
        string name,
        long priceInCents)
        : base(GuidV7.New())
    {
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderItemId = Ensure.NotEmpty(orderItemId, nameof(OrderItemId));
        GroupName = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(groupName, nameof(GroupName)), 80, nameof(GroupName));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        PriceInCents = Ensure.NotNegative(priceInCents, nameof(PriceInCents));
    }
}
