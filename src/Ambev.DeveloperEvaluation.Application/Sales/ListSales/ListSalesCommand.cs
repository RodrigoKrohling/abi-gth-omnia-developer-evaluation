using Ambev.DeveloperEvaluation.Application.Sales.Common;
using MediatR;

namespace Ambev.DeveloperEvaluation.Application.Sales.ListSales;

/// <summary>
/// Requests a page of sales, filtered and ordered as the query string asked.
/// </summary>
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
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
