namespace Ambev.DeveloperEvaluation.Application.Sales.Common;

/// <summary>
/// The full state of a sale, as returned by the create, read and update use cases.
/// </summary
public class SaleResult
{
    /// <summary>Gets the sale's unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets the business-facing sale number.</summary>
    public string SaleNumber { get; set; } = string.Empty;

    /// <summary>Gets the date on which the sale was made, in UTC.</summary>
    public DateTime SaleDate { get; set; }

    /// <summary>Gets the customer who made the purchase.</summary>
    public ExternalIdentityResult Customer { get; set; } = new();

    /// <summary>Gets the branch where the sale was made.</summary>
    public ExternalIdentityResult Branch { get; set; } = new();

    /// <summary>Gets the items on the sale, including cancelled ones.</summary>
    /// <remarks>
    /// Cancelled items are included rather than filtered out, so a caller can see
    /// the whole history of the sale. Each carries its own
    /// <see cref="SaleItemResult.IsCancelled"/> flag, and only the active ones
    /// contribute to <see cref="TotalAmount"/>.
    /// </remarks>
    public List<SaleItemResult> Items { get; set; } = [];

    /// <summary>Gets the total payable for the sale, after discounts.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Gets a value indicating whether the sale has been cancelled.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Gets when the sale record was created, in UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets when the sale was last changed, in UTC, if it has been.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Gets when the sale was cancelled, in UTC, if it was.</summary>
    public DateTime? CancelledAt { get; set; }
}

/// <summary>
/// A reference to an entity owned by another domain: its identifier plus the
/// description captured at the time of sale.
/// </summary>
public class ExternalIdentityResult
{
    /// <summary>Gets the identifier in the owning domain.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets the denormalized description as it was at the time of sale.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// One product line on a sale.
/// </summary>
public class SaleItemResult
{
    /// <summary>Gets the item's unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets the product that was sold.</summary>
    public ExternalIdentityResult Product { get; set; } = new();

    /// <summary>Gets how many units were sold.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets the price of one unit at the time of sale.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets the discount rate applied, as a fraction such as 0.20.</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>Gets the money taken off this line by the discount.</summary>
    public decimal Discount { get; set; }

    /// <summary>Gets the amount payable for this line, after the discount.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Gets a value indicating whether this item has been cancelled.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Gets when the item was cancelled, in UTC, if it was.</summary>
    public DateTime? CancelledAt { get; set; }
}
