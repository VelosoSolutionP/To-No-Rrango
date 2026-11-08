using Microsoft.EntityFrameworkCore;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Repositories;

public sealed class MenuRepository(ToNoRangoDbContext dbContext) : IMenuRepository
{
    public async Task<IReadOnlyList<MenuCategory>> ListCategoriesAsync(
        Guid establishmentId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MenuCategories
            .AsNoTracking()
            .Where(category => category.EstablishmentId == establishmentId);

        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<MenuCategory?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.MenuCategories.FirstOrDefaultAsync(category => category.Id == categoryId, cancellationToken);

    public Task<bool> CategoryBelongsToEstablishmentAsync(
        Guid categoryId,
        Guid establishmentId,
        CancellationToken cancellationToken) =>
        dbContext.MenuCategories.AnyAsync(
            category => category.Id == categoryId && category.EstablishmentId == establishmentId,
            cancellationToken);

    public Task<bool> CategoryHasItemsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.MenuItems.AnyAsync(item => item.CategoryId == categoryId, cancellationToken);

    public Task AddCategoryAsync(MenuCategory category, CancellationToken cancellationToken)
    {
        dbContext.MenuCategories.Add(category);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MenuItem>> ListItemsAsync(
        Guid establishmentId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MenuItems
            .AsNoTracking()
            .Include(item => item.OptionGroups)
            .ThenInclude(group => group.Options)
            .Where(item => item.EstablishmentId == establishmentId);

        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        return await query
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<MenuItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken) =>
        dbContext.MenuItems
            .Include(item => item.OptionGroups)
            .ThenInclude(group => group.Options)
            .FirstOrDefaultAsync(item => item.Id == itemId, cancellationToken);

    public Task AddItemAsync(MenuItem item, CancellationToken cancellationToken)
    {
        dbContext.MenuItems.Add(item);
        return Task.CompletedTask;
    }
}
