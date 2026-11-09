using System.Net.Http.Json;
using ToNoRango.Application.Identity;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure.Security;

public sealed class VelosoIdGateway(HttpClient http, VelosoIdOptions options, IClock clock) : IVelosoIdGateway
{
    public async Task<VelosoAuthorization?> RedeemAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("redeem", new { code, redirectUri, codeVerifier }, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<VelosoAuthorization>(cancellationToken) : null;
    }

    public async Task<VelosoSessionState> CheckAsync(string sessionHandle, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("session/check", new { sessionHandle }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VelosoSessionState>(cancellationToken))!;
    }

    public async Task EndAsync(string sessionHandle, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("session/end", new { sessionHandle }, cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.PortalApiUrl.TrimEnd('/')}/api/v1/identity/apps/{path}")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("X-Veloso-Client", options.ClientId);
        request.Headers.Add("X-Veloso-Credential", VelosoIdProtocol.DailyCredential(options.RootSecret, options.ClientId, clock.UtcNow));

        return http.SendAsync(request, cancellationToken);
    }
}
