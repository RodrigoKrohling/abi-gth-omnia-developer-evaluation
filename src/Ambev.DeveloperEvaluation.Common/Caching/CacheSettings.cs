namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// Settings for the read-model cache, bound from the <c>Cache</c> section of
/// configuration.
/// </summary>
public class CacheSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Cache";

    /// <summary>
    /// Gets or sets a value indicating whether sale reads are cached in Redis.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the Redis connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prefix applied to every key this application writes.
    /// </summary>
    public string InstanceName { get; set; } = "ambev-developer-evaluation:";
}
