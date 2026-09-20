using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// An <see cref="ICacheService"/> backed by <see cref="IDistributedCache"/>, which in
/// this application is Redis.
/// </summary>
public class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheService> _logger;

    /// <summary>
    /// Serializer options for cached values.
    /// </summary>
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
            _logger.LogWarning(exception, "Discarding an unreadable cache entry.");
            return null;
        }
    }
}
