using Ambev.DeveloperEvaluation.Domain.Entities;

namespace Ambev.DeveloperEvaluation.Domain.Repositories;

/// <summary>
/// Persistence operations for the <see cref="Sale"/> aggregate.
/// </summary>
public interface ISaleRepository
{
    /// <summary>
    /// Persists a new sale together with its items.
    /// </summary>
    /// <param name="sale">The sale to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored sale.</returns>
    Task<Sale> CreateAsync(Sale sale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a sale and its items by identifier.
    /// </summary>
    /// <param name="id">The sale's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, or <c>null</c> when no sale has that identifier.</returns
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a sale by its business-facing number.
    /// </summary>
    /// <param name="saleNumber">The sale number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, or <c>null</c> when no sale has that number.</returns>
    Task<Sale?> GetBySaleNumberAsync(string saleNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a page of sales, filtered and ordered as requested.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="size">The number of sales per page.</param>
    /// <param name="order">
    /// An ordering clause such as <c>"saleDate desc, saleNumber asc"</c>, or
    /// <c>null</c> for the default order.
    /// </param>
    /// <param name="filters">
    /// Field filters from the query string, honouring the wildcard and
    /// <c>_min</c>/<c>_max</c> conventions in <c>.doc/general-api.md</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sales on the requested page, and the total number that matched.</returns>
    Task<(IReadOnlyList<Sale> Sales, int TotalCount)> ListAsync(
        int page,
        int size,
        string? order,
        IReadOnlyDictionary<string, string?> filters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves changes made to a sale that was loaded from this repository.
    /// </summary>
    /// <param name="sale">The modified sale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated sale.</returns>
    Task<Sale> UpdateAsync(Sale sale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes a sale and its items.
    /// </summary>
    /// <param name="id">The identifier of the sale to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a sale was deleted; <c>false</c> when none had that identifier.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
