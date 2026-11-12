using Microsoft.Extensions.DependencyInjection;
using ToNoRango.Application.Catalog;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Delivery;
using ToNoRango.Application.Establishments;
using ToNoRango.Application.Finance;
using ToNoRango.Application.Fiscal;
using ToNoRango.Application.Identity;
using ToNoRango.Application.Inventory;
using ToNoRango.Application.Media;
using ToNoRango.Application.Messaging;
using ToNoRango.Application.Orders;
using ToNoRango.Application.Parties;
using ToNoRango.Application.Payments;
using ToNoRango.Application.Zest;

namespace ToNoRango.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<EstablishmentService>();
        services.AddScoped<MenuService>();
        services.AddScoped<OrderService>();
        services.AddScoped<OrderLifecycle>();
        services.AddScoped<OrderNotifier>();
        services.AddSingleton<ContractAccessService>();
        services.AddScoped<ContractService>();
        services.AddScoped<Abstractions.IModuleAccess>(provider => provider.GetRequiredService<ContractService>());
        services.AddScoped<AuthService>();
        services.AddScoped<VelosoIdService>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<CourierService>();
        services.AddScoped<DeliveryTrackingService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PartyService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<FinanceService>();
        services.AddScoped<ToNoRango.Application.Pdv.PdvService>();
        services.AddScoped<ToNoRango.Application.Fiscal.DfeService>();
        services.AddScoped<ToNoRango.Application.Fiscal.ReceivedNotesService>();
        services.AddScoped<ToNoRango.Application.Fiscal.AccountantPackageService>();
        services.AddScoped<ToNoRango.Application.Fiscal.FiscalReviewService>();
        services.AddScoped<ToNoRango.Application.Zest.ZestConnectorService>();
        services.AddScoped<ToNoRango.Application.Feedback.RatingService>();
        services.AddScoped<NfeImportService>();
        services.AddScoped<FiscalService>();
        services.AddScoped<ZestService>();
        services.AddScoped<ZestSalesService>();
        services.AddScoped<MediaService>();
        services.AddScoped<WhatsAppService>();

        return services;
    }
}
