using System.Security.Claims;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Infrastructure.Persistence;

namespace ToNoRango.Api.Endpoints;

/// <summary>
/// Accountant portal: an office signs in once and serves every store that invited it. Each company
/// route opens only through an active link, and then runs inside that store's tenant.
/// </summary>
internal static class AccountantEndpoints
{
    private const string FirmItem = "zest.accountant.firm";
    private const string LinkItem = "zest.accountant.link";
    private const string Establishment = "/establishments/{establishmentId:guid}";

    public static IEndpointRouteBuilder MapAccountantEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/accountant/auth").WithTags("Portal do contador").AllowAnonymous().RequireRateLimiting("login");

        auth.MapPost("/signup", async (AccountantSignUpRequest request, AccountantPortalService service, CancellationToken ct) =>
            (await service.SignUpAsync(request, ct)).ToHttpResult());

        auth.MapPost("/login", async (AccountantLoginRequest request, AccountantPortalService service, CancellationToken ct) =>
            (await service.LoginAsync(request, ct)).ToHttpResult());

        var portal = app.MapGroup("/api/accountant").WithTags("Portal do contador");
        portal.AddEndpointFilter(RequireAccountantAsync);

        portal.MapGet("/me", async (HttpContext http, AccountantPortalService service, CancellationToken ct) =>
            (await service.GetMeAsync(http.User.UserId()!.Value, ct)).ToHttpResult());

        portal.MapGet("/companies", async (HttpContext http, AccountantPortalService service, CancellationToken ct) =>
            Results.Ok(await service.ListCompaniesAsync(Firm(http), ct)));

        portal.MapPost("/companies/accept", async (HttpContext http, AcceptInviteRequest request, AccountantPortalService service, CancellationToken ct) =>
            (await service.AcceptAsync(Firm(http), http.User.FindFirstValue(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Email) ?? string.Empty, request, ct)).ToHttpResult());

        portal.MapPost("/partnership", async (HttpContext http, AccountantPortalService service, CancellationToken ct) =>
            (await service.RequestPartnershipAsync(Firm(http), ct)).ToHttpResult());

        var company = portal.MapGroup(Establishment);
        company.AddEndpointFilter(RequireLinkAsync);
        company.AddEndpointFilter(FiscalAudit.Filter);

        // Accounting offices read the books; software houses plug systems in. Each sees only its part.
        var books = company.MapGroup(string.Empty);
        books.AddEndpointFilter(RequireKind(PartnerKind.Accounting));
        var integration = company.MapGroup(string.Empty);
        integration.AddEndpointFilter(RequireKind(PartnerKind.SoftwareHouse));
        MapSoftwareHouse(integration);

        books.MapGet("/fiscal/review", async (Guid establishmentId, FiscalReviewService service, CancellationToken ct) =>
            (await service.ReviewAsync(establishmentId, ct)).ToHttpResult());

        books.MapGet("/fiscal/book", async (Guid establishmentId, int year, int month, FiscalBooksService service, CancellationToken ct) =>
            (await service.GetBookAsync(establishmentId, new FiscalPeriodRequest(year, month), ct)).ToHttpResult());

        books.MapGet("/fiscal/assessment", async (Guid establishmentId, int year, int month, long? previousIcmsCredit, FiscalBooksService service, CancellationToken ct) =>
            (await service.GetAssessmentAsync(establishmentId, new FiscalPeriodRequest(year, month, previousIcmsCredit ?? 0), ct)).ToHttpResult());

        books.MapGet("/fiscal/periods", async (Guid establishmentId, int? months, FiscalBooksService service, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await service.GetPeriodsAsync(establishmentId, months ?? 12, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct)));

        books.MapGet("/fiscal/audit", async (Guid establishmentId, int? take, FiscalBooksService service, CancellationToken ct) =>
            Results.Ok(await service.GetAuditAsync(establishmentId, take ?? 200, ct)));

