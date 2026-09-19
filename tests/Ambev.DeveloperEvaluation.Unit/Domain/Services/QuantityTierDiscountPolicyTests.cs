using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Services;
using FluentAssertions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Domain.Services;

/// <summary>
/// Contains unit tests for <see cref="QuantityTierDiscountPolicy"/>.
/// </summary>
/// <remarks>
/// These tests are the executable statement of the four business rules in the
/// project brief. The cases are chosen at the tier boundaries rather than at
/// comfortable middle values, because every plausible way of getting these rules
/// wrong - an inclusive bound written as exclusive, the tiers tested in the wrong
/// order, the cap applied as a clamp instead of a rejection - shows up at a
/// boundary and nowhere else.
/// </remarks>
public class QuantityTierDiscountPolicyTests
{
    private readonly QuantityTierDiscountPolicy _policy = new();

    /// <summary>
    /// Walks the entire sellable range, asserting the rate for every quantity from
    /// 1 to 20 plus the boundaries on either side.
    /// </summary>
    [Theory(DisplayName = "Discount rate should follow the quantity tiers")]
    // Below 4: "Purchases below 4 items cannot have a discount."
    [InlineData(1, 0.00)]
    [InlineData(2, 0.00)]
    [InlineData(3, 0.00)]
    // The lower boundary of the 10% tier. The brief's prose says "above 4" while its
    // restated tier list says "4+"; the tier list is authoritative, so 4 qualifies.
    // This is the single most consequential assertion in the file.
    [InlineData(4, 0.10)]
    [InlineData(5, 0.10)]
    [InlineData(9, 0.10)]
    // "Purchases between 10 and 20 identical items have a 20% discount." Both ends
    // inclusive.
    [InlineData(10, 0.20)]
    [InlineData(15, 0.20)]
    [InlineData(20, 0.20)]
    public void Given_Quantity_When_CalculatingDiscountRate_Then_ReturnsTierRate(int quantity, decimal expectedRate)
    {
        var rate = _policy.GetDiscountRate(quantity);

        rate.Should().Be(expectedRate);
    }

    [Fact(DisplayName = "Quantity of 3 should earn no discount and 4 should earn 10%")]
    public void Given_QuantitiesAroundLowerTier_When_CalculatingDiscountRate_Then_BoundaryIsAtFour()
    {
        // Stated as its own test because this boundary is where the brief
        // contradicts itself. If someone later reads "above 4" and moves the tier to
        // start at 5, this fails with an unmistakable message.
        _policy.GetDiscountRate(3).Should().Be(0m, "purchases below 4 items cannot have a discount");
        _policy.GetDiscountRate(4).Should().Be(0.10m, "the tier list states 4+ items receive 10%");
    }

    [Fact(DisplayName = "Quantity of 9 should earn 10% and 10 should earn 20%")]
    public void Given_QuantitiesAroundUpperTier_When_CalculatingDiscountRate_Then_BoundaryIsAtTen()
    {
        // Guards the order the tiers are tested in. Checking the 10% condition first
        // would match quantities of 10 or more and shadow the 20% tier entirely.
        _policy.GetDiscountRate(9).Should().Be(0.10m);
        _policy.GetDiscountRate(10).Should().Be(0.20m);
    }

    [Theory(DisplayName = "Quantity above 20 should be rejected")]
    [InlineData(21)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void Given_QuantityAboveMaximum_When_CalculatingDiscountRate_Then_ThrowsDomainException(int quantity)
    {
        // "It's not possible to sell above 20 identical items." The operation is
        // refused outright; it is not clamped to 20, which would silently sell the
        // customer fewer items than they asked for.
        var act = () => _policy.GetDiscountRate(quantity);

        act.Should().Throw<DomainException>()
            .WithMessage("*more than 20 identical items*");
    }

    [Theory(DisplayName = "Quantity below one should be rejected")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Given_QuantityBelowOne_When_CalculatingDiscountRate_Then_ThrowsDomainException(int quantity)
    {
        var act = () => _policy.GetDiscountRate(quantity);

        act.Should().Throw<DomainException>()
            .WithMessage("*at least 1*");
    }

    [Fact(DisplayName = "Quantity of exactly 20 should be allowed")]
    public void Given_MaximumQuantity_When_CalculatingDiscountRate_Then_DoesNotThrow()
    {
        // The cap is "above 20", so 20 itself is sellable. An off-by-one here would
        // reject a legitimate order.
        var act = () => _policy.GetDiscountRate(QuantityTierDiscountPolicy.MaximumQuantity);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "The shared instance should behave like a new one")]
    public void Given_SharedInstance_When_CalculatingDiscountRate_Then_ReturnsSameResult()
    {
        // The policy is shared as a static default, which is only safe because it
        // holds no state.
        QuantityTierDiscountPolicy.Instance.GetDiscountRate(15)
            .Should().Be(_policy.GetDiscountRate(15));
    }
}
