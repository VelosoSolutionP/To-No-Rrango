using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ToNoRango.Application.Delivery;

namespace ToNoRango.Infrastructure.Integrations;

public sealed class RedisDeliveryTrackingStore(IConnectionMultiplexer redis) : IDeliveryTrackingStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private IDatabase Database => redis.GetDatabase();

    private static RedisKey TokenKey(string token) => $"tonorango:tracking:token:{token}";

    private static RedisKey OrderKey(string code) => $"tonorango:tracking:order:{code}";

    private static RedisKey PositionKey(string code) => $"tonorango:tracking:position:{code}";

    public async Task SaveAsync(TrackingSession session)
    {
        var value = JsonSerializer.Serialize(session);
        await Database.StringSetAsync(TokenKey(session.Token), value, Lifetime);
        await Database.StringSetAsync(OrderKey(session.OrderCode), value, Lifetime);
    }

    public async Task<TrackingSession?> FindByTokenAsync(string token) => Read<TrackingSession>(await Database.StringGetAsync(TokenKey(token)));

    public async Task<TrackingSession?> FindByOrderAsync(string orderCode) => Read<TrackingSession>(await Database.StringGetAsync(OrderKey(orderCode)));

    public Task SavePositionAsync(string orderCode, CourierPosition position) =>
        Database.StringSetAsync(PositionKey(orderCode), JsonSerializer.Serialize(position), Lifetime);

    public async Task<CourierPosition?> FindPositionAsync(string orderCode) => Read<CourierPosition>(await Database.StringGetAsync(PositionKey(orderCode)));

    public Task RemoveAsync(TrackingSession session) =>
        Database.KeyDeleteAsync([TokenKey(session.Token), OrderKey(session.OrderCode), PositionKey(session.OrderCode)]);

    private static T? Read<T>(RedisValue value) where T : class =>
        value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value.ToString());
}

/// <summary>Free OpenStreetMap search, called once per delivery. It receives the address only, never the customer.</summary>
public sealed class NominatimGeocoder(HttpClient http, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    public async Task<GeoPoint?> FindAsync(string street, string? number, string? city, string? state, CancellationToken cancellationToken)
    {
        var line = string.IsNullOrWhiteSpace(number) ? street : $"{number} {street}";
        var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=br"
            + $"&street={Uri.EscapeDataString(line)}"
            + (string.IsNullOrWhiteSpace(city) ? string.Empty : $"&city={Uri.EscapeDataString(city)}")
            + (string.IsNullOrWhiteSpace(state) ? string.Empty : $"&state={Uri.EscapeDataString(state)}");

        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var first = document.RootElement[0];
            return new GeoPoint(
                double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // Sem o ponto de entrega o mapa ainda mostra o entregador; só o tempo estimado some.
            logger.LogWarning(exception, "Could not locate the delivery address");
            return null;
        }
    }
}
