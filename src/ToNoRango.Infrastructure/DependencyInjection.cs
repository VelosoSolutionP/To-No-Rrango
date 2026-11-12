using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ToNoRango.Application.Abstractions;
using ToNoRango.Infrastructure.Caching;
using ToNoRango.Infrastructure.Integrations;
using ToNoRango.Infrastructure.Messaging;
using ToNoRango.Infrastructure.Persistence;
using ToNoRango.Infrastructure.Persistence.Repositories;
using ToNoRango.Infrastructure.Security;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ToNoRangoDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICacheService, RedisCacheService>();
        services.AddScoped<IEstablishmentRepository, EstablishmentRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IStockStore, StockStore>();
        services.AddScoped<ICustomerStore, CustomerStore>();
        services.AddScoped<IReportQueries, ReportQueries>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IAppUrls, AppUrls>();
        services.AddMemoryCache();
        services.AddSingleton(configuration.GetSection(ToNoRango.Application.Identity.VelosoIdOptions.SectionName).Get<ToNoRango.Application.Identity.VelosoIdOptions>() ?? new ToNoRango.Application.Identity.VelosoIdOptions());
        services.AddHttpClient<ToNoRango.Application.Identity.IVelosoIdGateway, Security.VelosoIdGateway>(client => client.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IOrderEvents, RedisOrderEvents>();
        services.AddSingleton<ToNoRango.Application.Delivery.IDeliveryTrackingStore, RedisDeliveryTrackingStore>();
        services.AddHttpClient<ToNoRango.Application.Delivery.IGeocoder, NominatimGeocoder>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(6);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToNoRango/1.0 (+https://to-no-rango.velososolution.com.br)");
        });

        services.AddHttpClient<IPaymentGateway, MercadoPagoGateway>(client =>
        {
            client.BaseAddress = new Uri(configuration["MercadoPago:BaseUrl"] ?? "https://api.mercadopago.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddHttpClient<ILookupClient, PublicLookupClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToNoRango/1.0");
        });

        var evolutionBaseUrl = configuration["Evolution:BaseUrl"];

        services.AddHttpClient<ISpeechToText, WhisperSpeechToText>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddSingleton<ITextToSpeech, PiperTextToSpeech>();
        services.AddSingleton<ToNoRango.Application.Fiscal.IDfeEmitter, Fiscal.ZeusDfeEmitter>();
        services.AddHttpClient<IWhatsAppSender, EvolutionWhatsAppSender>(client =>
        {
            if (!string.IsNullOrWhiteSpace(evolutionBaseUrl))
            {
                client.BaseAddress = new Uri($"{evolutionBaseUrl.TrimEnd('/')}/");
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<ToNoRango.Application.Fiscal.IFiscalGateway, AcbrFiscalGateway>();

        services.AddHostedService<OutboxWorker>();

        var gtin = configuration.GetSection(Integrations.GtinOptions.SectionName).Get<Integrations.GtinOptions>() ?? new Integrations.GtinOptions();
        services.AddSingleton(gtin);
        services.AddSingleton<Integrations.GtinGate>();
        services.AddHttpClient<ToNoRango.Application.Catalog.IGtinCatalog, Integrations.RscGtinCatalog>(client =>
        {
            client.BaseAddress = new Uri(gtin.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VelosoSolution-Client/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddHostedService<Integrations.GtinImportWorker>();

        var fiscalApi = configuration.GetSection(Integrations.FiscalApiOptions.SectionName).Get<Integrations.FiscalApiOptions>() ?? new Integrations.FiscalApiOptions();
        services.AddSingleton(fiscalApi);
        services.AddHttpClient<ToNoRango.Application.Fiscal.IWebhookSender, Integrations.FiscalWebhookSender>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZEST-Fiscal-Webhook/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => Integrations.FiscalWebhookSender.CreateHandler(fiscalApi));
        services.AddHostedService<Integrations.FiscalWebhookWorker>();

        return services;
    }
}
