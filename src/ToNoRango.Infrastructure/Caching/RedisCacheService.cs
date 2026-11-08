using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Applied when a query does not state its own expiry.</summary>
    public int DefaultExpirationSeconds { get; set; } = 60;

    /// <summary>Prefix on every key, so one Redis instance can host more than one environment.</summary>
    public string KeyPrefix { get; set; } = "tonorango";

    /// <summary>
    /// Turning this off makes every read a miss without changing a line of calling code, which is
    /// what you want when diagnosing whether a wrong number on a dashboard is stale cache.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Redis cache with tag based invalidation.
/// <para>
/// The tag index is the part that earns its complexity. A cost dashboard is assembled from a dozen
/// cached queries, and when someone corrects a model price every one of them is wrong. Without
/// tags, each write path has to know and enumerate the keys it invalidates, which drifts out of
/// date the moment a new query is added. With tags, the write says "catalog changed" and every
/// query that read pricing is dropped, including ones written later.
/// </para>
/// A cache failure is logged and swallowed: Redis being down should make the product slow, never
/// broken.
/// </summary>
public sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CacheOptions _options = options.Value;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return default;
        }

        try
        {
            var value = await Database().StringGetAsync(Prefixed(key));

            return value.IsNullOrEmpty
                ? default
                : JsonSerializer.Deserialize<T>(value.ToString(), SerializerOptions);
        }
        catch (Exception exception) when (exception is RedisException or JsonException or TimeoutException)
        {
            logger.LogWarning(exception, "Falha ao ler cache {Key}. Seguindo sem cache.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var ttl = expiration ?? TimeSpan.FromSeconds(_options.DefaultExpirationSeconds);
        var prefixed = Prefixed(key);

        try
        {
            var database = Database();
            var payload = JsonSerializer.Serialize(value, SerializerOptions);

            var batch = database.CreateBatch();
            var tasks = new List<Task> { batch.StringSetAsync(prefixed, payload, ttl) };

            foreach (var tag in tags ?? [])
            {
                var tagKey = TagKey(tag);
                tasks.Add(batch.SetAddAsync(tagKey, prefixed));

                // The tag index outlives its entries slightly, then expires on its own. Without a
                // TTL these sets would grow without bound and never be reclaimed.
                tasks.Add(batch.KeyExpireAsync(tagKey, ttl.Add(TimeSpan.FromMinutes(5)), ExpireWhen.HasNoExpiry));
            }

            batch.Execute();
            await Task.WhenAll(tasks);
        }
        catch (Exception exception) when (exception is RedisException or JsonException or TimeoutException)
        {
            logger.LogWarning(exception, "Falha ao gravar cache {Key}. Seguindo sem cache.", key);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);

        if (value is not null)
        {
            await SetAsync(key, value, expiration, tags, cancellationToken);
        }

        return value;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await Database().KeyDeleteAsync(Prefixed(key));
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.LogWarning(exception, "Falha ao remover cache {Key}.", key);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = Database();
            var tagKey = TagKey(tag);
            var members = await database.SetMembersAsync(tagKey);

            if (members.Length > 0)
            {
                await database.KeyDeleteAsync([.. members.Select(m => (RedisKey)m.ToString())]);
            }

            await database.KeyDeleteAsync(tagKey);

            logger.LogDebug("Cache invalidado por tag {Tag}: {Count} chave(s).", tag, members.Length);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.LogWarning(exception, "Falha ao invalidar cache por tag {Tag}.", tag);
        }
    }

    /// <summary>
    /// Prefix removal uses SCAN, never KEYS: KEYS blocks the whole Redis instance while it walks
    /// the keyspace, which on a shared cache means stalling every other request in the product.
    /// </summary>
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"{Prefixed(prefix)}*";
            var database = Database();

            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);

                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                await foreach (var key in server.KeysAsync(database.Database, pattern, pageSize: 500)
                    .WithCancellation(cancellationToken))
                {
                    await database.KeyDeleteAsync(key);
                }
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.LogWarning(exception, "Falha ao invalidar cache por prefixo {Prefix}.", prefix);
        }
    }

    private IDatabase Database() => redis.GetDatabase();

    private string Prefixed(string key) => $"{_options.KeyPrefix}:{key}";

    private string TagKey(string tag) => $"{_options.KeyPrefix}:tag:{tag}";
}
