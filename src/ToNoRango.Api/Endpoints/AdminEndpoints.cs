using ToNoRango.Application.Catalog;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Establishments;
using ToNoRango.Application.Orders;
using ToNoRango.Domain.Identity;
using ToNoRango.Domain.Orders;
using ToNoRango.Infrastructure.Persistence;

namespace ToNoRango.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Administração");

        group.AddEndpointFilter(Access.RequireTenantAccess);

        var managers = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager);
        var owners = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner);
        var catalog = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Cardapio);
        var operations = group.MapGroup(string.Empty).RequireModule(Modules.Pedidos);

        group.MapGet("/establishments", async (
            EstablishmentService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ListarEstabelecimentos");

        group.MapGet("/establishments/{establishmentId:guid}", async (
            Guid establishmentId,
            EstablishmentService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(establishmentId, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ObterEstabelecimento");

        owners.MapPost("/establishments", async (
            CreateEstablishmentRequest request,
            EstablishmentService service,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.CurrentTenantId != request.TenantId)
            {
                return Results.Problem(
                    title: "tenant.mismatch",
                    detail: "O tenant do corpo não corresponde ao tenant autenticado.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await service.CreateAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CriarEstabelecimento");

        managers.MapPut("/establishments/{establishmentId:guid}", async (
            Guid establishmentId,
            UpdateEstablishmentRequest request,
            EstablishmentService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(establishmentId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AtualizarEstabelecimento");

        group.MapGet("/establishments/{establishmentId:guid}/menu", async (
            Guid establishmentId,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAdminMenuAsync(establishmentId, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ObterCardapioAdministrativo");

        catalog.MapPost("/establishments/{establishmentId:guid}/categories", async (
            Guid establishmentId,
            CreateCategoryRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateCategoryAsync(establishmentId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CriarCategoria");

        catalog.MapPut("/categories/{categoryId:guid}", async (
            Guid categoryId,
            UpdateCategoryRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateCategoryAsync(categoryId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AtualizarCategoria");

        catalog.MapPatch("/categories/{categoryId:guid}/active", async (
            Guid categoryId,
            SetMenuActiveRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetCategoryActiveAsync(categoryId, request.IsActive, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AlterarCategoriaAtiva");

        catalog.MapDelete("/categories/{categoryId:guid}", async (
            Guid categoryId,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeactivateCategoryAsync(categoryId, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("DesativarCategoria");

        catalog.MapPost("/establishments/{establishmentId:guid}/items", async (
            Guid establishmentId,
            CreateMenuItemRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateItemAsync(establishmentId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CriarItem");

        catalog.MapPut("/items/{itemId:guid}", async (
            Guid itemId,
            UpdateMenuItemRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateItemAsync(itemId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AtualizarItem");

        catalog.MapDelete("/items/{itemId:guid}", async (
            Guid itemId,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeactivateItemAsync(itemId, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("DesativarItem");

        catalog.MapPatch("/items/{itemId:guid}/active", async (
            Guid itemId,
            SetMenuActiveRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetItemActiveAsync(itemId, request.IsActive, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AlterarItemAtivo");

        catalog.MapPatch("/items/{itemId:guid}/availability", async (
            Guid itemId,
            SetMenuAvailabilityRequest request,
            MenuService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetItemAvailabilityAsync(itemId, request.IsAvailable, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AlterarDisponibilidadeItem");

        operations.MapGet("/establishments/{establishmentId:guid}/orders", async (
            Guid establishmentId,
            OrderStatus? status,
            int? take,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(establishmentId, status, take ?? 50, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ListarPedidos");

        operations.MapPatch("/orders/{orderId:guid}/status", async (
            Guid orderId,
            UpdateOrderStatusRequest request,
            HttpContext http,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateStatusAsync(orderId, request with { ChangedBy = http.Actor() }, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AtualizarStatusPedido");

        operations.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant).RequireModule(Modules.Rastreio).MapPost("/orders/{orderId:guid}/tracking", async (
            Guid orderId,
            ToNoRango.Application.Delivery.DeliveryTrackingService service,
            CancellationToken cancellationToken) =>
            (await service.StartAsync(orderId, cancellationToken)).ToHttpResult())
        .WithName("IniciarRastreio");

        operations.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant).MapPost("/orders/{orderId:guid}/paid", async (
            Guid orderId,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.MarkPaidAsync(orderId, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("MarcarPedidoPago");

        return app;
    }
}
