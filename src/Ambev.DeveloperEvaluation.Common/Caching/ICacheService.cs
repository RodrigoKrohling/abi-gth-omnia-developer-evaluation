namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// Stores and retrieves serializable read models by key.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Reads a cached value.
    /// </summary>
    /// <typeparam name="T">The type the value was stored as.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value, or <c>null</c> on a miss or any cache failure.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stores a value.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="timeToLive">How long the entry may be served for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Removes a cached value, if present.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
