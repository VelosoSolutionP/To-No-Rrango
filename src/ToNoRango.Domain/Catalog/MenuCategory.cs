using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

public sealed class MenuCategory : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Where the items of this category are made: kitchen (food) or bar (drinks).</summary>
    public Orders.PrepStation Station { get; private set; } = Orders.PrepStation.Kitchen;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MenuCategory() { }

    public MenuCategory(
        Guid tenantId,
        Guid establishmentId,
        string name,
        string? description,
        int sortOrder,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        Description = NormalizeOptional(description);
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Update(string name, string? description, int sortOrder, DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        Description = NormalizeOptional(description);
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
        UpdatedAt = now;
    }

    public void SetStation(Orders.PrepStation station, DateTimeOffset now)
    {
        Station = Enum.IsDefined(station) ? station : Orders.PrepStation.Kitchen;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Ensure.MaxLength(value.Trim(), 300, nameof(Description));
}
