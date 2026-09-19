namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// Stores and retrieves serializable read models by key.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately typed over arbitrary values rather than over the Sale aggregate.
/// What gets cached is a <c>SaleResult</c> - a flat, inert read model - never the
/// aggregate itself. Reviving a <see cref="Ambev.DeveloperEvaluation.Domain.Entities"/>
/// aggregate from JSON would mean writing past its private setters and handing back
/// an object that looks like a sale but was never built through its own rules. A
/// cached read model cannot be mistaken for something safe to modify.
/// </para>
/// <para>
/// Implementations must degrade gracefully. A cache is an optimisation, so an
/// unreachable Redis has to mean a slower request, not a failed one. Every method
/// therefore swallows its own transport failures rather than propagating them.
/// </para>
/// </remarks>
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