        books.MapPost("/fiscal/accountant-package", async (Guid establishmentId, AccountantPackageRequest request, AccountantPackageService service, HttpContext http, IModuleAccess access, CancellationToken ct) =>
        {
            var tenant = Link(http).TenantId;
            var allowed = new List<string>();

            foreach (var module in new[] { Modules.FiscalSintegra, Modules.FiscalSped })
            {
                if (await access.IsAllowedAsync(tenant, module, ct))
                {
                    allowed.Add(module);
                }
            }

            return (await service.BuildAsync(establishmentId, allowed, request, ct)).ToHttpResult();
        });

        company.MapGet("/fiscal/accountant/messages", async (Guid establishmentId, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            Results.Ok(await service.ConversationAsync(establishmentId, Link(http).Id, Side(http), ct)));

        company.MapPost("/fiscal/accountant/messages", async (Guid establishmentId, PostAccountantMessageRequest request, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            (await service.PostAsync(establishmentId, Link(http).Id, Side(http), request, ct)).ToHttpResult());

        company.MapPost("/fiscal/accountant/messages/{messageId:guid}/resolve", async (Guid establishmentId, Guid messageId, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            (await service.ResolveAsync(establishmentId, Link(http).Id, messageId, Side(http), ct)).ToHttpResult());

        // Partner program: the Veloso Solution sees the offices and approves partners.
        var platform = app.MapGroup("/api/platform/accountants").WithTags("Plataforma");
        platform.AddEndpointFilter(Access.RequirePlatformKey);

        platform.MapGet(string.Empty, async (AccountantPortalService service, CancellationToken ct) =>
            Results.Ok(await service.ListFirmsAsync(ct)));

        platform.MapPut("/{firmId:guid}/partner", async (Guid firmId, SetPartnerRequest request, AccountantPortalService service, CancellationToken ct) =>
            (await service.SetPartnerAsync(firmId, request, ct)).ToHttpResult());

        return app;
    }

    /// <summary>The store's side, in the panel: invite, revoke and talk.</summary>
    public static void MapStoreSide(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/fiscal/accountant", async (Guid establishmentId, AccountantDeskService service, CancellationToken ct) =>
            Results.Ok(await service.GetDeskAsync(establishmentId, ct)));

        group.MapPost($"{Establishment}/fiscal/accountant/invites", async (Guid establishmentId, InviteAccountantRequest request, HttpContext http, ITenantContext tenant, AccountantDeskService service, CancellationToken ct) =>
            (await service.InviteAsync(tenant.CurrentTenantId!.Value, establishmentId, request, http.Actor(), ct)).ToHttpResult());

        group.MapDelete($"{Establishment}/fiscal/accountant/links/{{linkId:guid}}", async (Guid establishmentId, Guid linkId, AccountantDeskService service, CancellationToken ct) =>
            (await service.RevokeAsync(establishmentId, linkId, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/fiscal/accountant/messages", async (Guid establishmentId, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            Results.Ok(await service.ConversationAsync(establishmentId, null, StoreSide(http), ct)));

        group.MapPost($"{Establishment}/fiscal/accountant/messages", async (Guid establishmentId, PostAccountantMessageRequest request, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            (await service.PostAsync(establishmentId, null, StoreSide(http), request, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/accountant/messages/{{messageId:guid}}/resolve", async (Guid establishmentId, Guid messageId, HttpContext http, AccountantDeskService service, CancellationToken ct) =>
            (await service.ResolveAsync(establishmentId, null, messageId, StoreSide(http), ct)).ToHttpResult());
    }

    private static void MapSoftwareHouse(RouteGroupBuilder group)
    {
        group.MapGet("/fiscal/api", async (Guid establishmentId, HttpContext http, FiscalWebhookService service, CancellationToken ct) =>
            Results.Ok(await service.GetOverviewAsync(establishmentId, ct, Firm(http))));

        group.MapPost("/fiscal/api/keys", async (Guid establishmentId, CreateApiKeyRequest request, HttpContext http, FiscalApiKeyService service, CancellationToken ct) =>
            (await service.CreateAsync(Link(http).TenantId, establishmentId, request, http.Actor(), ct, Firm(http))).ToHttpResult());

        group.MapDelete("/fiscal/api/keys/{keyId:guid}", async (Guid establishmentId, Guid keyId, HttpContext http, FiscalApiKeyService service, CancellationToken ct) =>
            (await service.RevokeAsync(establishmentId, keyId, ct, Firm(http))).ToHttpResult());

        group.MapPost("/fiscal/api/webhooks", async (Guid establishmentId, CreateWebhookRequest request, HttpContext http, FiscalWebhookService service, CancellationToken ct) =>
            (await service.CreateAsync(Link(http).TenantId, establishmentId, request, ct, Firm(http))).ToHttpResult());

        group.MapDelete("/fiscal/api/webhooks/{webhookId:guid}", async (Guid establishmentId, Guid webhookId, HttpContext http, FiscalWebhookService service, CancellationToken ct) =>
            (await service.RemoveAsync(establishmentId, webhookId, ct, Firm(http))).ToHttpResult());

        group.MapPost("/fiscal/api/webhooks/{webhookId:guid}/test", async (Guid establishmentId, Guid webhookId, HttpContext http, FiscalWebhookService service, CancellationToken ct) =>
            (await service.TestAsync(establishmentId, webhookId, ct, Firm(http))).ToHttpResult());

        group.MapPost("/fiscal/api/deliveries/{deliveryId:guid}/retry", async (Guid establishmentId, Guid deliveryId, HttpContext http, FiscalWebhookService service, CancellationToken ct) =>
            (await service.RetryAsync(establishmentId, deliveryId, ct, Firm(http))).ToHttpResult());
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireKind(PartnerKind kind) =>
        async (context, next) => Link(context.HttpContext).Kind == kind
            ? await next(context)
            : Results.Problem(
                title: "partner.wrong_kind",
                detail: kind == PartnerKind.Accounting ? "Esta área é do escritório contábil da empresa." : "Esta área é da software house da empresa.",
                statusCode: StatusCodes.Status403Forbidden);

    public static bool IsAccountant(this ClaimsPrincipal user) => user.FindFirstValue("role") == JwtTokenIssuer.AccountantRole;

    private static AccountantSide StoreSide(HttpContext http) =>
        new(false, http.User.FindFirstValue(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Name) ?? http.Actor());

    private static AccountantSide Side(HttpContext http) =>
        new(true, http.User.FindFirstValue(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Name) ?? "Contador");

    private static Guid Firm(HttpContext http) => (Guid)http.Items[FirmItem]!;

    private static AccountantLink Link(HttpContext http) => (AccountantLink)http.Items[LinkItem]!;

    private static async ValueTask<object?> RequireAccountantAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (http.User.Identity?.IsAuthenticated != true || !http.User.IsAccountant()
            || !Guid.TryParse(http.User.FindFirstValue(JwtTokenIssuer.AccountantFirmClaim), out var firmId)
            || http.User.UserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        // A removed person loses access at once, not when the token expires.
        if (!await http.RequestServices.GetRequiredService<AccountantPortalService>().IsActiveAsync(userId, firmId, http.RequestAborted))
        {
            return Results.Unauthorized();
        }

        http.Items[FirmItem] = firmId;
        return await next(context);
    }

    private static async ValueTask<object?> RequireLinkAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;

        if (!Guid.TryParse(http.Request.RouteValues["establishmentId"]?.ToString(), out var establishmentId)
            || await services.GetRequiredService<AccountantPortalService>().FindLinkAsync(Firm(http), establishmentId, http.RequestAborted) is not { } link)
        {
            return Results.Problem(title: "accountant.no_access", detail: "Seu escritório não tem acesso a esta empresa.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await services.GetRequiredService<IModuleAccess>().IsAllowedAsync(link.TenantId, Modules.Fiscal, http.RequestAborted))
        {
            return Results.Problem(title: "contract.module_unavailable", detail: "O módulo fiscal desta empresa está desligado.", statusCode: StatusCodes.Status403Forbidden);
        }

        // From here every query runs fenced to this store's tenant, as in the store's own panel.
        services.GetRequiredService<ITenantContext>().SetTenant(link.TenantId);
        http.Items[LinkItem] = link;
        return await next(context);
    }
}
