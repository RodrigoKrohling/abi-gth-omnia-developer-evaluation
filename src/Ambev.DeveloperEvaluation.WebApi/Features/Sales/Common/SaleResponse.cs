namespace Ambev.DeveloperEvaluation.WebApi.Features.Sales.Common;

/// <summary>
/// The sale as it appears on the wire.
/// </summary>
/// <remarks>
/// Deliberately a separate type from
/// <see cref="Ambev.DeveloperEvaluation.Application.Sales.Common.SaleResult"/>, even
/// though the two currently carry the same fields.
///
/// This one is the published API contract: renaming a field here breaks every
/// client. The application result is an internal hand-off between two layers and
/// should be free to change with the use cases. Collapsing them would tie the public
/// contract to an internal refactoring, so that adding a field a handler happens to
/// need would silently widen what the API promises.
///
/// The two are kept in step by AutoMapper rather than by hand, so the duplication
/// costs a declaration and no logic. This mirrors the template's own split between
/// <c>CreateUserResult</c> and <c>CreateUserResponse</c>.
/// </remarks>
public class SaleResponse
{
    /// <summary>Gets or sets the sale's unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the business-facing sale number.</summary>
    public string SaleNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the date on which the sale was made, in UTC.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets or sets the customer who made the purchase.</summary>
    public ExternalIdentityResponse Customer { get; set; } = new();

    /// <summary>Gets or sets the branch where the sale was made.</summary>
    public ExternalIdentityResponse Branch { get; set; } = new();

    /// <summary>Gets or sets the items on the sale, cancelled ones included.</summary>
    public List<SaleItemResponse> Items { get; set; } = [];

    /// <summary>Gets or sets the total payable for the sale, after discounts.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Gets or sets a value indicating whether the sale has been cancelled.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Gets or sets when the sale record was created, in UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets when the sale was last changed, in UTC, if it has been.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Gets or sets when the sale was cancelled, in UTC, if it was.</summary>
    public DateTime? CancelledAt { get; set; }
}

/// <summary>
/// A reference to an entity owned by another domain: its identifier plus the
/// description captured at the time of sale.
/// </summary>
/// <remarks>
/// This is the External Identities pattern as it appears to a client. The
/// description is a snapshot, not a live lookup, so a customer renamed tomorrow does
/// not change what this sale reports.
/// </remarks>
public class ExternalIdentityResponse
{
    /// <summary>Gets or sets the identifier in the owning domain.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the denormalized description as it was at the time of sale.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// One product line on a sale.
/// </summary>
public class SaleItemResponse
{
    /// <summary>Gets or sets the item's unique identifier.</summary>
    /// <remarks>
    /// Pass this to <c>PATCH /api/sales/{id}/items/{itemId}/cancel</c>. It survives an
    /// update to the sale, so it is safe to store.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the product that was sold.</summary>
    public ExternalIdentityResponse Product { get; set; } = new();

    /// <summary>Gets or sets how many units were sold.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of one unit at the time of sale.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the discount rate applied, as a fraction such as 0.20.</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>Gets or sets the money taken off this line by the discount.</summary>
    public decimal Discount { get; set; }

    /// <summary>Gets or sets the amount payable for this line, after the discount.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Gets or sets a value indicating whether this item has been cancelled.</summary>
    /// <remarks>A cancelled item is still listed, but contributes nothing to the sale total.</remarks>
    public bool IsCancelled { get; set; }

    /// <summary>Gets or sets when the item was cancelled, in UTC, if it was.</summary>
    public DateTime? CancelledAt { get; set; }
}
