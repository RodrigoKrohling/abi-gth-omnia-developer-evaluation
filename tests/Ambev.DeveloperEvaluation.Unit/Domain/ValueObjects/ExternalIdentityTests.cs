using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Domain.ValueObjects;

/// <summary>
/// Contains unit tests for the External Identity value objects:
/// <see cref="CustomerReference"/>, <see cref="BranchReference"/> and
/// <see cref="ProductReference"/>.
/// </summary>
/// <remarks>
/// Two properties matter for these types. They must refuse to exist in an unusable
/// state, so an invalid reference is caught at construction rather than discovered
/// in the database. And they must compare by value, because they are value objects:
/// two references to the same customer are the same reference, and the aggregate's
/// duplicate-product check depends on that holding.
/// </remarks>
public class ExternalIdentityTests
{
    private static readonly Guid AnId = Guid.NewGuid();

    // -- Construction ---------------------------------------------------------

    [Fact(DisplayName = "A customer reference should keep its id and name")]
    public void Given_ValidData_When_CreatingCustomer_Then_KeepsValues()
    {
        var customer = new CustomerReference(AnId, "Maria Silva");

        customer.Id.Should().Be(AnId);
        customer.Name.Should().Be("Maria Silva");
    }

    [Fact(DisplayName = "A reference should trim its description")]
    public void Given_PaddedName_When_CreatingCustomer_Then_TrimsIt()
    {
        var customer = new CustomerReference(AnId, "  Maria Silva  ");

        customer.Name.Should().Be("Maria Silva");
    }

    [Fact(DisplayName = "A customer reference with an empty id should be rejected")]
    public void Given_EmptyId_When_CreatingCustomer_Then_ThrowsArgumentException()
    {
        // Guid.Empty identifies nobody, so the reference would be useless.
        var act = () => new CustomerReference(Guid.Empty, "Maria Silva");

        act.Should().Throw<ArgumentException>().WithMessage("*customer identifier is required*");
    }

    [Theory(DisplayName = "A customer reference without a name should be rejected")]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_BlankName_When_CreatingCustomer_Then_ThrowsArgumentException(string name)
    {
        // The denormalized description is the whole reason the reference carries
        // more than an id; a blank one defeats the pattern.
        var act = () => new CustomerReference(AnId, name);

        act.Should().Throw<ArgumentException>().WithMessage("*customer name is required*");
    }

    [Fact(DisplayName = "A branch reference with an empty id should be rejected")]
    public void Given_EmptyId_When_CreatingBranch_Then_ThrowsArgumentException()
    {
        var act = () => new BranchReference(Guid.Empty, "Downtown");

        act.Should().Throw<ArgumentException>().WithMessage("*branch identifier is required*");
    }

    [Theory(DisplayName = "A branch reference without a name should be rejected")]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_BlankName_When_CreatingBranch_Then_ThrowsArgumentException(string name)
    {
        var act = () => new BranchReference(AnId, name);

        act.Should().Throw<ArgumentException>().WithMessage("*branch name is required*");
    }

    [Fact(DisplayName = "A product reference with an empty id should be rejected")]
    public void Given_EmptyId_When_CreatingProduct_Then_ThrowsArgumentException()
    {
        var act = () => new ProductReference(Guid.Empty, "Backpack");

        act.Should().Throw<ArgumentException>().WithMessage("*product identifier is required*");
    }

    [Theory(DisplayName = "A product reference without a title should be rejected")]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_BlankTitle_When_CreatingProduct_Then_ThrowsArgumentException(string title)
    {
        var act = () => new ProductReference(AnId, title);

        act.Should().Throw<ArgumentException>().WithMessage("*product title is required*");
    }

    // -- Value equality -------------------------------------------------------

    [Fact(DisplayName = "References with the same values should be equal")]
    public void Given_SameValues_When_Compared_Then_AreEqual()
    {
        var first = new CustomerReference(AnId, "Maria Silva");
        var second = new CustomerReference(AnId, "Maria Silva");

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact(DisplayName = "References with different ids should not be equal")]
    public void Given_DifferentIds_When_Compared_Then_AreNotEqual()
    {
        var first = new ProductReference(Guid.NewGuid(), "Backpack");
        var second = new ProductReference(Guid.NewGuid(), "Backpack");

        first.Should().NotBe(second);
    }

    [Fact(DisplayName = "References with different descriptions should not be equal")]
    public void Given_DifferentDescriptions_When_Compared_Then_AreNotEqual()
    {
        // The denormalized description is part of the value, not incidental
        // metadata: the same customer recorded under two names is two different
        // historical facts.
        var first = new CustomerReference(AnId, "Maria Silva");
        var second = new CustomerReference(AnId, "Maria Santos");

        first.Should().NotBe(second);
    }
}
