using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Catalog;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Catalog;

public sealed class MenuService(
    IEstablishmentRepository establishments,
    IMenuRepository menu,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<PublicMenuDto>> GetPublicMenuAsync(string slug, CancellationToken cancellationToken = default)
    {
        var establishment = await establishments.GetBySlugAsync(slug, cancellationToken);

        if (establishment is null || !establishment.IsActive)
        {
            return Result<PublicMenuDto>.Failure(
                AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
        }

        var categories = await menu.ListCategoriesAsync(establishment.Id, includeInactive: false, cancellationToken);
        var items = await menu.ListItemsAsync(establishment.Id, includeInactive: false, cancellationToken);

        return PublicMenuDto.From(establishment, categories, items);
    }

    public async Task<Result<PublicMenuDto>> GetAdminMenuAsync(Guid establishmentId, CancellationToken cancellationToken = default)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<PublicMenuDto>.Failure(
                AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
        }

        var categories = await menu.ListCategoriesAsync(establishment.Id, includeInactive: true, cancellationToken);
        var items = await menu.ListItemsAsync(establishment.Id, includeInactive: true, cancellationToken);

        return PublicMenuDto.From(establishment, categories, items);
    }

    public async Task<Result<MenuCategoryDto>> CreateCategoryAsync(
        Guid establishmentId,
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<MenuCategoryDto>.Failure(
                    AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
            }

            var category = new MenuCategory(
                establishment.TenantId,
                establishment.Id,
                request.Name,
                request.Description,
                request.SortOrder,
                clock.UtcNow);

            await menu.AddCategoryAsync(category, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return MenuCategoryDto.From(category, Array.Empty<MenuItem>());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<MenuCategoryDto>(exception);
        }
    }

    public async Task<Result<MenuCategoryDto>> UpdateCategoryAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var category = await menu.GetCategoryAsync(categoryId, cancellationToken);

            if (category is null)
            {
                return Result<MenuCategoryDto>.Failure(
                    AppErrors.NotFound("category.not_found", "Categoria não encontrada."));
            }

            category.Update(request.Name, request.Description, request.SortOrder, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return MenuCategoryDto.From(category, Array.Empty<MenuItem>());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<MenuCategoryDto>(exception);
        }
    }

    public async Task<Result<MenuCategoryDto>> DeactivateCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await menu.GetCategoryAsync(categoryId, cancellationToken);

        if (category is null)
        {
            return Result<MenuCategoryDto>.Failure(
                AppErrors.NotFound("category.not_found", "Categoria não encontrada."));
        }

        category.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MenuCategoryDto.From(category, Array.Empty<MenuItem>());
    }

    public async Task<Result<MenuCategoryDto>> SetCategoryActiveAsync(
        Guid categoryId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var category = await menu.GetCategoryAsync(categoryId, cancellationToken);

        if (category is null)
        {
            return Result<MenuCategoryDto>.Failure(
                AppErrors.NotFound("category.not_found", "Categoria não encontrada."));
        }

        if (isActive)
        {
            category.Activate(clock.UtcNow);
        }
        else
        {
            category.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MenuCategoryDto.From(category, Array.Empty<MenuItem>());
    }

    public async Task<Result<MenuItemDto>> CreateItemAsync(
        Guid establishmentId,
        CreateMenuItemRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<MenuItemDto>.Failure(
                    AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
            }

            if (!await menu.CategoryBelongsToEstablishmentAsync(request.CategoryId, establishmentId, cancellationToken))
            {
                return Result<MenuItemDto>.Failure(
                    AppErrors.NotFound("category.not_found", "Categoria não encontrada neste estabelecimento."));
            }

            var now = clock.UtcNow;
            var item = new MenuItem(
                establishment.TenantId,
                establishment.Id,
                request.CategoryId,
                request.Name,
                request.Description,
                request.ImageUrl,
                request.PriceInCents,
                request.IsAvailable,
                request.SortOrder,
                now);

            item.ReplaceOptionGroups(MapGroups(request.OptionGroups), now);

            await menu.AddItemAsync(item, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return MenuItemDto.From(item);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<MenuItemDto>(exception);
        }
    }

    public async Task<Result<MenuItemDto>> UpdateItemAsync(
        Guid itemId,
        UpdateMenuItemRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await menu.GetItemAsync(itemId, cancellationToken);

            if (item is null)
            {
                return Result<MenuItemDto>.Failure(
                    AppErrors.NotFound("item.not_found", "Item não encontrado."));
            }

            if (!await menu.CategoryBelongsToEstablishmentAsync(request.CategoryId, item.EstablishmentId, cancellationToken))
            {
                return Result<MenuItemDto>.Failure(
                    AppErrors.NotFound("category.not_found", "Categoria não encontrada neste estabelecimento."));
            }

            var now = clock.UtcNow;
            item.UpdateDetails(
                request.CategoryId,
                request.Name,
                request.Description,
                request.ImageUrl,
                request.PriceInCents,
                request.IsAvailable,
                request.SortOrder,
                now);

            if (request.OptionGroups is not null)
            {
                item.ReplaceOptionGroups(MapGroups(request.OptionGroups), now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return MenuItemDto.From(item);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<MenuItemDto>(exception);
        }
    }

    public async Task<Result<MenuItemDto>> DeactivateItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await menu.GetItemAsync(itemId, cancellationToken);

        if (item is null)
        {
            return Result<MenuItemDto>.Failure(
                AppErrors.NotFound("item.not_found", "Item não encontrado."));
        }

        item.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MenuItemDto.From(item);
    }

    public async Task<Result<MenuItemDto>> SetItemActiveAsync(
        Guid itemId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var item = await menu.GetItemAsync(itemId, cancellationToken);

        if (item is null)
        {
            return Result<MenuItemDto>.Failure(
                AppErrors.NotFound("item.not_found", "Item não encontrado."));
        }

        if (isActive)
        {
            item.Activate(clock.UtcNow);
        }
        else
        {
            item.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MenuItemDto.From(item);
    }

    public async Task<Result<MenuItemDto>> SetItemAvailabilityAsync(
        Guid itemId,
        bool isAvailable,
        CancellationToken cancellationToken = default)
    {
        var item = await menu.GetItemAsync(itemId, cancellationToken);

        if (item is null)
        {
            return Result<MenuItemDto>.Failure(
                AppErrors.NotFound("item.not_found", "Item não encontrado."));
        }

        item.SetAvailability(isAvailable, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MenuItemDto.From(item);
    }

    private static IReadOnlyList<MenuOptionGroupDefinition> MapGroups(IReadOnlyList<MenuOptionGroupRequest>? groups)
    {
        if (groups is null)
        {
            return Array.Empty<MenuOptionGroupDefinition>();
        }

        return groups
            .Select(group => new MenuOptionGroupDefinition(
                group.Name,
                group.MinSelections,
                group.MaxSelections,
                group.IsRequired,
                group.SortOrder,
                (group.Options ?? Array.Empty<MenuOptionRequest>())
                    .Select(option => new MenuOptionDefinition(
                        option.Name,
                        option.PriceInCents,
                        option.IsAvailable,
                        option.SortOrder))
                    .ToList()))
            .ToList();
    }
}
