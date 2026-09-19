namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// Settings for the read-model cache, bound from the <c>Cache</c> section of
/// configuration.
/// </summary>
/// <remarks>
/// <see cref="Enabled"/> defaults to <c>false</c> so the API runs against PostgreSQL
/// alone unless caching is deliberately switched on. Failing closed is right for an
/// optional dependency: a reviewer who starts only the API and the database should
/// not have to stand up Redis as well.
/// </remarks>
public class CacheSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Cache";

    /// <summary>
    /// Gets or sets a value indicating whether sale reads are cached in Redis.
    /// </summary>
    /// <remarks>
    /// When <c>false</c>, a <see cref="NullCacheService"/> is registered instead, so
    /// no call site has to know whether a cache exists.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the Redis connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prefix applied to every key this application writes.
    /// </summary>
    /// <remarks>
    /// Namespaces the entries so a Redis instance shared with another service cannot
    /// collide with them.
    /// </remarks>
    public string InstanceName { get; set; } = "ambev-developer-evaluation:";
}
