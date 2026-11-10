using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

public sealed class MenuItem : AggregateRoot
{
    private readonly List<MenuOptionGroup> _optionGroups = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public long PriceInCents { get; private set; }

    /// <summary>The "de" price of a promotion. Only kept when it is higher than the current price.</summary>
    public long? CompareAtPriceInCents { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsAvailable { get; private set; } = true;
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<MenuOptionGroup> OptionGroups => _optionGroups;

    private MenuItem() { }

    public MenuItem(
        Guid tenantId,
        Guid establishmentId,
        Guid categoryId,
        string name,
        string? description,
        string? imageUrl,
        long priceInCents,
        bool isAvailable,
        int sortOrder,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        CategoryId = Ensure.NotEmpty(categoryId, nameof(CategoryId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Description = NormalizeOptional(description, 500, nameof(Description));
        ImageUrl = NormalizeOptional(imageUrl, 500, nameof(ImageUrl));
        PriceInCents = Ensure.NotNegative(priceInCents, nameof(PriceInCents));
        IsAvailable = isAvailable;
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void UpdateDetails(
        Guid categoryId,
        string name,
        string? description,
        string? imageUrl,
        long priceInCents,
        bool isAvailable,
        int sortOrder,
        DateTimeOffset now)
    {
        CategoryId = Ensure.NotEmpty(categoryId, nameof(CategoryId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Description = NormalizeOptional(description, 500, nameof(Description));
        ImageUrl = NormalizeOptional(imageUrl, 500, nameof(ImageUrl));
        PriceInCents = Ensure.NotNegative(priceInCents, nameof(PriceInCents));
        IsAvailable = isAvailable;
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
        UpdatedAt = now;
    }

    /// <summary>Starts or ends a promotion. A "de" price not above the current price ends it.</summary>
    public void SetCompareAtPrice(long? compareAtPriceInCents, DateTimeOffset now)
    {
        CompareAtPriceInCents = compareAtPriceInCents is long value && value > PriceInCents ? value : null;
        UpdatedAt = now;
    }

    public bool IsOnPromotion => CompareAtPriceInCents is long value && value > PriceInCents;

    public void SetAvailability(bool isAvailable, DateTimeOffset now)
    {
        IsAvailable = isAvailable;
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

    public void ReplaceOptionGroups(IEnumerable<MenuOptionGroupDefinition> groups, DateTimeOffset now)
    {
        _optionGroups.Clear();

        foreach (var definition in groups)
        {
            var group = new MenuOptionGroup(
                TenantId,
                EstablishmentId,
                Id,
                definition.Name,
                definition.MinSelections,
                definition.MaxSelections,
                definition.IsRequired,
                definition.SortOrder);

            group.ReplaceOptions(definition.Options);
            _optionGroups.Add(group);
        }

        UpdatedAt = now;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Ensure.MaxLength(value.Trim(), maxLength, paramName);
    }
}

public sealed record MenuOptionGroupDefinition(
    string Name,
    int MinSelections,
    int MaxSelections,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<MenuOptionDefinition> Options);
