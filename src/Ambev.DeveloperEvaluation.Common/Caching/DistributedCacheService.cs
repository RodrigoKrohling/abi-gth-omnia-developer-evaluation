using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// An <see cref="ICacheService"/> backed by <see cref="IDistributedCache"/>, which in
/// this application is Redis.
/// </summary>
/// <remarks>
/// Written against the <see cref="IDistributedCache"/> abstraction rather than
/// against StackExchange.Redis directly, so the backing store is chosen in
/// composition. Swapping Redis for an in-memory cache during development, or for any
/// other distributed cache later, changes one registration and nothing here.
///
/// Every operation is wrapped in a try/catch that logs and continues. That is the
/// whole point of a cache being optional: if Redis is down, reads fall through to
/// the database and the API keeps working, slower. Letting a cache failure surface
/// as a request failure would make an optimisation into a liability.
/// </remarks>
public class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheService> _logger;

    /// <summary>
    /// Serializer options for cached values.
    /// </summary>
    /// <remarks>
    /// Camel case so a cached entry is byte-identical to what the API would have
    /// serialized anyway, which makes an entry readable with <c>redis-cli</c> during
    /// debugging.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheService"/> class.
    /// </summary>
    /// <param name="cache">The distributed cache to store entries in.</param>
    /// <param name="logger">Logger used to record cache failures.</param>
    public DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var cached = await _cache.GetStringAsync(key, cancellationToken);

            if (string.IsNullOrEmpty(cached))
                return null;

            return JsonDeserialize<T>(cached);
        }
        catch (Exception exception)
        {
            // Reported as a miss. The caller then reads from the database, which is
            // exactly what should happen when the cache is unavailable.
            _logger.LogWarning(exception, "Cache read failed for key {CacheKey}; falling back to the source.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var payload = JsonSerializer.Serialize(value, JsonOptions);

            await _cache.SetStringAsync(
                key,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive },
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Failing to populate the cache costs nothing but a future miss.
            _logger.LogWarning(exception, "Cache write failed for key {CacheKey}.", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            // This one is worth a louder log than the others. A failed invalidation
            // means a stale entry can be served until its time-to-live expires, which
            // is the only way this cache can return something wrong.
            _logger.LogWarning(
                exception,
                "Cache invalidation failed for key {CacheKey}. A stale entry may be served until it expires.",
                key);
        }
    }

    /// <summary>
    /// Deserializes a cached payload, treating malformed content as a miss.
    /// </summary>
    private T? JsonDeserialize<T>(string payload) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException exception)
        {
            // An entry written by an older version of the model no longer parses.
            // Treating it as a miss lets the cache heal itself on the next write,
            // rather than failing every read until the key expires.
            _logger.LogWarning(exception, "Discarding an unreadable cache entry.");
            return null;
        }
    }
}
