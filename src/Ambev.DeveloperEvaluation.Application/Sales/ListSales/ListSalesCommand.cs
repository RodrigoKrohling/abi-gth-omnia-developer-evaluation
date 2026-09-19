using Ambev.DeveloperEvaluation.Application.Sales.Common;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.ListSales;

/// <summary>
/// Requests a page of sales, filtered and ordered as the query string asked.
/// </summary>
/// <remarks>
/// <see cref="Filters"/> carries the query string's remaining key/value pairs rather
/// than a fixed set of typed properties, because <c>.doc/general-api.md</c> defines
/// filtering generically: any field may be filtered, string fields accept
/// <c>*</c> wildcards, and numeric or date fields accept <c>_min</c> and
/// <c>_max</c> prefixes. Enumerating those as properties would mean a new command
/// property, validator rule and handler branch for every filterable field, and would
/// still not cover the range prefixes.
///
/// The dictionary is inert data, not something the caller can use to reach arbitrary
/// state: <c>QueryableExtensions</c> resolves keys only against public properties of
/// the entity, and silently drops anything it cannot resolve or convert.
/// </remarks>
public class ListSalesCommand : IRequest<ListSalesResult>
{
    /// <summary>Gets or sets the 1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Gets or sets the number of sales per page.</summary>
    public int Size { get; set; } = 10;

    /// <summary>
    /// Gets or sets the ordering clause, such as <c>"saleDate desc, saleNumber asc"</c>.
    /// </summary>
    public string? Order { get; set; }

    /// <summary>Gets or sets the field filters taken from the query string.</summary>
    public Dictionary<string, string?> Filters { get; set; } = [];
}

/// <summary>
/// A page of sales and the total number that matched the filters.
/// </summary>
/// <remarks>
/// The total is the count of everything matching the filters, not the size of the
/// page, because the API's pagination envelope reports <c>totalItems</c> and
/// <c>totalPages</c>.
/// </remarks>
public class ListSalesResult
{
    /// <summary>Gets or sets the sales on the requested page.</summary>
    public List<SaleResult> Sales { get; set; } = [];

    /// <summary>Gets or sets how many sales matched the filters in total.</summary>
    public int TotalCount { get; set; }

    /// <summary>Gets or sets the page that was returned.</summary>
    public int CurrentPage { get; set; }

    /// <summary>Gets or sets the page size that was applied.</summary>
    public int PageSize { get; set; }

    /// <summary>Gets how many pages the full result spans.</summary>
    /// <remarks>
    /// Derived rather than stored, so it can never disagree with the count and the
    /// page size it is computed from.
    /// </remarks>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
