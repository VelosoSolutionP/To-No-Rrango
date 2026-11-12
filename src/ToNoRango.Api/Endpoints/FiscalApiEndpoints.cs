using Microsoft.AspNetCore.Mvc;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Establishments;
using ToNoRango.Infrastructure.Persistence;

namespace ToNoRango.Api.Endpoints;

/// <summary>
/// Fiscal API of the ZEST: the single door other products and partners use to issue and read notes
/// of a store. It authenticates by store key (X-Api-Key), never by a user session.
/// </summary>
internal static class FiscalApiEndpoints
{
    public const string RateLimitPolicy = "fiscal-api";
    private const string CallerItem = "zest.fiscal.caller";
    private const string Establishment = "/establishments/{establishmentId:guid}";

    public static FiscalApiCaller? FiscalCaller(this HttpContext http) => http.Items[CallerItem] as FiscalApiCaller;

    public static IEndpointRouteBuilder MapFiscalApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/fiscal/v1").WithTags("API Fiscal").AllowAnonymous();
        api.AddEndpointFilter(AuthenticateAsync);
        api.AddEndpointFilter(FiscalAudit.Filter);
        api.RequireRateLimiting(RateLimitPolicy);

        api.MapGet("/me", (HttpContext http) =>
        {
            var caller = http.FiscalCaller()!;
            return Results.Ok(new { caller.Name, caller.Prefix, caller.EstablishmentId, caller.IsTest, caller.CanIssue });
        });

        api.MapGet("/status", async (HttpContext http, int? model, DfeService service, CancellationToken ct) =>
        {
            var caller = http.FiscalCaller()!;

            return caller.IsTest
                ? Results.Ok(new { accepted = true, message = "Chave de teste: as notas são simuladas, a SEFAZ não é consultada.", protocol = (string?)null })
                : (await service.StatusAsync(caller.EstablishmentId, model ?? 65, ct)).ToHttpResult();
        });

        api.MapGet("/notes", async (HttpContext http, DateTimeOffset? from, DateTimeOffset? to, string? externalId, int? take, FiscalApiService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(http.FiscalCaller()!, from, to, externalId, take, ct)));

        api.MapPost("/notes", async (HttpContext http, ApiNoteRequest request, FiscalApiService service, CancellationToken ct) =>
        {
            var issued = await service.IssueAsync(http.FiscalCaller()!, request, ct);
            return issued.IsSuccess ? Results.Created($"/api/fiscal/v1/notes/{issued.Value.Id}", issued.Value) : issued.ToHttpResult();
        });

        api.MapGet("/notes/{noteId:guid}", async (HttpContext http, Guid noteId, FiscalApiService service, CancellationToken ct) =>
            (await service.GetAsync(http.FiscalCaller()!, noteId, ct)).ToHttpResult());

        api.MapGet("/notes/{noteId:guid}/xml", async (HttpContext http, Guid noteId, FiscalApiService service, CancellationToken ct) =>
        {
            var xml = await service.GetXmlAsync(http.FiscalCaller()!, noteId, ct);
            return xml.IsSuccess ? Results.Text(xml.Value, "application/xml; charset=utf-8") : xml.ToHttpResult();
        });

        api.MapPost("/notes/{noteId:guid}/cancel", async (HttpContext http, Guid noteId, CancelNoteRequest request, FiscalApiService service, CancellationToken ct) =>
            (await service.CancelAsync(http.FiscalCaller()!, noteId, request, ct)).ToHttpResult());

        api.MapPost("/notes/{noteId:guid}/correction", async (HttpContext http, Guid noteId, CorrectionRequest request, FiscalApiService service, CancellationToken ct) =>
            (await service.CorrectAsync(http.FiscalCaller()!, noteId, request, ct)).ToHttpResult());

        NfseEndpoints.MapApi(api);

        // Books are the store's real data: only a live key reads them.
        api.MapGet("/books/assessment", async (HttpContext http, int year, int month, long? previousIcmsCredit, FiscalBooksService service, CancellationToken ct) =>
            http.FiscalCaller()!.IsTest
                ? TestKeyRefused()
                : (await service.GetAssessmentAsync(http.FiscalCaller()!.EstablishmentId, new FiscalPeriodRequest(year, month, previousIcmsCredit ?? 0), ct)).ToHttpResult());

