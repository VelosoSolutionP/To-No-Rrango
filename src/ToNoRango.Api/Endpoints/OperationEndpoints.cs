using System.Security.Claims;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Delivery;
using ToNoRango.Application.Feedback;
using ToNoRango.Application.Orders;
using ToNoRango.Application.Pdv;
using ToNoRango.Domain.Identity;

namespace ToNoRango.Api.Endpoints;

/// <summary>Kitchen item steps, the register (PDV), courier assignment and customer reviews.</summary>
internal static class OperationEndpoints
{
    private const string Establishment = "/establishments/{establishmentId:guid}";

    public sealed record StartTrackingRequest(Guid? CourierId);

    public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Operação");
        admin.AddEndpointFilter(Access.RequireTenantAccess);
        var operations = admin.MapGroup(string.Empty).RequireModule(Modules.Pedidos);

        operations.MapPatch("/orders/{orderId:guid}/items/{itemId:guid}/prep", async (
            Guid orderId, Guid itemId, AdvanceItemRequest request, HttpContext http, OrderService service, CancellationToken ct) =>
            (await service.AdvanceItemAsync(orderId, itemId, request.Status, http.Actor(), ct)).ToHttpResult())
        .WithName("AvancarItemCozinha");

        operations.MapGroup(string.Empty)
            .RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant)
            .RequireModule(Modules.Rastreio)
            .MapPost("/orders/{orderId:guid}/courier", async (
                Guid orderId, StartTrackingRequest request, DeliveryTrackingService service, CancellationToken ct) =>
                (await service.StartAsync(orderId, request.CourierId, ct)).ToHttpResult())
            .WithName("AtribuirEntregador");

        operations.MapGroup(string.Empty)
            .RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant)
            .MapPost("/orders/{orderId:guid}/delivery-issue/resolve", async (
                Guid orderId, HttpContext http, DeliveryTrackingService service, CancellationToken ct) =>
                (await service.ResolveIssueAsync(orderId, http.Actor(), ct)).ToHttpResult())
            .WithName("ResolverProblemaEntrega");

        var cashiers = operations.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant);

        cashiers.MapGet($"{Establishment}/pdv", async (Guid establishmentId, PdvService service, CancellationToken ct) =>
            (await service.GetAsync(establishmentId, ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/open", async (Guid establishmentId, OpenCashRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.OpenAsync(establishmentId, request, http.Actor(), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/supply", async (Guid establishmentId, CashMovementRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.SupplyAsync(establishmentId, request, http.Actor(), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/withdraw", async (Guid establishmentId, CashMovementRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.WithdrawAsync(establishmentId, request, http.Actor(), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/close", async (Guid establishmentId, CloseCashRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.CloseAsync(establishmentId, request, http.Actor(), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/sales", async (Guid establishmentId, CounterSaleRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.SellAsync(establishmentId, request, http.Actor(), CanDiscount(http), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/pdv/orders/{{orderId:guid}}/receive", async (Guid establishmentId, Guid orderId, ReceiveOrderRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.ReceiveAsync(establishmentId, orderId, request, http.Actor(), ct)).ToHttpResult());

        cashiers.MapGet($"{Establishment}/tables", async (Guid establishmentId, PdvService service, CancellationToken ct) =>
            (await service.ListTablesAsync(establishmentId, ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/tables/{{table}}/orders", async (Guid establishmentId, string table, TableOrderRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.TakeTableOrderAsync(establishmentId, table, request, ActorName(http), ct)).ToHttpResult());

        cashiers.MapPost($"{Establishment}/tables/{{table}}/close", async (Guid establishmentId, string table, CloseTableRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
            (await service.CloseTableAsync(establishmentId, table, request, http.Actor(), ct)).ToHttpResult());

        // Undoing a sale moves money out of the register, so it needs a manager.
        operations.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager)
            .MapPost($"{Establishment}/pdv/sales/{{orderId:guid}}/cancel", async (Guid establishmentId, Guid orderId, CancelSaleRequest request, HttpContext http, PdvService service, CancellationToken ct) =>
                (await service.CancelSaleAsync(establishmentId, orderId, request, http.Actor(), ct)).ToHttpResult());

        operations.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager)
            .MapGet($"{Establishment}/ratings", async (Guid establishmentId, int? days, RatingService service, CancellationToken ct) =>
                (await service.SummaryAsync(establishmentId, days ?? 30, ct)).ToHttpResult());

        var publicGroup = app.MapGroup("/api/public").WithTags("Público");

        publicGroup.MapGet("/orders/{code}/rating", async (string code, RatingService service, CancellationToken ct) =>
            (await service.GetStateAsync(code, ct)).ToHttpResult());

        publicGroup.MapPost("/orders/{code}/rating", async (string code, RateOrderRequest request, RatingService service, CancellationToken ct) =>
            (await service.RateAsync(code, request, ct)).ToHttpResult())
            .RequireRateLimiting("public");

        return app;
    }

    /// <summary>The person's name for the kitchen and the bill; falls back to the e-mail.</summary>
    private static string ActorName(HttpContext http) =>
        http.User.FindFirstValue("name") ?? http.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name) ?? http.Actor();

    private static bool CanDiscount(HttpContext http) =>
        http.IsPlatform() || http.User.FindFirstValue("role") is nameof(UserRole.Owner) or nameof(UserRole.Manager);
}
