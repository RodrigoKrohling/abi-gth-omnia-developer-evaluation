using Ambev.DeveloperEvaluation.Domain.Exceptions;

namespace Ambev.DeveloperEvaluation.Domain.Services;

/// <summary>
/// The quantity-based discount rules defined by the project brief.
/// </summary>
/// <remarks>
/// <para>The brief states the rules twice. In prose:</para>
/// <list type="bullet">
///   <item>Purchases above 4 identical items have a 10% discount</item>
///   <item>Purchases between 10 and 20 identical items have a 20% discount</item>
///   <item>It's not possible to sell above 20 identical items</item>
///   <item>Purchases below 4 items cannot have a discount</item>
/// </list>
///
/// <para>and then restated as explicit tiers:</para>
/// <list type="bullet">
///   <item>4+ items: 10% discount</item>
///   <item>10-20 items: 20% discount</item>
///   <item>Maximum limit: 20 items per product</item>
///   <item>No discounts allowed for quantities below 4 items</item>
/// </list>
///
/// <para>Which produces this table:</para>
/// <list type="table">
///   <listheader><term>Quantity</term><description>Outcome</description></listheader>
///   <item><term>1 to 3</term><description>no discount</description></item>
///   <item><term>4 to 9</term><description>10%</description></item>
///   <item><term>10 to 20</term><description>20%</description></item>
///   <item><term>21 or more</term><description>rejected</description></item>
/// </list>
///
/// <para>
/// Two points in the brief are ambiguous, and both are resolved here deliberately.
/// </para>
///
/// <para>
/// First, the prose says "above 4", which would start the tier at 5, while the
/// restated list says "4+", which starts it at 4. They cannot both hold. The
/// restated list wins, because it is the more precise of the two and because the
/// fourth prose rule - "below 4 items cannot have a discount" - only makes sense as
/// the complement of a tier that begins exactly at 4. So a quantity of 4 earns 10%.
/// </para>
///
/// <para>
/// Second, "identical items" is a property of the product across the whole sale,
/// not of one line in isolation. Enforcing the cap per line would let a caller sell
/// 40 units by splitting them across two lines of 20, and would let them dodge the
/// tiers entirely by splitting 10 units into three small lines that each earn
/// nothing. <see cref="Entities.Sale"/> closes that by refusing to hold the same
/// product on more than one item, so one line is always the full quantity of that
/// product and this policy can work on a single number.
/// </para>
/// </remarks>
public sealed class QuantityTierDiscountPolicy : IDiscountPolicy
{
    /// <summary>The smallest quantity that earns any discount.</summary>
    public const int MinimumQuantityForDiscount = 4;

    /// <summary>The smallest quantity that earns the higher tier.</summary>
    public const int MinimumQuantityForHigherTier = 10;

    /// <summary>The largest quantity of one product that may be sold.</summary>
    public const int MaximumQuantity = 20;

    /// <summary>The rate earned from <see cref="MinimumQuantityForDiscount"/> items.</summary>
    public const decimal StandardDiscountRate = 0.10m;

    /// <summary>The rate earned from <see cref="MinimumQuantityForHigherTier"/> items.</summary>
    public const decimal HigherDiscountRate = 0.20m;

    /// <summary>No discount.</summary>
    public const decimal NoDiscountRate = 0m;

    /// <summary>
    /// A shared instance, safe to reuse because the policy holds no state.
    /// </summary>
    /// <remarks>
    /// Used as the default when <see cref="Entities.Sale"/> is asked to add an item
    /// without being handed a policy, which keeps unit tests and simple call sites
    /// free of ceremony. Call sites that need different rules pass their own
    /// implementation instead.
    /// </remarks>
    public static readonly QuantityTierDiscountPolicy Instance = new();

    /// <inheritdoc />
    /// <exception cref="DomainException">
    /// Thrown when <paramref name="quantity"/> is below one or above
    /// <see cref="MaximumQuantity"/>.
    /// </exception>
    public decimal GetDiscountRate(int quantity)
    {
        // A sale item for zero or a negative number of units is not a smaller sale,
        // it is a meaningless one. Rejected before the tiers are considered.
        if (quantity < 1)
            throw new DomainException(
                $"The quantity must be at least 1, but was {quantity}.");

        // "It's not possible to sell above 20 identical items." This is a hard
        // limit, not a tier that caps out: the operation is refused rather than
        // clamped, because silently selling 20 when 25 were asked for would be worse
        // than an error.
        if (quantity > MaximumQuantity)
            throw new DomainException(
                $"Cannot sell more than {MaximumQuantity} identical items. Requested {quantity}.");

        // Tiers are checked from the highest down, so each branch only has to state
        // its own lower bound. Written the other way around, the 10% test would also
        // match quantities of 10 or more and shadow the 20% tier.
        if (quantity >= MinimumQuantityForHigherTier)
            return HigherDiscountRate;

        if (quantity >= MinimumQuantityForDiscount)
            return StandardDiscountRate;

        // "Purchases below 4 items cannot have a discount."
        return NoDiscountRate;
    }
}
