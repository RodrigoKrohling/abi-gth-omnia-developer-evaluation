namespace Ambev.DeveloperEvaluation.Application.Sales.Common;

/// <summary>
/// Builds the cache keys used by the sale use cases.
/// </summary>
/// <remarks>
/// Centralised so that the handler which writes an entry and the handlers which
/// invalidate it cannot disagree about the key. A typo in a literal key string would
/// not fail any test - it would simply mean the entry is never invalidated, and stale
/// sales are served until they expire.
///
/// The <c>sale:</c> prefix namespaces these entries, so a Redis instance shared with
/// another service cannot collide with them.
/// </remarks>
public static class SaleCacheKeys
{
    /// <summary>
    /// How long a cached sale may be served before it is re-read from the database.
    /// </summary>
    /// <remarks>
    /// Short on purpose. Every write path invalidates explicitly, so the time-to-live
    /// is not the primary correctness mechanism; it is the backstop for the one case
    /// invalidation cannot cover - a delete or update that succeeded while Redis was
    /// briefly unreachable. Five minutes bounds how long such an entry can be wrong.
    /// </remarks>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Builds the key holding a single sale's read model.
    /// </summary>
    /// <param name="saleId">The sale's identifier.</param>
    /// <returns>The cache key.</returns>
    public static string ForSale(Guid saleId) => $"sale:{saleId}";
}
