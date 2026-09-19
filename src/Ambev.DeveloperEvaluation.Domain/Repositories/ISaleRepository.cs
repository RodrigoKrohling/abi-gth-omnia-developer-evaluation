using Ambev.DeveloperEvaluation.Domain.Entities;

namespace Ambev.DeveloperEvaluation.Domain.Repositories;

/// <summary>
/// Persistence operations for the <see cref="Sale"/> aggregate.
/// </summary>
/// <remarks>
/// Declared in the domain and implemented in the ORM project, so the dependency
/// points inwards: the application layer talks to this interface and never
/// references Entity Framework. Substituting the storage technology, or a test
/// double, means writing another implementation and changing nothing else.
///
/// The interface is deliberately expressed in terms of the aggregate root only.
/// There is no <c>ISaleItemRepository</c>, because an item has no life outside its
/// sale; loading or saving one directly would step around the invariants
/// <see cref="Sale"/> exists to enforce.
/// </remarks>
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
    /// <returns>The sale, or <c>null</c> when no sale has that identifier.</returns>
    /// <remarks>
    /// Returns the aggregate whole, items included. A partially loaded aggregate
    /// could not enforce its own rules: a sale without its items would happily
    /// accept a product it already holds.
    /// </remarks>
    Task<Sale?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a sale by its business-facing number.
    /// </summary>
    /// <param name="saleNumber">The sale number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sale, or <c>null</c> when no sale has that number.</returns>
    /// <remarks>
    /// Used to enforce that sale numbers are unique before inserting, so the caller
    /// gets a clear conflict rather than a database constraint violation.
    /// </remarks>
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
    /// <remarks>
    /// The total count is returned alongside the page because the API's pagination
    /// envelope needs both, and computing it here keeps it in the same round trip.
    /// </remarks>
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
    /// <remarks>
    /// A hard delete, distinct from <see cref="Sale.Cancel"/>. Cancelling is the
    /// business operation that voids a sale while keeping the record; deleting
    /// exists to satisfy the CRUD contract and erases the row entirely.
    /// </remarks>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
