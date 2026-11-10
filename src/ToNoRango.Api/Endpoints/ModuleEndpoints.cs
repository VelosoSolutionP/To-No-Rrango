using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Delivery;
using ToNoRango.Application.Finance;
using ToNoRango.Application.Fiscal;
using ToNoRango.Application.Identity;
using ToNoRango.Application.Inventory;
using ToNoRango.Application.Media;
using ToNoRango.Application.Messaging;
using ToNoRango.Application.Parties;
using ToNoRango.Application.Payments;
using ToNoRango.Application.Zest;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Identity;
using ToNoRango.Infrastructure.Persistence;

namespace ToNoRango.Api.Endpoints;

internal static class ModuleEndpoints
{
    private const string Establishment = "/establishments/{establishmentId:guid}";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, AuthService service, VelosoIdOptions veloso, CancellationToken ct) =>
            veloso.AllowsPassword(request.Email)
                ? (await service.LoginAsync(request, ct)).ToHttpResult()
                : Results.NotFound())
            .WithTags("Autenticação")
            .RequireRateLimiting("login")
            .AllowAnonymous();

        return app;
    }

    /// <summary>Provisioning used by the Portal; protected by the platform key, never by a user token.</summary>
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform/tenants/{tenantId:guid}").WithTags("Plataforma");
        platform.AddEndpointFilter(Access.RequirePlatformKey);

        platform.MapPut("/contract", async (Guid tenantId, PublishContractRequest request, ContractService service, CancellationToken ct) =>
            (await service.PublishAsync(tenantId, request, ct)).ToHttpResult());

        platform.MapGet("/contract", async (Guid tenantId, ContractService service, CancellationToken ct) =>
            Results.Ok(await service.GetStateAsync(tenantId, ct)));

        platform.MapPost("/users", async (Guid tenantId, CreateUserRequest request, AuthService service, CancellationToken ct) =>
            (await service.CreateAsync(tenantId, request, ct)).ToHttpResult());

        return app;
    }

    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Módulos");
        admin.AddEndpointFilter(Access.RequireTenantAccess);

        admin.MapGet("/me", async (HttpContext http, AuthService auth, ContractService contracts, ITenantContext tenant, CancellationToken ct) =>
        {
            var contract = await contracts.GetStateAsync(tenant.CurrentTenantId!.Value, ct);

            if (http.User.UserId() is not { } userId)
            {
                return Results.Ok(new { user = (UserDto?)null, contract });
            }

            var user = await auth.GetAsync(userId, ct);

            return user.IsSuccess ? Results.Ok(new { user = user.Value, contract }) : user.ToHttpResult();
        });

        admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).MapPost($"{Establishment}/media", async (Guid establishmentId, HttpRequest request, IAppUrls urls, MediaService media, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || (await request.ReadFormAsync(ct)).Files.GetFile("file") is not { } file)
            {
                return Results.Problem(title: "media.missing", detail: "Selecione uma foto.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (file.Length > MediaService.MaxBytes)
            {
                return Results.Problem(title: "media.too_large", detail: "A foto precisa ter até 5 MB.", statusCode: StatusCodes.Status400BadRequest);
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);

            var uploaded = await media.UploadAsync(establishmentId, buffer.ToArray(), ct);

            if (uploaded.IsFailure)
            {
                return uploaded.ToHttpResult();
            }

            var baseUrl = urls.PublicApiUrl ?? $"{request.Scheme}://{request.Host}";

            return Results.Ok(new { url = $"{baseUrl}/api/public/media/{uploaded.Value.Id}" });
        }).DisableAntiforgery();

        MapUsers(admin.MapGroup("/users").RequireRoles(UserRole.Owner));
        MapDelivery(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Pedidos));
        MapCouriers(admin.MapGroup(string.Empty).RequireModule(Modules.Rastreio));
        MapPayments(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Pagamentos));
        MapCustomers(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant).RequireModule(Modules.Clientes));
        MapInventory(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Estoque));
        MapFinance(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Financeiro));
        MapReports(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Relatorios));
        MapFiscal(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Fiscal));
        MapFiscalNfce(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.FiscalNfce));
        var fiscalManagers = admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager);
        MapFiscalFile(fiscalManagers.MapGroup(string.Empty).RequireModule(Modules.FiscalSintegra), "sintegra", FiscalFileKind.Sintegra);
        MapFiscalFile(fiscalManagers.MapGroup(string.Empty).RequireModule(Modules.FiscalSped), "efd-icms-ipi", FiscalFileKind.EfdIcmsIpi);
        MapFiscalFile(fiscalManagers.MapGroup(string.Empty).RequireModule(Modules.FiscalSped), "efd-contribuicoes", FiscalFileKind.EfdContribuicoes);
        MapWhatsApp(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant).RequireModule(Modules.WhatsApp));
        MapZest(admin.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager).RequireModule(Modules.Ia));

        return app;
    }

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var webhooks = app.MapGroup("/api/webhooks").WithTags("Webhooks").AllowAnonymous();

        // ZEST relays customer messages here and delivers the replies; the store key goes in X-Zest-Key.
        app.MapPost("/api/zest/{establishmentId:guid}/messages", async (Guid establishmentId, ConnectorMessageRequest request, HttpContext http, ZestConnectorService service, CancellationToken ct) =>
            (await service.RelayAsync(establishmentId, http.Request.Headers["X-Zest-Key"].ToString(), request, ct)).ToHttpResult())
            .WithTags("ZEST")
            .AllowAnonymous()
            .RequireRateLimiting("zest");

        webhooks.MapPost("/mercadopago/{establishmentId:guid}", async (Guid establishmentId, HttpContext http, PaymentService service, CancellationToken ct) =>
        {
            var type = http.Request.Query["type"].ToString();
            var topic = http.Request.Query["topic"].ToString();

            if (type != "payment" && topic != "payment")
            {
                return Results.Ok();
            }

            var dataId = http.Request.Query["data.id"].ToString();

            if (string.IsNullOrEmpty(dataId))
            {
                dataId = http.Request.Query["id"].ToString();
            }

            var result = await service.HandleWebhookAsync(
                establishmentId,
                dataId,
                http.Request.Headers["x-signature"],
                http.Request.Headers["x-request-id"],
                ct);

            return result.ToHttpResult();
        });

        webhooks.MapPost("/evolution/{establishmentId:guid}/{token}", async (Guid establishmentId, string token, JsonElement payload, WhatsAppService service, CancellationToken ct) =>
            (await service.HandleWebhookAsync(establishmentId, token, payload, ct)).ToHttpResult());

        return app;
    }

    private static void MapUsers(RouteGroupBuilder users)
    {
        users.MapGet(string.Empty, async (ITenantContext tenant, AuthService service, CancellationToken ct) =>
            (await service.ListAsync(tenant.CurrentTenantId!.Value, ct)).ToHttpResult());

        users.MapPost(string.Empty, async (CreateUserRequest request, ITenantContext tenant, AuthService service, CancellationToken ct) =>
            (await service.CreateAsync(tenant.CurrentTenantId!.Value, request, ct)).ToHttpResult());

        users.MapPatch("/{userId:guid}/active", async (Guid userId, SetUserActiveRequest request, AuthService service, CancellationToken ct) =>
            (await service.SetActiveAsync(userId, request.IsActive, ct)).ToHttpResult());
    }

    private static void MapDelivery(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/delivery-zones", async (Guid establishmentId, DeliveryService service, CancellationToken ct) =>
            (await service.ListAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/delivery-zones", async (Guid establishmentId, DeliveryZoneRequest request, DeliveryService service, CancellationToken ct) =>
            (await service.CreateAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut("/delivery-zones/{zoneId:guid}", async (Guid zoneId, DeliveryZoneRequest request, DeliveryService service, CancellationToken ct) =>
            (await service.UpdateAsync(zoneId, request, ct)).ToHttpResult());
    }

    private static void MapCouriers(RouteGroupBuilder group)
    {
        // A atendente envia o link da entrega, então precisa ver a lista, mas não altera o cadastro.
        var viewers = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager, UserRole.Attendant);

        viewers.MapGet($"{Establishment}/couriers", async (Guid establishmentId, CourierService service, CancellationToken ct) =>
            (await service.ListAsync(establishmentId, ct)).ToHttpResult());

        viewers.MapGet($"{Establishment}/live-deliveries", async (Guid establishmentId, DeliveryTrackingService service, CancellationToken ct) =>
            Results.Ok(await service.ListLiveAsync(establishmentId, ct)));

        var managers = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager);

        managers.MapPost($"{Establishment}/couriers", async (Guid establishmentId, CourierRequest request, CourierService service, CancellationToken ct) =>
            (await service.CreateAsync(establishmentId, request, ct)).ToHttpResult());

        managers.MapPut("/couriers/{courierId:guid}", async (Guid courierId, CourierRequest request, CourierService service, CancellationToken ct) =>
            (await service.UpdateAsync(courierId, request, ct)).ToHttpResult());
    }

    private static void MapPayments(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/payment-settings", async (Guid establishmentId, PaymentService service, CancellationToken ct) =>
            (await service.GetSettingsAsync(establishmentId, ct)).ToHttpResult());

        group.MapPut($"{Establishment}/payment-settings", async (Guid establishmentId, PaymentSettingsRequest request, PaymentService service, CancellationToken ct) =>
            (await service.SaveSettingsAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPost("/orders/{orderId:guid}/refund", async (Guid orderId, PaymentService service, CancellationToken ct) =>
            (await service.RefundAsync(orderId, ct)).ToHttpResult());
    }

    private static void MapCustomers(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/customers", async (Guid establishmentId, PartyService service, CancellationToken ct) =>
            (await service.ListCustomersAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/customers", async (Guid establishmentId, CustomerRequest request, PartyService service, CancellationToken ct) =>
            (await service.CreateCustomerAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut("/customers/{customerId:guid}", async (Guid customerId, CustomerRequest request, PartyService service, CancellationToken ct) =>
            (await service.UpdateCustomerAsync(customerId, request, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/suppliers", async (Guid establishmentId, PartyService service, CancellationToken ct) =>
            (await service.ListSuppliersAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/suppliers", async (Guid establishmentId, SupplierRequest request, PartyService service, CancellationToken ct) =>
            (await service.CreateSupplierAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut("/suppliers/{supplierId:guid}", async (Guid supplierId, SupplierRequest request, PartyService service, CancellationToken ct) =>
            (await service.UpdateSupplierAsync(supplierId, request, ct)).ToHttpResult());

        group.MapGet("/lookups/cnpj/{cnpj}", async (string cnpj, PartyService service, CancellationToken ct) =>
            (await service.LookupCnpjAsync(cnpj, ct)).ToHttpResult());
    }

    private static void MapInventory(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/stock-items", async (Guid establishmentId, InventoryService service, CancellationToken ct) =>
            (await service.ListItemsAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/stock-items", async (Guid establishmentId, StockItemRequest request, InventoryService service, CancellationToken ct) =>
            (await service.CreateItemAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut("/stock-items/{itemId:guid}", async (Guid itemId, StockItemRequest request, InventoryService service, CancellationToken ct) =>
            (await service.UpdateItemAsync(itemId, request, ct)).ToHttpResult());

        group.MapPost("/stock-items/{itemId:guid}/adjustments", async (Guid itemId, StockAdjustmentRequest request, HttpContext http, InventoryService service, CancellationToken ct) =>
            (await service.AdjustAsync(itemId, request, http.Actor(), ct)).ToHttpResult());

        group.MapGet($"{Establishment}/stock-movements", async (Guid establishmentId, InventoryService service, CancellationToken ct) =>
            (await service.ListMovementsAsync(establishmentId, ct)).ToHttpResult());

        group.MapGet("/items/{itemId:guid}/recipe", async (Guid itemId, InventoryService service, CancellationToken ct) =>
            (await service.GetRecipeAsync(itemId, ct)).ToHttpResult());

        group.MapPut("/items/{itemId:guid}/recipe", async (Guid itemId, IReadOnlyList<RecipeLineRequest> request, InventoryService service, CancellationToken ct) =>
            (await service.SetRecipeAsync(itemId, request, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/purchases", async (Guid establishmentId, InventoryService service, CancellationToken ct) =>
            (await service.ListPurchasesAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/purchases", async (Guid establishmentId, CreatePurchaseRequest request, HttpContext http, InventoryService service, CancellationToken ct) =>
            (await service.CreatePurchaseAsync(establishmentId, request, http.Actor(), ct)).ToHttpResult());
    }

    private static void MapFinance(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/financial-entries", async (Guid establishmentId, FinancialStatus? status, FinanceService service, CancellationToken ct) =>
            (await service.ListAsync(establishmentId, status, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/financial-entries", async (Guid establishmentId, FinancialEntryRequest request, FinanceService service, CancellationToken ct) =>
            (await service.CreateAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPost("/financial-entries/{entryId:guid}/settle", async (Guid entryId, FinanceService service, CancellationToken ct) =>
            (await service.SettleAsync(entryId, ct)).ToHttpResult());

        group.MapPost("/financial-entries/{entryId:guid}/cancel", async (Guid entryId, FinanceService service, CancellationToken ct) =>
            (await service.CancelAsync(entryId, ct)).ToHttpResult());
    }

    private static void MapReports(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/dashboard", async (Guid establishmentId, int? days, FinanceService service, CancellationToken ct) =>
            (await service.GetDashboardAsync(establishmentId, days ?? 30, ct)).ToHttpResult());
    }

    private static void MapFiscal(RouteGroupBuilder group)
    {
        group.MapPost($"{Establishment}/nfe/preview", async (Guid establishmentId, NfeXmlRequest request, NfeImportService service, CancellationToken ct) =>
            (await service.PreviewAsync(establishmentId, request, ct)).ToHttpResult());
    }

    private static void MapFiscalFile(RouteGroupBuilder group, string route, FiscalFileKind kind) =>
        group.MapPost($"{Establishment}/fiscal/files/{route}", async (Guid establishmentId, FiscalFileRequest request, FiscalService service, CancellationToken ct) =>
            (await service.GenerateFileAsync(establishmentId, kind, request, ct)).ToHttpResult());

    private static void MapFiscalNfce(RouteGroupBuilder group)
    {
        // Direct emission at the SEFAZ with the store's A1 certificate (no ACBr).
        group.MapGet($"{Establishment}/fiscal/dfe", async (Guid establishmentId, DfeService service, CancellationToken ct) =>
            (await service.GetSettingsAsync(establishmentId, ct)).ToHttpResult());
        group.MapPut($"{Establishment}/fiscal/dfe", async (Guid establishmentId, DfeSettingsRequest request, DfeService service, CancellationToken ct) =>
            (await service.SaveSettingsAsync(establishmentId, request, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/fiscal/certificate", async (Guid establishmentId, UploadCertificateRequest request, DfeService service, CancellationToken ct) =>
            (await service.UploadCertificateAsync(establishmentId, request, ct)).ToHttpResult());
        group.MapDelete($"{Establishment}/fiscal/certificate", async (Guid establishmentId, DfeService service, CancellationToken ct) =>
            (await service.RemoveCertificateAsync(establishmentId, ct)).ToHttpResult());
        group.MapGet($"{Establishment}/fiscal/status", async (Guid establishmentId, int? model, DfeService service, CancellationToken ct) =>
            (await service.StatusAsync(establishmentId, model ?? 65, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/fiscal/orders/{{orderId:guid}}/nfe", async (Guid establishmentId, Guid orderId, NfeRecipientRequest request, DfeService service, CancellationToken ct) =>
            (await service.IssueAsync(establishmentId, orderId, ToNoRango.Domain.Fiscal.FiscalDocument.NfeModel, request, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/fiscal/documents/{{documentId:guid}}/cancel", async (Guid establishmentId, Guid documentId, CancelNoteRequest request, DfeService service, CancellationToken ct) =>
            (await service.CancelAsync(establishmentId, documentId, request, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/fiscal/contingency/send", async (Guid establishmentId, DfeService service, CancellationToken ct) =>
            (await service.SendContingencyAsync(establishmentId, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/fiscal/number-gaps", async (Guid establishmentId, NumberGapRequest request, DfeService service, CancellationToken ct) =>
            (await service.GiveUpNumbersAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/fiscal", async (Guid establishmentId, FiscalService service, CancellationToken ct) =>
            (await service.GetOverviewAsync(establishmentId, ct)).ToHttpResult());

        group.MapPut($"{Establishment}/fiscal/profile", async (Guid establishmentId, FiscalProfileRequest request, FiscalService service, CancellationToken ct) =>
            (await service.SaveProfileAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut($"{Establishment}/fiscal/products/{{menuItemId:guid}}", async (Guid establishmentId, Guid menuItemId, ProductFiscalRequest request, FiscalService service, CancellationToken ct) =>
            (await service.SaveProductAsync(establishmentId, menuItemId, request, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/orders/{{orderId:guid}}/nfce", async (Guid establishmentId, Guid orderId, FiscalService service, CancellationToken ct) =>
            (await service.IssueAsync(establishmentId, orderId, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/fiscal/documents/{{documentId:guid}}/xml", async (Guid establishmentId, Guid documentId, FiscalService service, CancellationToken ct) =>
            (await service.GetXmlAsync(establishmentId, documentId, ct)).ToHttpResult());
    }

    private static void MapWhatsApp(RouteGroupBuilder group)
    {
        var managers = group.MapGroup(string.Empty).RequireRoles(UserRole.Owner, UserRole.Manager);

        group.MapGet($"{Establishment}/whatsapp", async (Guid establishmentId, WhatsAppService service, CancellationToken ct) =>
            (await service.GetSettingsAsync(establishmentId, ct)).ToHttpResult());

        managers.MapPut($"{Establishment}/whatsapp", async (Guid establishmentId, WhatsAppSettingsRequest request, WhatsAppService service, CancellationToken ct) =>
            (await service.SaveSettingsAsync(establishmentId, request, ct)).ToHttpResult());

        managers.MapPost($"{Establishment}/whatsapp/connect", async (Guid establishmentId, ConnectWhatsAppRequest request, WhatsAppService service, CancellationToken ct) =>
            (await service.ConnectAsync(establishmentId, request.Phone, ct)).ToHttpResult());

        managers.MapPost($"{Establishment}/whatsapp/disconnect", async (Guid establishmentId, WhatsAppService service, CancellationToken ct) =>
            (await service.DisconnectAsync(establishmentId, ct)).ToHttpResult());

        managers.MapPut($"{Establishment}/whatsapp/automation", async (Guid establishmentId, SetAutomationRequest request, WhatsAppService service, CancellationToken ct) =>
            (await service.SetAutomationAsync(establishmentId, request.Enabled, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/whatsapp/connection", async (Guid establishmentId, WhatsAppService service, CancellationToken ct) =>
            (await service.GetConnectionAsync(establishmentId, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/conversations", async (Guid establishmentId, WhatsAppService service, CancellationToken ct) =>
            (await service.ListConversationsAsync(establishmentId, ct)).ToHttpResult());

        group.MapGet("/conversations/{conversationId:guid}/messages", async (Guid conversationId, WhatsAppService service, CancellationToken ct) =>
            (await service.ListMessagesAsync(conversationId, ct)).ToHttpResult());

        group.MapPost("/conversations/{conversationId:guid}/messages", async (Guid conversationId, StaffMessageRequest request, WhatsAppService service, CancellationToken ct) =>
            (await service.SendStaffMessageAsync(conversationId, request, ct)).ToHttpResult());

        group.MapPatch("/conversations/{conversationId:guid}/bot", async (Guid conversationId, SetBotRequest request, WhatsAppService service, CancellationToken ct) =>
            (await service.SetBotAsync(conversationId, request.Paused, ct)).ToHttpResult());
    }

    private static void MapZest(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/zest/knowledge", async (Guid establishmentId, ZestService service, CancellationToken ct) =>
            (await service.ListKnowledgeAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/zest/knowledge", async (Guid establishmentId, KnowledgeRequest request, ZestService service, CancellationToken ct) =>
            (await service.CreateKnowledgeAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapPut("/zest/knowledge/{entryId:guid}", async (Guid entryId, KnowledgeRequest request, ZestService service, CancellationToken ct) =>
            (await service.UpdateKnowledgeAsync(entryId, request, ct)).ToHttpResult());

        group.MapDelete("/zest/knowledge/{entryId:guid}", async (Guid entryId, ZestService service, CancellationToken ct) =>
            (await service.DeleteKnowledgeAsync(entryId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/zest/simulate", async (Guid establishmentId, ZestSimulateRequest request, ZestSalesService service, CancellationToken ct) =>
            (await service.SimulateAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/zest/stats", async (Guid establishmentId, ZestSalesService service, CancellationToken ct) =>
            (await service.GetStatsAsync(establishmentId, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/zest/connector", async (Guid establishmentId, ZestConnectorService service, CancellationToken ct) =>
            (await service.GetAsync(establishmentId, ct)).ToHttpResult());
        group.MapPost($"{Establishment}/zest/connector/key", async (Guid establishmentId, ZestConnectorService service, CancellationToken ct) =>
            (await service.CreateKeyAsync(establishmentId, ct)).ToHttpResult());
        group.MapDelete($"{Establishment}/zest/connector", async (Guid establishmentId, ZestConnectorService service, CancellationToken ct) =>
            (await service.DisconnectAsync(establishmentId, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/zest/gaps", async (Guid establishmentId, ZestService service, CancellationToken ct) =>
            (await service.ListGapsAsync(establishmentId, ct)).ToHttpResult());

        group.MapPost("/zest/gaps/{gapId:guid}/resolve", async (Guid gapId, ZestService service, CancellationToken ct) =>
            (await service.ResolveGapAsync(gapId, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/zest/ask", async (Guid establishmentId, ZestAskRequest request, ZestService service, CancellationToken ct) =>
            (await service.AskAsync(establishmentId, request, ct)).ToHttpResult());
    }
}
