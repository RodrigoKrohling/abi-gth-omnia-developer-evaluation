namespace Ambev.DeveloperEvaluation.Domain.Services;

/// <summary>
/// Decides what discount a sale item earns, and whether its quantity is allowed at
/// all.
/// </summary>
/// <remarks>
/// Expressed as an interface so that the pricing rules are a named, separately
/// testable concept rather than an <c>if</c> chain buried inside
/// <see cref="Entities.SaleItem"/>. Discount schemes are the part of a sales domain
/// most likely to change - seasonal campaigns, customer tiers, per-branch
/// promotions - and this is the seam along which such a change would arrive.
///
/// Implementations must be stateless and safe to share between threads.
/// </remarks>
public interface IDiscountPolicy
{
    /// <summary>
    /// Returns the discount rate earned by the given quantity of one product.
    /// </summary>
    /// <param name="quantity">The number of identical items on the sale item.</param>
    /// <returns>
    /// The rate as a fraction, so <c>0.10m</c> means ten percent and <c>0m</c> means
    /// no discount.
    /// </returns>
    /// <exception cref="Exceptions.DomainException">
    /// Thrown when the quantity is not sellable.
    /// </exception>
    decimal GetDiscountRate(int quantity);
}
