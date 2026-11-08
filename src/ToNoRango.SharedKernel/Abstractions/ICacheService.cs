namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>
/// Distributed cache facade over Redis. Tag based invalidation exists so a single write
/// can drop every dashboard query that depended on the data it changed, instead of
/// each handler guessing which keys to remove.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default);

    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiration = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>Well known cache tags, so invalidation is not spelled out as loose strings.</summary>
public static class CacheTags
{
    public const string Catalog = "catalog";

    public static string ForTenant(Guid tenantId) => $"tenant:{tenantId:N}";

    public static string ForEstablishment(Guid establishmentId) => $"establishment:{establishmentId:N}";
}
