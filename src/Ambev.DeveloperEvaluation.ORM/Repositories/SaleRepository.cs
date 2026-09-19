using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.ORM.Common;
using Microsoft.EntityFrameworkCore;

namespace Ambev.DeveloperEvaluation.ORM.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="ISaleRepository"/>.
/// </summary>
/// <remarks>
/// Every read that returns a sale for modification includes its items, because a
/// partially loaded aggregate cannot enforce its own rules: a sale without its
/// items would accept a product it already holds, and would recalculate its total
/// from an empty list.
/// </remarks>
public class SaleRepository : ISaleRepository
{
    private readonly DefaultContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SaleRepository"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    public SaleRepository(DefaultContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Sale> CreateAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        await _context.Sales.AddAsync(sale, cancellationToken);

        // Items are added with the sale in one call because EF tracks the whole
        // object graph reachable from the root.
        await _context.SaveChangesAsync(cancellationToken);

        return sale;
    }

    /// <inheritdoc />
    public async Task<Sale?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Sale?> GetBySaleNumberAsync(string saleNumber, CancellationToken cancellationToken = default)
    {
        return await _context.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.SaleNumber == saleNumber, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Sale> Sales, int TotalCount)> ListAsync(
        int page,
        int size,
        string? order,
        IReadOnlyDictionary<string, string?> filters,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking because these sales are read to be serialized, never
        // modified. It skips building the change tracker's snapshots, which is the
        // single biggest cost of a large read.
        var query = _context.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .AsQueryable();

        // Filters are applied before the count so the total reflects what matched,
        // not the size of the table.
        query = query.ApplyFilters(filters);

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.ApplyOrdering(order, out var wasOrdered);

        // A stable tiebreaker is essential for paging. Without a total order, two
        // rows that compare equal on the requested column can swap places between
        // queries, so one row appears on both page 1 and page 2 while another is
        // never returned at all. SaleNumber is unique, which makes the order total.
        query = wasOrdered
            ? ((IOrderedQueryable<Sale>)query).ThenBy(s => s.SaleNumber)
            // No ordering was requested, so newest first is the useful default for a
            // sales list, with the same tiebreaker appended.
            : query.OrderByDescending(s => s.SaleDate).ThenBy(s => s.SaleNumber);

        var sales = await query
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (sales, totalCount);
    }

    /// <inheritdoc />
    public async Task<Sale> UpdateAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        // The sale was loaded through this same context and is already tracked, so
        // EF works out the changed columns and the added or removed items by
        // comparing against its snapshot. Calling Update() here would instead mark
        // every property modified and rewrite untouched columns.
        await _context.SaveChangesAsync(cancellationToken);

        return sale;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sale = await GetByIdAsync(id, cancellationToken);

        if (sale is null)
            return false;

        // The items go with it through the cascade configured in
        // SaleItemConfiguration.
        _context.Sales.Remove(sale);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
