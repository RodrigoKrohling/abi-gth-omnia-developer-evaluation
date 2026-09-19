using Microsoft.AspNetCore.Mvc;

namespace Ambev.DeveloperEvaluation.WebApi.Common;

/// <summary>
/// The pagination and ordering parameters accepted by every collection endpoint.
/// </summary>
/// <remarks>
/// The names and defaults come from <c>.doc/general-api.md</c>:
///
/// <code>
/// GET /sales?_page=2&amp;_size=20&amp;_order=saleDate desc, saleNumber asc
/// </code>
///
/// The leading underscore distinguishes these control parameters from the field
/// filters that share the same query string, so <c>?_size=10&amp;branchName=Downtown</c>
/// is unambiguous: <c>_size</c> pages the result, <c>branchName</c> filters it.
/// Binding them through <see cref="FromQueryAttribute"/> with an explicit
/// <c>Name</c> is required because an underscore prefix is not a legal C# member
/// name prefix by convention.
/// </remarks>
public class PaginationQuery
{
    /// <summary>The largest page size a caller may request.</summary>
    /// <remarks>
    /// Caps the cost of a single request. Without a ceiling, <c>?_size=1000000</c>
    /// would let any caller pull the whole table in one query.
    /// </remarks>
    public const int MaxPageSize = 100;

    /// <summary>The page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 10;

    private readonly int _page = 1;
    private readonly int _size = DefaultPageSize;

    /// <summary>
    /// Gets the 1-based page number to return. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// Values below 1 are clamped rather than rejected: a caller asking for page 0
    /// wants the first page, and failing the request would be pedantic.
    /// </remarks>
    [FromQuery(Name = "_page")]
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Gets the number of items per page. Defaults to 10, capped at 100.
    /// </summary>
    [FromQuery(Name = "_size")]
    public int Size
    {
        get => _size;
        init => _size = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>
    /// Gets the ordering clause, for example <c>"saleDate desc, saleNumber asc"</c>.
    /// </summary>
    /// <remarks>
    /// Null or empty means the endpoint's natural order. The direction may be
    /// omitted per field and defaults to ascending, so <c>"totalAmount desc, saleNumber"</c>
    /// is valid. Parsed by <c>QueryableExtensions.ApplyOrdering</c>.
    /// </remarks>
    [FromQuery(Name = "_order")]
    public string? Order { get; init; }
}
