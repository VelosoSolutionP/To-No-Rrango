using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Identity;

namespace ToNoRango.Api.Endpoints;

internal static class VelosoIdEndpoints
{
    public const string SessionClaim = "vid";

    public static IEndpointRouteBuilder MapVelosoIdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/veloso-id").WithTags("Autenticação");

        group.MapGet("/options", (VelosoIdService service) => Results.Ok(service.Describe())).AllowAnonymous();

        group.MapPost("/start", (VelosoStartRequest request, VelosoIdService service) => service.Start(request).ToHttpResult())
            .RequireRateLimiting("login")
            .AllowAnonymous();

        group.MapPost("/complete", async (VelosoCompleteRequest request, VelosoIdService service, CancellationToken ct) =>
                (await service.CompleteAsync(request, ct)).ToHttpResult())
            .RequireRateLimiting("login")
            .AllowAnonymous();

        group.MapPost("/logout", async (HttpContext http, VelosoIdService service, CancellationToken ct) =>
                (await service.LogoutAsync(http.User.FindFirst(SessionClaim)?.Value, ct)).ToHttpResult())
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// A session opened through the Veloso ID stays subject to the Portal: a block or a revocation
    /// reaches who is already inside, within the interval the Portal asks for. If the Portal is
    /// unreachable a session confirmed recently carries on, so an outage does not log anybody out.
    /// </summary>
    public static async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var claim = context.User.FindFirst(SessionClaim)?.Value;

        if (string.IsNullOrEmpty(claim))
        {
            await next(context);
            return;
        }

        var services = context.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();
        var key = $"veloso-id:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(claim)))}";
        var now = DateTimeOffset.UtcNow;
        var entry = cache.Get<GuardEntry>(key);

        if (entry is null || entry.NextCheckAt <= now)
        {
            try
            {
                var handle = services.GetRequiredService<ISecretProtector>().Unprotect(claim);
                var state = await services.GetRequiredService<IVelosoIdGateway>().CheckAsync(handle, context.RequestAborted);
                var configured = services.GetRequiredService<VelosoIdOptions>().CheckSeconds;
                var seconds = state.CheckAfterSeconds > 0 ? Math.Min(state.CheckAfterSeconds, configured) : configured;

                entry = new GuardEntry(state.Active, state.Message, now.AddSeconds(state.Active ? seconds : Math.Min(seconds, 30)), state.Active ? now : entry?.LastConfirmedAt ?? now);
                cache.Set(key, entry, TimeSpan.FromHours(1));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                if (entry is null || now - entry.LastConfirmedAt > OutageTolerance)
                {
                    await Results.Problem(title: "veloso_id.unavailable", detail: "Não foi possível validar a sessão agora. Tente novamente.", statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
                    return;
                }
            }
        }

        if (entry is { Active: false })
        {
            await Results.Problem(title: "veloso_id.session_inactive", detail: entry.Message ?? "Sua sessão foi encerrada. Entre novamente.", statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static readonly TimeSpan OutageTolerance = TimeSpan.FromMinutes(15);

    private sealed record GuardEntry(bool Active, string? Message, DateTimeOffset NextCheckAt, DateTimeOffset LastConfirmedAt);
}
