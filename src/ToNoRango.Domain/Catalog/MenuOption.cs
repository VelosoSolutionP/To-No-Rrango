using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

public sealed class MenuOption : Entity
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid MenuOptionGroupId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public long PriceInCents { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    public int SortOrder { get; private set; }

    private MenuOption() { }

    public MenuOption(
        Guid tenantId,
        Guid establishmentId,
        Guid menuOptionGroupId,
        string name,
        long priceInCents,
        bool isAvailable,
        int sortOrder)
        : base(GuidV7.New())
    {
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        MenuOptionGroupId = Ensure.NotEmpty(menuOptionGroupId, nameof(MenuOptionGroupId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        PriceInCents = Ensure.NotNegative(priceInCents, nameof(PriceInCents));
        IsAvailable = isAvailable;
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
    }

    public void Update(string name, long priceInCents, bool isAvailable, int sortOrder)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        PriceInCents = Ensure.NotNegative(priceInCents, nameof(PriceInCents));
        IsAvailable = isAvailable;
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
    }
}
