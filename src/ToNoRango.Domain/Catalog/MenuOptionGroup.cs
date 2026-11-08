using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

public sealed class MenuOptionGroup : Entity
{
    private readonly List<MenuOption> _options = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int MinSelections { get; private set; }
    public int MaxSelections { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyList<MenuOption> Options => _options;

    private MenuOptionGroup() { }

    public MenuOptionGroup(
        Guid tenantId,
        Guid establishmentId,
        Guid menuItemId,
        string name,
        int minSelections,
        int maxSelections,
        bool isRequired,
        int sortOrder)
        : base(GuidV7.New())
    {
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        MenuItemId = Ensure.NotEmpty(menuItemId, nameof(MenuItemId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        SetSelectionRules(minSelections, maxSelections, isRequired);
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
    }

    public void Update(string name, int minSelections, int maxSelections, bool isRequired, int sortOrder)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 80, nameof(Name));
        SetSelectionRules(minSelections, maxSelections, isRequired);
        SortOrder = Ensure.NotNegative(sortOrder, nameof(SortOrder));
    }

    public MenuOption AddOption(string name, long priceInCents, bool isAvailable, int sortOrder)
    {
        var option = new MenuOption(TenantId, EstablishmentId, Id, name, priceInCents, isAvailable, sortOrder);
        _options.Add(option);
        return option;
    }

    public void ReplaceOptions(IEnumerable<MenuOptionDefinition> options)
    {
        _options.Clear();

        foreach (var option in options)
        {
            AddOption(option.Name, option.PriceInCents, option.IsAvailable, option.SortOrder);
        }
    }

    private void SetSelectionRules(int minSelections, int maxSelections, bool isRequired)
    {
        MinSelections = Ensure.NotNegative(minSelections, nameof(MinSelections));
        MaxSelections = Ensure.NotNegative(maxSelections, nameof(MaxSelections));

        if (isRequired && MinSelections == 0)
        {
            MinSelections = 1;
        }

        if (MaxSelections == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSelections), "Max selections must be at least 1.");
        }

        if (MinSelections > MaxSelections)
        {
            throw new ArgumentException("Min selections cannot be greater than max selections.", nameof(minSelections));
        }

        IsRequired = isRequired || MinSelections > 0;
    }
}

public sealed record MenuOptionDefinition(string Name, long PriceInCents, bool IsAvailable, int SortOrder);