        api.MapGet("/books/register", async (HttpContext http, int year, int month, FiscalBooksService service, CancellationToken ct) =>
            http.FiscalCaller()!.IsTest
                ? TestKeyRefused()
                : (await service.GetBookAsync(http.FiscalCaller()!.EstablishmentId, new FiscalPeriodRequest(year, month), ct)).ToHttpResult());

        return app;
    }

    /// <summary>Keys, webhooks and deliveries, managed by the store in the panel.</summary>
    public static void MapFiscalApiManagement(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/fiscal/api", async (Guid establishmentId, FiscalWebhookService service, CancellationToken ct) =>
            Results.Ok(await service.GetOverviewAsync(establishmentId, ct)));

        group.MapPost($"{Establishment}/fiscal/api/keys", async (Guid establishmentId, CreateApiKeyRequest request, HttpContext http, ITenantContext tenant, IRepository<Establishment> establishments, FiscalApiKeyService service, CancellationToken ct) =>
            await establishments.AnyAsync(item => item.Id == establishmentId, ct)
                ? (await service.CreateAsync(tenant.CurrentTenantId!.Value, establishmentId, request, http.Actor(), ct)).ToHttpResult()
                : Results.NotFound());

        group.MapDelete($"{Establishment}/fiscal/api/keys/{{keyId:guid}}", async (Guid establishmentId, Guid keyId, FiscalApiKeyService service, CancellationToken ct) =>
            (await service.RevokeAsync(establishmentId, keyId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/api/webhooks", async (Guid establishmentId, CreateWebhookRequest request, ITenantContext tenant, IRepository<Establishment> establishments, FiscalWebhookService service, CancellationToken ct) =>
            await establishments.AnyAsync(item => item.Id == establishmentId, ct)
                ? (await service.CreateAsync(tenant.CurrentTenantId!.Value, establishmentId, request, ct)).ToHttpResult()
                : Results.NotFound());

        group.MapDelete($"{Establishment}/fiscal/api/webhooks/{{webhookId:guid}}", async (Guid establishmentId, Guid webhookId, FiscalWebhookService service, CancellationToken ct) =>
            (await service.RemoveAsync(establishmentId, webhookId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/api/webhooks/{{webhookId:guid}}/test", async (Guid establishmentId, Guid webhookId, FiscalWebhookService service, CancellationToken ct) =>
            (await service.TestAsync(establishmentId, webhookId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/api/deliveries/{{deliveryId:guid}}/retry", async (Guid establishmentId, Guid deliveryId, FiscalWebhookService service, CancellationToken ct) =>
            (await service.RetryAsync(establishmentId, deliveryId, ct)).ToHttpResult());
    }

    private static async ValueTask<object?> AuthenticateAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var caller = await services.GetRequiredService<FiscalApiKeyService>().AuthenticateAsync(http.Request.Headers["X-Api-Key"].ToString(), http.RequestAborted);

        if (caller is null)
        {
            return Results.Problem(
                title: "fiscal_api.unauthorized",
                detail: "Envie uma chave válida no header X-Api-Key. As chaves são criadas no painel, em Fiscal > API e integrações.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        services.GetRequiredService<ITenantContext>().SetTenant(caller.TenantId);
        http.Items[CallerItem] = caller;

        var modules = services.GetRequiredService<IModuleAccess>();

        if (!await modules.IsAllowedAsync(caller.TenantId, Modules.Fiscal, http.RequestAborted))
        {
            return ModuleMissing("O módulo fiscal não está no contrato desta loja.");
        }

        // Real notes need the emission module; simulated ones are free so a partner can build before the store buys it.
        var changesNotes = HttpMethods.IsPost(http.Request.Method);

        if (changesNotes && !caller.IsTest && !await modules.IsAllowedAsync(caller.TenantId, Modules.FiscalNfce, http.RequestAborted))
        {
            return ModuleMissing("A emissão de notas não está no contrato desta loja.");
        }

        return await next(context);
    }

    private static IResult ModuleMissing(string detail) =>
        Results.Problem(title: "contract.module_unavailable", detail: detail, statusCode: StatusCodes.Status403Forbidden);

    private static IResult TestKeyRefused() =>
        Results.Problem(title: "fiscal_api.test_key", detail: "Chave de teste não lê os livros reais da loja. Use uma chave de produção.", statusCode: StatusCodes.Status403Forbidden);
}
