using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Identity;
using ToNoRango.Infrastructure.Persistence;

namespace ToNoRango.Api.Endpoints;

internal static class Access
{
    public const string TenantClaim = "tenant_id";
    private const string PlatformFlag = "tonorango.platform";

    public static bool HasPlatformKey(HttpContext context)
    {
        var expected = context.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(context.Request.Headers["X-Admin-Key"].ToString()));
    }

    /// <summary>
    /// The tenant comes from the signed token. X-Tenant-Id is honored only together with the platform
    /// key, so a client can never choose another tenant by changing a header.
    /// </summary>
    public static async Task ResolveTenantAsync(HttpContext context, RequestDelegate next)
    {
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

        if (context.User.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(context.User.FindFirstValue(TenantClaim), out var claimedTenant))
            {
                tenantContext.SetTenant(claimedTenant);
            }
        }
        else if (HasPlatformKey(context))
        {
            context.Items[PlatformFlag] = true;

            if (Guid.TryParse(context.Request.Headers["X-Tenant-Id"], out var headerTenant))
            {
                tenantContext.SetTenant(headerTenant);
            }
        }

        await next(context);
    }

    public static bool IsPlatform(this HttpContext context) => context.Items.ContainsKey(PlatformFlag);

    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public static string Actor(this HttpContext context) =>
        context.User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "plataforma";

    public static async ValueTask<object?> RequireTenantAccess(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (http.User.Identity?.IsAuthenticated != true && !http.IsPlatform())
        {
            return Results.Unauthorized();
        }

        if (http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId is null)
        {
            return Results.Problem(
                title: "tenant.required",
                detail: "Informe o tenant no header X-Tenant-Id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A deactivated employee must lose access immediately, not when the token expires.
        if (http.User.UserId() is { } userId
            && !await http.RequestServices.GetRequiredService<IRepository<User>>().AnyAsync(user => user.Id == userId && user.IsActive, http.RequestAborted))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    public static async ValueTask<object?> RequirePlatformKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        HasPlatformKey(context.HttpContext) ? await next(context) : Results.Unauthorized();

    public static RouteGroupBuilder RequireRoles(this RouteGroupBuilder group, params UserRole[] roles)
    {
        var allowed = roles.Select(role => role.ToString()).ToHashSet(StringComparer.Ordinal);

        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;

            return http.IsPlatform() || allowed.Contains(http.User.FindFirstValue("role") ?? string.Empty)
                ? await next(context)
                : Results.Problem(
                    title: "access.forbidden",
                    detail: "Seu perfil não tem acesso a esta função.",
                    statusCode: StatusCodes.Status403Forbidden);
        });

        return group;
    }

    public static RouteGroupBuilder RequireModule(this RouteGroupBuilder group, string module)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;

            if (http.IsPlatform())
            {
                return await next(context);
            }

            var tenantId = http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId!.Value;
            var modules = http.RequestServices.GetRequiredService<IModuleAccess>();

            return await modules.IsAllowedAsync(tenantId, module, http.RequestAborted)
                ? await next(context)
                : Results.Problem(
                    title: "contract.module_unavailable",
                    detail: "Este módulo não está liberado no seu contrato.",
                    statusCode: StatusCodes.Status403Forbidden);
        });

        return group;
    }
}

public sealed class JwtTokenIssuer(IConfiguration configuration) : ITokenIssuer
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(10);

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid tenantId, string name, string email, string role)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"],
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Email] = email,
                [JwtRegisteredClaimNames.Name] = name,
                ["role"] = role,
                [Access.TenantClaim] = tenantId.ToString()
            }
        });

        return (token, expiresAt);
    }
}
