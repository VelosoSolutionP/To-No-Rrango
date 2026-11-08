using ToNoRango.Application.Establishments;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Application.Catalog;

public sealed record PublicMenuDto(
    EstablishmentDto Establishment,
    IReadOnlyList<MenuCategoryDto> Categories)
{
    public static PublicMenuDto From(
        Domain.Establishments.Establishment establishment,
        IReadOnlyList<MenuCategory> categories,
        IReadOnlyList<MenuItem> items) => new(
            EstablishmentDto.From(establishment),
            categories
                .OrderBy(category => category.SortOrder)
                .ThenBy(category => category.Name)
                .Select(category => MenuCategoryDto.From(
                    category,
                    items.Where(item => item.CategoryId == category.Id)
                        .OrderBy(item => item.SortOrder)
                        .ThenBy(item => item.Name)
                        .ToList()))
                .ToList());
}

public sealed record MenuCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<MenuItemDto> Items)
{
    public static MenuCategoryDto From(MenuCategory category, IReadOnlyList<MenuItem> items) => new(
        category.Id,
        category.Name,
        category.Description,
        category.SortOrder,
        category.IsActive,
        items.Select(MenuItemDto.From).ToList());
}

public sealed record MenuItemDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    string? ImageUrl,
    long PriceInCents,
    bool IsActive,
    bool IsAvailable,
    int SortOrder,
    IReadOnlyList<MenuOptionGroupDto> OptionGroups)
{
    public static MenuItemDto From(MenuItem item) => new(
        item.Id,
        item.CategoryId,
        item.Name,
        item.Description,
        item.ImageUrl,
        item.PriceInCents,
        item.IsActive,
        item.IsAvailable,
        item.SortOrder,
        item.OptionGroups
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .Select(MenuOptionGroupDto.From)
            .ToList());
}

public sealed record MenuOptionGroupDto(
    Guid Id,
    string Name,
    int MinSelections,
    int MaxSelections,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<MenuOptionDto> Options)
{
    public static MenuOptionGroupDto From(MenuOptionGroup group) => new(
        group.Id,
        group.Name,
        group.MinSelections,
        group.MaxSelections,
        group.IsRequired,
        group.SortOrder,
        group.Options
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Name)
            .Select(MenuOptionDto.From)
            .ToList());
}

public sealed record MenuOptionDto(
    Guid Id,
    string Name,
    long PriceInCents,
    bool IsAvailable,
    int SortOrder)
{
    public static MenuOptionDto From(MenuOption option) => new(
        option.Id,
        option.Name,
        option.PriceInCents,
        option.IsAvailable,
        option.SortOrder);
}

public sealed record CreateCategoryRequest(string Name, string? Description, int SortOrder);

public sealed record UpdateCategoryRequest(string Name, string? Description, int SortOrder);

public sealed record SetMenuAvailabilityRequest(bool IsAvailable);

public sealed record SetMenuActiveRequest(bool IsActive);

public sealed record CreateMenuItemRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    string? ImageUrl,
    long PriceInCents,
    int SortOrder,
    bool IsAvailable,
    IReadOnlyList<MenuOptionGroupRequest>? OptionGroups);

public sealed record UpdateMenuItemRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    string? ImageUrl,
    long PriceInCents,
    int SortOrder,
    bool IsAvailable,
    IReadOnlyList<MenuOptionGroupRequest>? OptionGroups);

public sealed record MenuOptionGroupRequest(
    string Name,
    int MinSelections,
    int MaxSelections,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<MenuOptionRequest>? Options);

public sealed record MenuOptionRequest(
    string Name,
    long PriceInCents,
    bool IsAvailable,
    int SortOrder);
