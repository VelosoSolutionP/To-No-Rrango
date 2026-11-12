using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Fiscal;

namespace ToNoRango.Infrastructure.Integrations;

public sealed class FiscalApiOptions
{
    public const string SectionName = "FiscalApi";

    /// <summary>Only for local tests: lets webhooks point to http and to private addresses.</summary>
    public bool AllowPrivateWebhooks { get; init; }

    public int WorkerIntervalSeconds { get; init; } = 10;
}

/// <summary>
/// Posts webhook events. The address a store types could point inside our own network (database,
/// the API itself), so the connection is checked after DNS: only public addresses are dialed,
/// and redirects are not followed.
/// </summary>
public sealed class FiscalWebhookSender(HttpClient http, FiscalApiOptions options, ILogger<FiscalWebhookSender> logger) : IWebhookSender
{
    public const string ClientName = "fiscal-webhooks";

    public string? Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || url.Length > 500)
        {
            return "Endereço inválido. Use o endereço completo, começando com https://.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowPrivateWebhooks && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "O webhook precisa usar https.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Não coloque usuário e senha no endereço; confira a assinatura do envio.";
        }

        if (!options.AllowPrivateWebhooks && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host, out var literal) && !IsPublic(literal))))
        {
            return "O webhook precisa ser um endereço público da internet.";
        }

        return null;
    }

    public async Task<WebhookSendResult> SendAsync(string url, string eventName, Guid deliveryId, string body, string signature, long timestamp, CancellationToken cancellationToken)
    {
        if (Validate(url) is { } problem)
        {
            return new WebhookSendResult(false, null, problem);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Zest-Event", eventName);
        request.Headers.Add("X-Zest-Delivery", deliveryId.ToString());
        request.Headers.Add("X-Zest-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Zest-Signature", $"sha256={signature}");

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var code = (int)response.StatusCode;

            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, code, null)
                : new WebhookSendResult(false, code, code is >= 300 and < 400 ? $"Resposta {code}: redirecionamento não é seguido." : $"Resposta {code}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, "Sem resposta em 10 segundos.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogInformation("Fiscal webhook to {Host} failed: {Message}", new Uri(url).Host, exception.Message);
            return new WebhookSendResult(false, null, exception.InnerException is BlockedAddressException blocked ? blocked.Message : "Não foi possível conectar ao endereço.");
        }
    }

    /// <summary>Handler that resolves the host itself and refuses private, loopback and link-local targets.</summary>
    public static SocketsHttpHandler CreateHandler(FiscalApiOptions options) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(address => options.AllowPrivateWebhooks || IsPublic(address)).ToList();

            if (allowed.Count == 0)
            {
                throw new BlockedAddressException("O endereço do webhook aponta para uma rede interna e foi bloqueado.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(allowed.ToArray(), context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return !(bytes[0] == 10
                || bytes[0] == 0
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                || (bytes[0] == 198 && bytes[1] is 18 or 19)
                || bytes[0] >= 224);
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);
    }

    private sealed class BlockedAddressException(string message) : IOException(message);
}

/// <summary>Turns note changes into webhook events and sends what is due, every few seconds.</summary>
public sealed class FiscalWebhookWorker(IServiceScopeFactory scopes, FiscalApiOptions options, ILogger<FiscalWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, options.WorkerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // No tenant in this scope: the worker sees every store, as the outbox does.
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<FiscalWebhookService>();
                await service.ScanAsync(stoppingToken);
                await service.DeliverDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The fiscal webhook round failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
