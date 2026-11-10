using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

/// <summary>One constituent of a combo, referencing another menu item by its id.</summary>
public sealed record MenuComboLineDefinition(Guid ConstituentMenuItemId, int Quantity);

/// <summary>
/// A combo is sold as a regular menu item, whose price is the combo price. This table holds the
/// items it bundles, so the public menu can list them and the technical sheet can be derived.
/// </summary>
public sealed class MenuCombo : AggregateRoot
{
    private readonly List<MenuComboLine> _lines = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public IReadOnlyList<MenuComboLine> Lines => _lines;

    private MenuCombo() { }

    public MenuCombo(Guid tenantId, Guid establishmentId, Guid menuItemId, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        MenuItemId = Ensure.NotEmpty(menuItemId, nameof(MenuItemId));
    }

    /// <summary>Replaces the constituents. Duplicates are merged by summing their quantities.</summary>
    public void ReplaceLines(IEnumerable<MenuComboLineDefinition> lines, DateTimeOffset now)
    {
        var merged = lines
            .Select(line => (line.ConstituentMenuItemId, Quantity: Ensure.Positive(line.Quantity, nameof(line.Quantity))))
            .GroupBy(line => line.ConstituentMenuItemId)
            .Select(group => (group.Key, Quantity: group.Sum(line => line.Quantity)))
            .ToList();

        if (merged.Count == 0)
        {
            throw new InvalidOperationException("Um combo precisa de pelo menos um item.");
        }

        _lines.Clear();

        foreach (var (constituentMenuItemId, quantity) in merged)
        {
            if (constituentMenuItemId == MenuItemId)
            {
                throw new InvalidOperationException("Um combo não pode conter a si mesmo.");
            }

            _lines.Add(new MenuComboLine(Id, TenantId, EstablishmentId, constituentMenuItemId, quantity));
        }
    }
}

public sealed class MenuComboLine
{
    public Guid MenuComboId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid ConstituentMenuItemId { get; private set; }
    public int Quantity { get; private set; }

    private MenuComboLine() { }

    internal MenuComboLine(Guid menuComboId, Guid tenantId, Guid establishmentId, Guid constituentMenuItemId, int quantity)
    {
        MenuComboId = menuComboId;
        TenantId = tenantId;
        EstablishmentId = establishmentId;
        ConstituentMenuItemId = constituentMenuItemId;
        Quantity = quantity;
    }
}
