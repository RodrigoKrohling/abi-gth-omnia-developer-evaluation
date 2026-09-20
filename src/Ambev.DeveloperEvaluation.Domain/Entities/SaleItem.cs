using Ambev.DeveloperEvaluation.Domain.Common;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;

namespace Ambev.DeveloperEvaluation.Domain.Entities;

/// <summary>
/// One product line on a sale: what was sold, how many, at what price, and what
/// the quantity earned in discount.
/// </summary>
public class SaleItem : BaseEntity
{
    /// <summary>
    /// Gets the identifier of the sale this item belongs to.
    /// </summary>
    public Guid SaleId { get; private set; }

    /// <summary>
    /// Gets the product sold, as an external identity with its denormalized title.
    /// </summary>
    public ProductReference Product { get; private set; }

    /// <summary>
    /// Gets how many units of the product were sold.
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// Gets the price of a single unit at the time of the sale.
    /// </summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// Gets the discount rate that was applied, as a fraction.
    /// </summary>
    public decimal DiscountRate { get; private set; }

    /// <summary>
    /// Gets the money taken off this line by the discount.
    /// </summary>
    public decimal Discount { get; private set; }

    /// <summary>
    /// Gets the amount payable for this line, after the discount.
    /// </summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this item has been cancelled.
    /// </summary>
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Gets the instant at which the item was cancelled, if it was.
    /// </summary>
    public DateTime? CancelledAt { get; private set; }

    /// <summary>
    /// Parameterless constructor reserved for Entity Framework Core materialization.
    /// </summary>
    private SaleItem()
    {
        Product = null!;
    }

    /// <summary>
    /// Creates a sale item, applying the discount policy to the quantity.
    /// </summary>
    /// <param name="product">The product being sold.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="unitPrice">The price of one unit.</param>
    /// <param name="discountPolicy">The rules that decide the discount.</param>
    /// <returns>The new item.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the unit price is negative, or when the policy rejects the
    /// quantity.
    /// </exception>
    internal static SaleItem Create(
        ProductReference product,
        int quantity,
        decimal unitPrice,
        IDiscountPolicy discountPolicy)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(discountPolicy);

        if (unitPrice < 0)
            throw new DomainException($"The unit price cannot be negative, but was {unitPrice}.");

        var item = new SaleItem
        {
            Id = Guid.NewGuid(),
            Product = product
        };

        item.SetQuantityAndPrice(quantity, unitPrice, discountPolicy);

        return item;
    }

    /// <summary>
    /// Changes the quantity or unit price and recalculates the money.
    /// </summary>
    /// <param name="quantity">The new quantity.</param>
    /// <param name="unitPrice">The new unit price.</param>
    /// <param name="discountPolicy">The rules that decide the discount.</param>
    /// <exception cref="DomainException">
    /// Thrown when the item is cancelled, the price is negative, or the policy
    /// rejects the quantity.
    /// </exception>
    internal void SetQuantityAndPrice(int quantity, decimal unitPrice, IDiscountPolicy discountPolicy)
    {
        ArgumentNullException.ThrowIfNull(discountPolicy);

        if (IsCancelled)
            throw new DomainException("A cancelled sale item cannot be changed.");

        if (unitPrice < 0)
            throw new DomainException($"The unit price cannot be negative, but was {unitPrice}.");

        // Throws for a quantity outside 1..20, so an invalid quantity never reaches
        // the fields below and the item is left untouched.
        var rate = discountPolicy.GetDiscountRate(quantity);

        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountRate = rate;

        var grossAmount = quantity * unitPrice;

        // Round the discount first, then subtract, so that Discount and TotalAmount
        // always add back up to the gross amount exactly. Rounding the total
        // independently could leave the three figures off by a cent.
        Discount = Math.Round(grossAmount * rate, 2, MidpointRounding.AwayFromZero);
        TotalAmount = Math.Round(grossAmount, 2, MidpointRounding.AwayFromZero) - Discount;
    }

    /// <summary>
    /// Refreshes the denormalized product title, keeping the same product id.
    /// </summary>
    /// <param name="title">The title as the caller most recently knows it.</param>
    /// <exception cref="DomainException">Thrown when the item is cancelled.</exception>
    /// <remarks>
    /// Used when an update supplies a newer title for a product the sale already
    /// holds. A fresh <see cref="ProductReference"/> is constructed rather than the
    /// caller's instance being stored, because an owned entity is keyed by its owner
    /// and reusing one that another item already owns would make the persistence
    /// layer try to move it between parents.
    ///
    /// Only the description is refreshed; the identifier never changes, since a
    /// different product is a different item.
    /// </remarks>
    internal void SetProductTitle(string title)
    {
        if (IsCancelled)
            throw new DomainException("A cancelled sale item cannot be changed.");

        Product = new ProductReference(Product.Id, title);
    }

    /// <summary>
    /// Cancels this item.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the item is already cancelled.</exception>
    /// <remarks>
    /// The quantity, price and discount are left exactly as they were. The record of
    /// what was ordered is the point of keeping a cancelled row at all; only
    /// <see cref="Sale.TotalAmount"/> stops counting it.
    /// </remarks>
    internal void Cancel()
    {
        if (IsCancelled)
            throw new DomainException("The sale item is already cancelled.");

        IsCancelled = true;
        CancelledAt = DateTime.UtcNow;
    }
}
