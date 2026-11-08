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
        services.AddSingleton<IOrderEvents, RedisOrderEvents>();

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

        services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(client => client.Timeout = TimeSpan.FromSeconds(25));

        var zestBaseUrl = configuration["Zest:BaseUrl"];

        services.AddHttpClient<ToNoRango.Application.Zest.IHotelClient, ZestHotelClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(zestBaseUrl))
            {
                client.BaseAddress = new Uri($"{zestBaseUrl.TrimEnd('/')}/");
            }

            client.Timeout = TimeSpan.FromSeconds(10);
        });

        var evolutionBaseUrl = configuration["Evolution:BaseUrl"];

        services.AddHttpClient<IWhatsAppSender, EvolutionWhatsAppSender>(client =>
        {
            if (!string.IsNullOrWhiteSpace(evolutionBaseUrl))
            {
                client.BaseAddress = new Uri($"{evolutionBaseUrl.TrimEnd('/')}/");
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHostedService<OutboxWorker>();

        return services;
    }
}
