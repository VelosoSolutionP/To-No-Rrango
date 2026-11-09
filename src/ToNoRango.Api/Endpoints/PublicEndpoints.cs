using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Catalog;
using ToNoRango.Application.Delivery;
using ToNoRango.Application.Orders;
using ToNoRango.Application.Parties;
using ToNoRango.Application.Payments;
using ToNoRango.Domain.Delivery;

namespace ToNoRango.Api.Endpoints;

internal static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").WithTags("Público");

        group.MapGet("/establishments/{slug}/menu", async (
            string slug,
            MenuService menuService,
            CancellationToken cancellationToken) =>
        {
            var result = await menuService.GetPublicMenuAsync(slug, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ObterCardapioPublico")
        .AllowAnonymous();

        group.MapPost("/establishments/{slug}/orders", async (
            string slug,
            CreateOrderRequest request,
            OrderService orderService,
            CancellationToken cancellationToken) =>
        {
            var result = await orderService.CreateAsync(slug, request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CriarPedidoPublico")
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapGet("/orders/{code}", async (
            string code,
            OrderService orderService,
            CancellationToken cancellationToken) =>
        {
            var result = await orderService.TrackAsync(code, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AcompanharPedido")
        .AllowAnonymous();

        group.MapGet("/media/{id:guid}", async (Guid id, HttpContext http, ToNoRango.Application.Media.MediaService media, CancellationToken cancellationToken) =>
        {
            var file = await media.GetAsync(id, cancellationToken);

            if (file is null)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            http.Response.Headers.XContentTypeOptions = "nosniff";

            return Results.File(file.Data, file.ContentType);
        })
        .AllowAnonymous();

        group.MapGet("/establishments/{slug}/checkout-options", async (
            string slug,
            IEstablishmentRepository establishments,
            IRepository<DeliveryZone> zones,
            PaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var establishment = await establishments.GetBySlugAsync(slug, cancellationToken);

            if (establishment is null)
            {
                return Results.NotFound();
            }

            var hasZones = await zones.AnyAsync(zone => zone.EstablishmentId == establishment.Id && zone.IsActive, cancellationToken);

            return Results.Ok(await payments.GetCheckoutOptionsAsync(establishment.TenantId, establishment.Id, hasZones, cancellationToken));
        })
        .AllowAnonymous();

        group.MapGet("/establishments/{slug}/delivery-quote", async (
            string slug,
            string district,
            DeliveryService delivery,
            CancellationToken cancellationToken) =>
            (await delivery.QuoteBySlugAsync(slug, district, cancellationToken)).ToHttpResult())
        .AllowAnonymous();

        group.MapGet("/lookups/cep/{cep}", async (string cep, PartyService parties, CancellationToken cancellationToken) =>
            (await parties.LookupCepAsync(cep, cancellationToken)).ToHttpResult())
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapGet("/orders/{code}/events", async (
            string code,
            OrderService orders,
            IOrderEvents events,
            CancellationToken cancellationToken) =>
        {
            var order = await orders.TrackAsync(code, cancellationToken);

            return order.IsFailure
                ? order.ToHttpResult()
                : Results.ServerSentEvents(events.ListenAsync(code, TimeSpan.FromSeconds(25), cancellationToken));
        })
        .AllowAnonymous();

        group.MapGet("/orders/{code}/payment-options", async (
            string code,
            IOrderRepository orders,
            PaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var order = await orders.GetByCodeAsync(code, cancellationToken);

            return order is null
                ? Results.NotFound()
                : Results.Ok(await payments.GetCheckoutOptionsAsync(order.TenantId, order.EstablishmentId, false, cancellationToken));
        })
        .AllowAnonymous();

        group.MapGet("/orders/{code}/tracking", async (string code, DeliveryTrackingService tracking, CancellationToken cancellationToken) =>
            Results.Ok(await tracking.GetPublicAsync(code, cancellationToken)))
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapGet("/tracking/{token}", async (string token, DeliveryTrackingService tracking, CancellationToken cancellationToken) =>
            Results.Ok(await tracking.GetCourierSessionAsync(token, cancellationToken)))
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapPost("/tracking/{token}/position", async (string token, CourierPositionRequest request, DeliveryTrackingService tracking, CancellationToken cancellationToken) =>
            (await tracking.ReportPositionAsync(token, request, cancellationToken)).ToHttpResult())
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapPost("/tracking/{token}/finish", async (string token, DeliveryTrackingService tracking) =>
        {
            await tracking.FinishAsync(token);
            return Results.NoContent();
        })
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapGet("/orders/{code}/payment", async (string code, PaymentService payments, CancellationToken cancellationToken) =>
            (await payments.GetCurrentAsync(code, cancellationToken)).ToHttpResult())
        .AllowAnonymous();

        group.MapPost("/orders/{code}/payments/pix", async (
            string code,
            CreatePixRequest request,
            PaymentService payments,
            CancellationToken cancellationToken) =>
            (await payments.CreatePixAsync(code, request, cancellationToken)).ToHttpResult())
        .RequireRateLimiting("public")
        .AllowAnonymous();

        group.MapPost("/orders/{code}/payments/card", async (
            string code,
            CreateCardRequest request,
            PaymentService payments,
            CancellationToken cancellationToken) =>
            (await payments.CreateCardAsync(code, request, cancellationToken)).ToHttpResult())
        .RequireRateLimiting("public")
        .AllowAnonymous();

        return app;
    }
}
