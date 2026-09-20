using Ambev.DeveloperEvaluation.Domain.Exceptions;

namespace Ambev.DeveloperEvaluation.Domain.Services;

/// <summary>
/// The quantity-based discount rules defined by the project brief.
/// </summary>
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
    public static readonly QuantityTierDiscountPolicy Instance = new();

    /// <inheritdoc />
    /// <exception cref="DomainException">
    /// Thrown when <paramref name="quantity"/> is below one or above
    /// <see cref="MaximumQuantity"/>.
    /// </exception>
    public decimal GetDiscountRate(int quantity)
    {
        if (quantity < 1)
            throw new DomainException(
                $"The quantity must be at least 1, but was {quantity}.");

        if (quantity > MaximumQuantity)
            throw new DomainException(
                $"Cannot sell more than {MaximumQuantity} identical items. Requested {quantity}.");

        if (quantity >= MinimumQuantityForHigherTier)
            return HigherDiscountRate;

        if (quantity >= MinimumQuantityForDiscount)
            return StandardDiscountRate;

        return NoDiscountRate;
    }
}
