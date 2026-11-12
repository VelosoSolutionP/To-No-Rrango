using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Catalog;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure.Integrations;

public sealed class GtinOptions
{
    public const string SectionName = "Gtin";

    public string BaseUrl { get; set; } = "https://gtin.rscsistemas.com.br";

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>The plan allows 2 calls per minute, token and image included.</summary>
    public int IntervalSeconds { get; set; } = 31;

    /// <summary>SHA-256 of the provider's "PRODUTO SEM IMAGEM" picture, which is never saved as a product photo.</summary>
    public string[] PlaceholderImageSha256 { get; set; } = ["d31675f70fe3a18ec1558a4c8af3a797f0287a3c3bdb1cb6c59847a564a29bf2"];
}

/// <summary>Shared by every client instance: one call at a time, spaced by the plan's interval, one token for the hour.</summary>
public sealed class GtinGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public DateTimeOffset LastCall { get; set; } = DateTimeOffset.MinValue;

    public string? Token { get; set; }

    public DateTimeOffset TokenExpiresAt { get; set; }

    /// <summary>Answers already paid for; a barcode never changes its product.</summary>
    public ConcurrentDictionary<string, GtinLookupResult> Cache { get; } = new();
}

public sealed class RscGtinCatalog(HttpClient http, GtinOptions options, GtinGate gate, ILogger<RscGtinCatalog> logger) : IGtinCatalog
{
    private const int CacheLimit = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password);

    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, options.IntervalSeconds));

    public async Task<GtinLookupResult> LookupAsync(string code, bool withImage, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return GtinLookupResult.Down("A consulta por código de barras não está configurada.");
        }

        if (gate.Cache.TryGetValue(code, out var cached) && (!withImage || cached.Product?.Image is not null || cached.Status == GtinLookupStatus.NotFound))
        {
            return cached;
        }

        await gate.Lock.WaitAsync(cancellationToken);

        try
        {
            var result = await FetchAsync(code, withImage, cancellationToken);

            if (result.Status != GtinLookupStatus.Unavailable)
            {
                if (gate.Cache.Count >= CacheLimit)
                {
                    gate.Cache.Clear();
                }

                gate.Cache[code] = result;
            }

            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "GTIN lookup failed for {Code}", code);
            return GtinLookupResult.Down(exception.Message.StartsWith("GTIN token refused", StringComparison.Ordinal)
                ? "A base de produtos recusou o usuário e a senha configurados no servidor."
                : "A base de produtos não respondeu. Tente de novo em instantes.");
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    private async Task<GtinLookupResult> FetchAsync(string code, bool withImage, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v3/gtin/{code}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return GtinLookupResult.Missing();
        }

        if (!response.IsSuccessStatusCode)
        {
            return GtinLookupResult.Down(ProviderMessage(response.StatusCode, body));
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var name = Text(root, "nome_acento") ?? Text(root, "nome");

        if (name is null)
        {
            return GtinLookupResult.Missing(Text(root, "mensagem"));
        }

        byte[]? image = null;

        if (withImage && Text(root, "link_foto") is not null)
        {
            using var picture = await SendAsync(HttpMethod.Get, $"/api/v3/gtin/{code}/image", cancellationToken);

            if (picture.IsSuccessStatusCode && (picture.Content.Headers.ContentType?.MediaType ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                image = await picture.Content.ReadAsByteArrayAsync(cancellationToken);

                if (options.PlaceholderImageSha256.Contains(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(image)), StringComparer.OrdinalIgnoreCase))
                {
                    image = null;
                }
            }
        }

        var ncm = Digits(Text(root, "ncm"));
        var cest = Digits(Text(root, "cest"));

        return new GtinLookupResult(
            GtinLookupStatus.Found,
            new GtinProduct(code, Clip(name, 120)!, Clip(Text(root, "marca"), 120), ncm.Length == 8 ? ncm : null, cest.Length == 7 ? cest : null, image),
            null);
    }

    /// <summary>Sends with the hour token, renewing it once on 401 and waiting a turn once when the plan's limit is hit.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await EnsureTokenAsync(cancellationToken);
            await WaitTurnAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gate.Token);
            var response = await http.SendAsync(request, cancellationToken);

            if (attempt == 0 && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                gate.Token = null;
                continue;
            }

            if (attempt == 0 && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (body.Contains("excedid", StringComparison.OrdinalIgnoreCase))
                {
                    response.Dispose();
                    gate.LastCall = DateTimeOffset.UtcNow;
                    continue;
                }

                // Read already: hand back a fresh copy so the caller can still show the provider's message.
                var copy = new HttpResponseMessage(response.StatusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                response.Dispose();
                return copy;
            }

            return response;
        }
    }

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (gate.Token is not null && gate.TokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return;
        }

        await WaitTurnAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/oauth/token") { Content = new ByteArrayContent([]) };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GTIN token refused: {(int)response.StatusCode} {ProviderMessage(response.StatusCode, body)}");
        }

        using var json = JsonDocument.Parse(body);
        gate.Token = Text(json.RootElement, "access_token") ?? Text(json.RootElement, "token")
            ?? throw new HttpRequestException("GTIN token missing in the answer");
        var seconds = json.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var value) ? value : 3600;
        gate.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        var wait = gate.LastCall + Interval - DateTimeOffset.UtcNow;

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        gate.LastCall = DateTimeOffset.UtcNow;
    }

    private static string ProviderMessage(HttpStatusCode status, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var message = Text(json.RootElement, "mensagem") ?? Text(json.RootElement, "erro");

            if (message is not null)
            {
                return $"A base de produtos recusou: {message}";
            }
        }
        catch (JsonException)
        {
        }

        return $"A base de produtos respondeu com erro {(int)status}.";
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static string? Clip(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Runs bulk barcode imports one code at a time, each in its own scope, at the pace the provider allows.</summary>
public sealed class GtinImportWorker(GtinImportQueue queue, IServiceScopeFactory scopes, IClock clock, ILogger<GtinImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            job.Begin();

            foreach (var code in job.Codes)
            {
                if (job.CancelRequested || stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<GtinService>();
                    job.Add(await service.ImportOneAsync(job, code, stoppingToken));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "GTIN import failed for {Code}", code);
                    job.Add(new GtinImportLine(code, "failed", null, "Erro inesperado ao importar este código."));
                }
            }

            job.Finish(clock.UtcNow);
        }
    }
}
