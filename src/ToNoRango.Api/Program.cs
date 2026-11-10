using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using ToNoRango.Api.Endpoints;
using ToNoRango.Application;
using ToNoRango.Application.Abstractions;
using ToNoRango.Infrastructure;
using ToNoRango.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key is required and must have at least 32 bytes.");
}

builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => FixedWindowByIp(context, 10));
    options.AddPolicy("public", context => FixedWindowByIp(context, 60));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var healthChecks = builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgres",
        tags: ["ready", "db"]);

if (builder.Configuration.GetValue("Cache:Enabled", true))
{
    healthChecks.AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis",
        tags: ["ready", "cache"]);
}

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ToNoRango API";
    document.Info.Version = "v1";
    document.Info.Description = """
        API do cardápio digital To-No-Rango: cardápio, pedidos, pagamentos, estoque e módulos.

        O acesso operacional é controlado pelo contrato único do Portal Veloso Solution.
        """;
    return Task.CompletedTask;
}));

var app = builder.Build();

// Fails at startup, not at the first credential saved by a customer.
app.Services.GetRequiredService<ISecretProtector>();

if (app.Configuration.GetValue("Network:TrustForwardedHeaders", false))
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ToNoRangoDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    await Results.Problem(
            title: "unexpected_error",
            detail: "Ocorreu um erro inesperado ao processar a requisição.",
            statusCode: StatusCodes.Status500InternalServerError)
        .ExecuteAsync(context);
}));

app.UseCors();

app.UseAuthentication();
app.Use(VelosoIdEndpoints.GuardAsync);
app.Use(Access.ResolveTenantAsync);
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
}).AllowAnonymous();

if (app.Environment.IsDevelopment() || app.Configuration["OpenApi:Exposed"] == "true")
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("ToNoRango API").WithTheme(ScalarTheme.Purple))
        .AllowAnonymous();
}

app.MapPublicEndpoints();
app.MapAdminEndpoints();
app.MapAuthEndpoints();
app.MapVelosoIdEndpoints();
app.MapPlatformEndpoints();
app.MapModuleEndpoints();
app.MapOperationEndpoints();
app.MapWebhookEndpoints();

app.Run();

static RateLimitPartition<string> FixedWindowByIp(HttpContext context, int permits) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });
