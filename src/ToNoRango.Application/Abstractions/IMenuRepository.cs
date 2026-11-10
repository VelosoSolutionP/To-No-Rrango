using ToNoRango.Domain.Catalog;

namespace ToNoRango.Application.Abstractions;

public interface IMenuRepository
{
    Task<IReadOnlyList<MenuCategory>> ListCategoriesAsync(Guid establishmentId, bool includeInactive, CancellationToken cancellationToken);

    Task<MenuCategory?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<bool> CategoryBelongsToEstablishmentAsync(Guid categoryId, Guid establishmentId, CancellationToken cancellationToken);

    Task<bool> CategoryHasItemsAsync(Guid categoryId, CancellationToken cancellationToken);

    Task AddCategoryAsync(MenuCategory category, CancellationToken cancellationToken);

    Task<IReadOnlyList<MenuItem>> ListItemsAsync(Guid establishmentId, bool includeInactive, CancellationToken cancellationToken);

    Task<MenuItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken);

    Task AddItemAsync(MenuItem item, CancellationToken cancellationToken);

    Task<IReadOnlyList<MenuCombo>> ListCombosAsync(Guid establishmentId, CancellationToken cancellationToken);

    Task<MenuCombo?> GetComboByMenuItemAsync(Guid menuItemId, CancellationToken cancellationToken);

    Task AddComboAsync(MenuCombo combo, CancellationToken cancellationToken);

    void RemoveCombo(MenuCombo combo);
}
