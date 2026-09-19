using Ambev.DeveloperEvaluation.Application.Sales.CreateSale;
using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Unit.Application.Sales.TestData;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Application.Sales;

/// <summary>
/// Contains unit tests for <see cref="CreateSaleHandler"/>.
/// </summary>
/// <remarks>
/// The repository is substituted; the discount policy and the AutoMapper
/// configuration are real. Substituting the policy would make these tests prove
/// only that the handler calls something, while using the real one proves the
/// discounts actually reach the stored sale.
/// </remarks>
public class CreateSaleHandlerTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IDiscountPolicy _discountPolicy = new QuantityTierDiscountPolicy();
    private readonly CreateSaleHandler _handler;

    /// <summary>
    /// Initializes the handler with a substituted repository.
    /// </summary>
    public CreateSaleHandlerTests()
    {
        // By default the repository reports the sale number as free and echoes back
        // whatever it is asked to store, so tests only stub what they care about.
        _saleRepository.GetBySaleNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);

        _saleRepository.CreateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Sale>());

        _handler = new CreateSaleHandler(
            _saleRepository, _discountPolicy, SaleCommandTestData.CreateMapper());
    }

    [Fact(DisplayName = "A valid command should create the sale and return it")]
    public async Task Given_ValidCommand_When_Handled_Then_CreatesAndReturnsSale()
    {
        var command = SaleCommandTestData.GenerateCreateCommand(itemCount: 2);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Id.Should().NotBeEmpty();
        result.SaleNumber.Should().Be(command.SaleNumber);
        result.Customer.Id.Should().Be(command.CustomerId);
        result.Customer.Description.Should().Be(command.CustomerName);
        result.Branch.Description.Should().Be(command.BranchName);
        result.Items.Should().HaveCount(2);

        await _saleRepository.Received(1).CreateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "The created sale should carry the discounts the domain calculated")]
    public async Task Given_QuantityEarningDiscount_When_Handled_Then_ResultCarriesDiscount()
    {
        var command = SaleCommandTestData.GenerateCreateCommand(itemCount: 1, quantity: 10);
        command.Items[0].UnitPrice = 100m;

        var result = await _handler.Handle(command, CancellationToken.None);

        // The caller never supplies a discount, so this is the server's own
        // calculation surfacing through the handler and the mapper.
        var item = result.Items.Should().ContainSingle().Subject;
        item.DiscountRate.Should().Be(0.20m);
        item.Discount.Should().Be(200m);
        item.TotalAmount.Should().Be(800m);
        result.TotalAmount.Should().Be(800m);
    }

    [Fact(DisplayName = "A duplicate sale number should be reported as a conflict")]
    public async Task Given_ExistingSaleNumber_When_Handled_Then_ThrowsResourceConflict()
    {
        var command = SaleCommandTestData.GenerateCreateCommand();

        _saleRepository.GetBySaleNumberAsync(command.SaleNumber, Arg.Any<CancellationToken>())
            .Returns(Sale.Create(
                command.SaleNumber,
                DateTime.UtcNow,
                new Ambev.DeveloperEvaluation.Domain.ValueObjects.CustomerReference(Guid.NewGuid(), "Someone"),
                new Ambev.DeveloperEvaluation.Domain.ValueObjects.BranchReference(Guid.NewGuid(), "Somewhere")));

        var act = () => _handler.Handle(command, CancellationToken.None);

        // A dedicated ResourceConflictException, not InvalidOperationException, which
        // the middleware maps to 409. Matching the framework's generic exception there
        // would report unrelated programming errors as business conflicts.
        await act.Should().ThrowAsync<ResourceConflictException>()
            .WithMessage("*already exists*");

        await _saleRepository.DidNotReceive().CreateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "A command without items should be rejected")]
    public async Task Given_NoItems_When_Handled_Then_ThrowsValidationException()
    {
        var command = SaleCommandTestData.GenerateCreateCommand();
        command.Items.Clear();

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await _saleRepository.DidNotReceive().CreateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "A command without a sale number should be rejected")]
    public async Task Given_BlankSaleNumber_When_Handled_Then_ThrowsValidationException()
    {
        var command = SaleCommandTestData.GenerateCreateCommand();
        command.SaleNumber = string.Empty;

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact(DisplayName = "A quantity above 20 should be rejected before reaching the repository")]
    public async Task Given_QuantityAboveMaximum_When_Handled_Then_ThrowsValidationException()
    {
        var command = SaleCommandTestData.GenerateCreateCommand(itemCount: 1, quantity: 21);

        var act = () => _handler.Handle(command, CancellationToken.None);

        // Caught by the validator rather than by the aggregate, so the caller is told
        // which item was wrong. The aggregate would still refuse it either way.
        await act.Should().ThrowAsync<ValidationException>();
        await _saleRepository.DidNotReceive().CreateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "The same product twice should be rejected")]
    public async Task Given_DuplicateProduct_When_Handled_Then_ThrowsValidationException()
    {
        var command = SaleCommandTestData.GenerateCreateCommand(itemCount: 2);
        command.Items[1].ProductId = command.Items[0].ProductId;

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact(DisplayName = "A custom discount policy should reach the domain")]
    public async Task Given_CustomPolicy_When_Handled_Then_ItIsApplied()
    {
        // Proves the handler passes its injected policy through to the aggregate
        // rather than letting the aggregate fall back to its default.
        var handler = new CreateSaleHandler(
            _saleRepository, new FlatFiftyPercentPolicy(), SaleCommandTestData.CreateMapper());

        var command = SaleCommandTestData.GenerateCreateCommand(itemCount: 1, quantity: 2);
        command.Items[0].UnitPrice = 100m;

        var result = await handler.Handle(command, CancellationToken.None);

        result.Items.Single().Discount.Should().Be(100m);
    }

    /// <summary>A stand-in policy that always grants 50%.</summary>
    private sealed class FlatFiftyPercentPolicy : IDiscountPolicy
    {
        public decimal GetDiscountRate(int quantity) =>
            quantity is < 1 or > 20 ? throw new DomainException("out of range") : 0.50m;
    }
}
