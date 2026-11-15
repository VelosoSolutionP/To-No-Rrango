using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Media;

namespace ToNoRango.Infrastructure.Integrations;

/// <summary>Pixabay: real photos with free commercial use. Only "photo" results, so no cartoon ever comes back.</summary>
public sealed class PixabayPhotoSearch(HttpClient http, IConfiguration configuration, IMemoryCache cache, ILogger<PixabayPhotoSearch> logger) : IPhotoSearch
{
    private const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Pixabay refuses bursts even under 100 per minute: one call at a time, a short gap between them.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(800);
    private static DateTimeOffset lastCall = DateTimeOffset.MinValue;
    private readonly string? key = configuration["Pixabay:Key"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(key);

    public async Task<IReadOnlyList<PhotoOption>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        // Pixabay asks clients to cache searches for 24 h.
        var cacheKey = "pixabay:" + query.ToLowerInvariant();

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<PhotoOption>? cached) && cached is not null)
        {
            return cached;
        }

        var url = $"api/?key={Uri.EscapeDataString(key!)}&q={Uri.EscapeDataString(query)}&lang=pt&image_type=photo&safesearch=true&per_page=12&min_width=500";

        for (var attempt = 1; ; attempt++)
        {
            string body;
            HttpStatusCode status;
            var resetSeconds = 5;

            await Gate.WaitAsync(cancellationToken);
            try
            {
                var wait = lastCall + Gap - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

                using var response = await http.GetAsync(url, cancellationToken);
                lastCall = DateTimeOffset.UtcNow;
                status = response.StatusCode;

                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && int.TryParse(reset.FirstOrDefault(), out var seconds))
                {
                    resetSeconds = seconds;
                }
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Pixabay search failed");
                throw new PhotoSearchUnavailableException("O banco de fotos não respondeu.", exception);
            }
            finally
            {
                Gate.Release();
            }

            if (status == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                // Waits what the bank says it needs (at most 30 s), so a whole catalog finishes in one go.
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(resetSeconds + 1, 2, 30)), cancellationToken);
                continue;
            }

            if ((int)status >= 400)
            {
                logger.LogWarning("Pixabay answered {Status}", (int)status);
                throw new PhotoSearchUnavailableException($"O banco de fotos respondeu {(int)status}.");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                IReadOnlyList<PhotoOption> photos = document.RootElement.GetProperty("hits").EnumerateArray()
                    .Select(hit => new PhotoOption(
                        hit.GetProperty("id").ToString(),
                        hit.GetProperty("webformatURL").GetString()!.Replace("_640", "_340"),
                        hit.TryGetProperty("largeImageURL", out var large) ? large.GetString()! : hit.GetProperty("webformatURL").GetString()!,
                        hit.GetProperty("tags").GetString() ?? string.Empty,
                        hit.GetProperty("user").GetString() ?? string.Empty,
                        hit.GetProperty("pageURL").GetString() ?? string.Empty))
                    .ToList();

                // Image links from a search live 24 h; keep the list a bit less than that.
                cache.Set(cacheKey, photos, TimeSpan.FromHours(20));
                return photos;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Pixabay answer could not be read");
                throw new PhotoSearchUnavailableException("Resposta do banco de fotos ilegível.", exception);
            }
        }
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        // Only the bank's own addresses: the panel never makes the server fetch an arbitrary URL.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("pixabay.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("cdn.pixabay.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length is > 0 and <= MaxBytes ? bytes : null;
    }
}
