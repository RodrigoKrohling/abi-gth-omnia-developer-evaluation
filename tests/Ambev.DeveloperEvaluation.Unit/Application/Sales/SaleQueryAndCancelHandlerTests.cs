using Ambev.DeveloperEvaluation.Application.Sales.CancelSale;
using Ambev.DeveloperEvaluation.Application.Sales.CancelSaleItem;
using Ambev.DeveloperEvaluation.Application.Sales.DeleteSale;
using Ambev.DeveloperEvaluation.Application.Sales.GetSale;
using Ambev.DeveloperEvaluation.Application.Sales.ListSales;
using Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;
using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Unit.Application.Sales.TestData;
using Ambev.DeveloperEvaluation.Unit.Domain.Entities.TestData;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Application.Sales;

/// <summary>
/// Contains unit tests for the read, update, cancel and delete sale handlers.
/// </summary>
/// <remarks>
/// Grouped into one class because each handler is small and they share the same
/// substituted repository and mapper setup. Splitting them would mean six near
/// identical constructors.
/// </remarks>
public class SaleQueryAndCancelHandlerTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IDiscountPolicy _discountPolicy = new QuantityTierDiscountPolicy();
    private readonly AutoMapper.IMapper _mapper = SaleCommandTestData.CreateMapper();

    /// <summary>
    /// Stubs the repository to return the given sale for its identifier.
    /// </summary>
    private void GivenStored(Sale sale) =>
        _saleRepository.GetByIdAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(sale);

    // -- GetSale --------------------------------------------------------------

    [Fact(DisplayName = "Getting an existing sale should return it")]
    public async Task Given_ExistingSale_When_Getting_Then_ReturnsIt()
    {
        var sale = SaleTestData.GenerateSaleWithItems(2);
        GivenStored(sale);
        var handler = new GetSaleHandler(_saleRepository, _mapper);

        var result = await handler.Handle(new GetSaleCommand(sale.Id), CancellationToken.None);

        result.Id.Should().Be(sale.Id);
        result.SaleNumber.Should().Be(sale.SaleNumber);
        result.Items.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Getting a missing sale should report it as not found")]
    public async Task Given_MissingSale_When_Getting_Then_ThrowsKeyNotFound()
    {
        _saleRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Sale?)null);
        var handler = new GetSaleHandler(_saleRepository, _mapper);

        var act = () => handler.Handle(new GetSaleCommand(Guid.NewGuid()), CancellationToken.None);

        // KeyNotFoundException is what the middleware turns into a 404.
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact(DisplayName = "Getting a sale with an empty id should be rejected")]
    public async Task Given_EmptyId_When_Getting_Then_ThrowsValidationException()
    {
        var handler = new GetSaleHandler(_saleRepository, _mapper);

        var act = () => handler.Handle(new GetSaleCommand(Guid.Empty), CancellationToken.None);

        // Rejected before any database round trip, since it could never match.
        await act.Should().ThrowAsync<ValidationException>();
        await _saleRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -- ListSales ------------------------------------------------------------

    [Fact(DisplayName = "Listing should return the page and compute the page count")]
    public async Task Given_Sales_When_Listing_Then_ReturnsPageAndTotals()
    {
        var sales = new List<Sale>
        {
            SaleTestData.GenerateSaleWithItems(1),
            SaleTestData.GenerateSaleWithItems(1)
        };

        _saleRepository.ListAsync(2, 5, "saleDate desc",
                Arg.Any<IReadOnlyDictionary<string, string?>>(), Arg.Any<CancellationToken>())
            .Returns((sales, 12));

        var handler = new ListSalesHandler(_saleRepository, _mapper);

        var result = await handler.Handle(
            new ListSalesCommand { Page = 2, Size = 5, Order = "saleDate desc" },
            CancellationToken.None);

        result.Sales.Should().HaveCount(2);
        result.TotalCount.Should().Be(12);
        result.CurrentPage.Should().Be(2);

        // 12 items at 5 per page spans 3 pages; the last one is partial.
        result.TotalPages.Should().Be(3);
    }

    [Fact(DisplayName = "Listing should pass its filters through to the repository")]
    public async Task Given_Filters_When_Listing_Then_ForwardsThem()
    {
        _saleRepository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string?>>(), Arg.Any<CancellationToken>())
            .Returns((new List<Sale>(), 0));

        var handler = new ListSalesHandler(_saleRepository, _mapper);
        var filters = new Dictionary<string, string?> { ["branch.name"] = "Downtown" };

        await handler.Handle(
            new ListSalesCommand { Page = 1, Size = 10, Filters = filters },
            CancellationToken.None);

        await _saleRepository.Received(1).ListAsync(
            1, 10, null,
            Arg.Is<IReadOnlyDictionary<string, string?>>(f => f["branch.name"] == "Downtown"),
            Arg.Any<CancellationToken>());
    }

    // -- UpdateSale -----------------------------------------------------------

    [Fact(DisplayName = "Updating an existing sale should apply the changes")]
    public async Task Given_ValidCommand_When_Updating_Then_AppliesChanges()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        GivenStored(sale);
        _saleRepository.UpdateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Sale>());

        var handler = new UpdateSaleHandler(_saleRepository, _discountPolicy, _mapper);
        var command = SaleCommandTestData.GenerateUpdateCommand(sale.Id, itemCount: 2);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Customer.Description.Should().Be(command.CustomerName);
        result.Branch.Description.Should().Be(command.BranchName);
        result.Items.Should().HaveCount(2);

        await _saleRepository.Received(1).UpdateAsync(sale, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Updating a missing sale should report it as not found")]
    public async Task Given_MissingSale_When_Updating_Then_ThrowsKeyNotFound()
    {
        _saleRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Sale?)null);
        var handler = new UpdateSaleHandler(_saleRepository, _discountPolicy, _mapper);

        var act = () => handler.Handle(
            SaleCommandTestData.GenerateUpdateCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact(DisplayName = "Updating a cancelled sale should be refused by the domain")]
    public async Task Given_CancelledSale_When_Updating_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        sale.Cancel();
        GivenStored(sale);

        var handler = new UpdateSaleHandler(_saleRepository, _discountPolicy, _mapper);

        var act = () => handler.Handle(
            SaleCommandTestData.GenerateUpdateCommand(sale.Id), CancellationToken.None);

        // The handler does not check this; the aggregate does. The rule lives in one
        // place and the handler simply lets it surface.
        await act.Should().ThrowAsync<DomainException>();
        await _saleRepository.DidNotReceive().UpdateAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    // -- CancelSale -----------------------------------------------------------

    [Fact(DisplayName = "Cancelling a sale should zero its total and persist it")]
    public async Task Given_ActiveSale_When_Cancelling_Then_CancelsAndPersists()
    {
        var sale = SaleTestData.GenerateSaleWithSingleItem(5, 100m);
        GivenStored(sale);
        var handler = new CancelSaleHandler(_saleRepository, _mapper);

        var result = await handler.Handle(new CancelSaleCommand(sale.Id), CancellationToken.None);

        result.IsCancelled.Should().BeTrue();
        result.TotalAmount.Should().Be(0m);
        result.CancelledAt.Should().NotBeNull();

        await _saleRepository.Received(1).UpdateAsync(sale, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Cancelling an already cancelled sale should be refused")]
    public async Task Given_CancelledSale_When_Cancelling_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        sale.Cancel();
        GivenStored(sale);
        var handler = new CancelSaleHandler(_saleRepository, _mapper);

        var act = () => handler.Handle(new CancelSaleCommand(sale.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // -- CancelSaleItem -------------------------------------------------------

    [Fact(DisplayName = "Cancelling an item should recalculate the sale total")]
    public async Task Given_SaleWithItems_When_CancellingItem_Then_RecalculatesTotal()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        sale.AddItem(SaleTestData.GenerateProduct(), 2, 50m);   // 100.00
        var toCancel = sale.AddItem(SaleTestData.GenerateProduct(), 5, 20m); // 90.00
        sale.ClearDomainEvents();
        GivenStored(sale);

        var handler = new CancelSaleItemHandler(_saleRepository, _mapper);

        var result = await handler.Handle(
            new CancelSaleItemCommand(sale.Id, toCancel.Id), CancellationToken.None);

        result.TotalAmount.Should().Be(100m);
        result.IsCancelled.Should().BeFalse();

        // The cancelled line is still reported, flagged, so the caller sees the full
        // history of the sale.
        result.Items.Should().HaveCount(2);
        result.Items.Single(i => i.Id == toCancel.Id).IsCancelled.Should().BeTrue();
    }

    [Fact(DisplayName = "Cancelling an unknown item should report it as not found")]
    public async Task Given_UnknownItem_When_Cancelling_Then_ThrowsKeyNotFound()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        GivenStored(sale);
        var handler = new CancelSaleItemHandler(_saleRepository, _mapper);

        var act = () => handler.Handle(
            new CancelSaleItemCommand(sale.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // -- DeleteSale -----------------------------------------------------------

    [Fact(DisplayName = "Deleting an existing sale should report success")]
    public async Task Given_ExistingSale_When_Deleting_Then_ReturnsSuccess()
    {
        var id = Guid.NewGuid();
        _saleRepository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteSaleHandler(_saleRepository);

        var result = await handler.Handle(new DeleteSaleCommand(id), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact(DisplayName = "Deleting a missing sale should report it as not found")]
    public async Task Given_MissingSale_When_Deleting_Then_ThrowsKeyNotFound()
    {
        _saleRepository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteSaleHandler(_saleRepository);

        var act = () => handler.Handle(new DeleteSaleCommand(Guid.NewGuid()), CancellationToken.None);

        // Answering 200 would tell the caller their request affected a resource that
        // never existed.
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
