using Ambev.DeveloperEvaluation.Domain.Events;
using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.Unit.Domain.Entities.TestData;
using FluentAssertions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Domain.Entities;

/// <summary>
/// Contains unit tests for the <see cref="Ambev.DeveloperEvaluation.Domain.Entities.Sale"/>
/// aggregate: its invariants, the money it calculates, its cancellation semantics
/// and the events it raises.
/// </summary>
public class SaleTests
{
    // -- Creation ------------------------------------------------------------

    [Fact(DisplayName = "Creating a sale should populate it and raise SaleCreated")]
    public void Given_ValidData_When_Creating_Then_PopulatesSaleAndRaisesEvent()
    {
        var customer = SaleTestData.GenerateCustomer();
        var branch = SaleTestData.GenerateBranch();
        var saleDate = new DateTime(2024, 3, 1);

        var sale = Ambev.DeveloperEvaluation.Domain.Entities.Sale.Create(
            "SALE-000001", saleDate, customer, branch);

        sale.Id.Should().NotBeEmpty();
        sale.SaleNumber.Should().Be("SALE-000001");
        sale.SaleDate.Should().Be(saleDate);
        sale.Customer.Should().Be(customer);
        sale.Branch.Should().Be(branch);
        sale.TotalAmount.Should().Be(0m);
        sale.IsCancelled.Should().BeFalse();
        sale.Items.Should().BeEmpty();

        sale.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SaleCreatedEvent>();
    }

    [Theory(DisplayName = "Creating a sale without a number should be rejected")]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_BlankSaleNumber_When_Creating_Then_ThrowsDomainException(string saleNumber)
    {
        var act = () => Ambev.DeveloperEvaluation.Domain.Entities.Sale.Create(
            saleNumber, DateTime.UtcNow, SaleTestData.GenerateCustomer(), SaleTestData.GenerateBranch());

        act.Should().Throw<DomainException>().WithMessage("*sale number is required*");
    }

    [Fact(DisplayName = "Creating a sale should trim the sale number")]
    public void Given_PaddedSaleNumber_When_Creating_Then_TrimsIt()
    {
        var sale = Ambev.DeveloperEvaluation.Domain.Entities.Sale.Create(
            "  SALE-42  ", DateTime.UtcNow, SaleTestData.GenerateCustomer(), SaleTestData.GenerateBranch());

        sale.SaleNumber.Should().Be("SALE-42");
    }

    // -- Discounts and totals -------------------------------------------------

    [Theory(DisplayName = "Adding an item should apply the discount its quantity earns")]
    // quantity, unitPrice, expectedDiscount, expectedItemTotal
    [InlineData(3, 100.00, 0.00, 300.00)]    // below 4: no discount
    [InlineData(4, 100.00, 40.00, 360.00)]   // 4+: 10% of 400
    [InlineData(9, 100.00, 90.00, 810.00)]   // still 10%
    [InlineData(10, 100.00, 200.00, 800.00)] // 10-20: 20% of 1000
    [InlineData(20, 100.00, 400.00, 1600.00)]// 20% at the cap
    public void Given_Quantity_When_AddingItem_Then_AppliesExpectedDiscount(
        int quantity, decimal unitPrice, decimal expectedDiscount, decimal expectedTotal)
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var item = sale.AddItem(SaleTestData.GenerateProduct(), quantity, unitPrice);

        item.Discount.Should().Be(expectedDiscount);
        item.TotalAmount.Should().Be(expectedTotal);

        // The sale total must track its items, not lag behind them.
        sale.TotalAmount.Should().Be(expectedTotal);
    }

    [Fact(DisplayName = "Item discount and total should add back up to the gross amount")]
    public void Given_PriceThatRounds_When_AddingItem_Then_FiguresReconcile()
    {
        // 7 x 33.33 = 233.31, and 10% of that is 23.331, which has to be rounded.
        // Rounding the discount and the total independently can leave the two
        // disagreeing with the gross by a cent, so the invariant worth asserting is
        // that they still reconcile.
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var item = sale.AddItem(SaleTestData.GenerateProduct(), 7, 33.33m);

        var gross = Math.Round(7 * 33.33m, 2);

        item.Discount.Should().Be(23.33m);
        item.TotalAmount.Should().Be(gross - item.Discount);
        (item.TotalAmount + item.Discount).Should().Be(gross);
    }

    [Fact(DisplayName = "Sale total should be the sum of its items")]
    public void Given_MultipleItems_When_Added_Then_TotalIsTheirSum()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var first = sale.AddItem(SaleTestData.GenerateProduct(), 2, 50.00m);   // 100.00
        var second = sale.AddItem(SaleTestData.GenerateProduct(), 5, 20.00m);  // 90.00 after 10%

        sale.TotalAmount.Should().Be(first.TotalAmount + second.TotalAmount);
        sale.TotalAmount.Should().Be(190.00m);
    }

    [Fact(DisplayName = "Adding an item above 20 units should be rejected")]
    public void Given_QuantityAboveMaximum_When_AddingItem_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var act = () => sale.AddItem(SaleTestData.GenerateProduct(), 21, 10.00m);

        act.Should().Throw<DomainException>().WithMessage("*more than 20 identical items*");
    }

    [Fact(DisplayName = "A rejected item should not be added to the sale")]
    public void Given_InvalidQuantity_When_AddingItem_Then_SaleIsUnchanged()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        sale.AddItem(SaleTestData.GenerateProduct(), 2, 10.00m);
        var totalBefore = sale.TotalAmount;

        var act = () => sale.AddItem(SaleTestData.GenerateProduct(), 21, 10.00m);
        act.Should().Throw<DomainException>();

        // A failed operation must leave no trace, or the sale ends up holding an
        // item that was never legally added.
        sale.Items.Should().HaveCount(1);
        sale.TotalAmount.Should().Be(totalBefore);
    }

    [Fact(DisplayName = "Adding an item with a negative price should be rejected")]
    public void Given_NegativeUnitPrice_When_AddingItem_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var act = () => sale.AddItem(SaleTestData.GenerateProduct(), 2, -1.00m);

        act.Should().Throw<DomainException>().WithMessage("*unit price cannot be negative*");
    }

    [Fact(DisplayName = "A zero unit price should be allowed")]
    public void Given_ZeroUnitPrice_When_AddingItem_Then_Succeeds()
    {
        // A giveaway or a replacement is a legitimate zero-price line, unlike a
        // negative one.
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var item = sale.AddItem(SaleTestData.GenerateProduct(), 5, 0m);

        item.TotalAmount.Should().Be(0m);
        sale.TotalAmount.Should().Be(0m);
    }

    // -- One line per product -------------------------------------------------

    [Fact(DisplayName = "Adding the same product twice should be rejected")]
    public void Given_ProductAlreadyOnSale_When_AddingItem_Then_ThrowsDomainException()
    {
        // The invariant that gives "identical items" a single meaning. Without it,
        // two lines of 20 would sell 40 units of one product.
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var product = SaleTestData.GenerateProduct();

        sale.AddItem(product, 5, 10.00m);
        var act = () => sale.AddItem(product, 5, 10.00m);

        act.Should().Throw<DomainException>().WithMessage("*already on this sale*");
        sale.Items.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Splitting a quantity across two lines should not bypass the cap")]
    public void Given_SplitQuantity_When_AddingItems_Then_CapCannotBeBypassed()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var product = SaleTestData.GenerateProduct();

        sale.AddItem(product, 20, 10.00m);

        // 20 + 20 would be 40 units of one product if a second line were allowed.
        var act = () => sale.AddItem(product, 20, 10.00m);

        act.Should().Throw<DomainException>();
        sale.Items.Sum(i => i.Quantity).Should().Be(20);
    }

    [Fact(DisplayName = "Different products should each get their own line")]
    public void Given_DifferentProducts_When_AddingItems_Then_BothAreAdded()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();

        sale.AddItem(SaleTestData.GenerateProduct(), 20, 10.00m);
        sale.AddItem(SaleTestData.GenerateProduct(), 20, 10.00m);

        // The cap is per product, not per sale: 20 of each of two products is fine.
        sale.Items.Should().HaveCount(2);
    }

    // -- Update ---------------------------------------------------------------

    [Fact(DisplayName = "Updating a sale should replace its details and items and raise SaleModified")]
    public void Given_ValidDrafts_When_Updating_Then_ReplacesContentAndRaisesEvent()
    {
        var sale = SaleTestData.GenerateSaleWithItems(3);
        var newCustomer = SaleTestData.GenerateCustomer();
        var newBranch = SaleTestData.GenerateBranch();
        var newDate = new DateTime(2025, 1, 15);

        sale.Update(newDate, newCustomer, newBranch, [SaleTestData.GenerateDraft(quantity: 10)]);

        sale.SaleDate.Should().Be(newDate);
        sale.Customer.Should().Be(newCustomer);
        sale.Branch.Should().Be(newBranch);
        sale.UpdatedAt.Should().NotBeNull();

        // PUT semantics: the item list is replaced wholesale, not merged.
        sale.Items.Should().ContainSingle();

        sale.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SaleModifiedEvent>();
    }

    [Fact(DisplayName = "Updating with no items should be rejected")]
    public void Given_EmptyDrafts_When_Updating_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(2);

        var act = () => sale.Update(
            DateTime.UtcNow, SaleTestData.GenerateCustomer(), SaleTestData.GenerateBranch(), []);

        act.Should().Throw<DomainException>().WithMessage("*at least one item*");
    }

    [Fact(DisplayName = "Updating with a duplicated product should be rejected")]
    public void Given_DuplicateProductInDrafts_When_Updating_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        var product = SaleTestData.GenerateProduct();

        var act = () => sale.Update(
            DateTime.UtcNow,
            SaleTestData.GenerateCustomer(),
            SaleTestData.GenerateBranch(),
            [SaleTestData.GenerateDraft(product: product), SaleTestData.GenerateDraft(product: product)]);

        act.Should().Throw<DomainException>().WithMessage("*appears more than once*");
    }

    [Fact(DisplayName = "A failed update should leave the sale untouched")]
    public void Given_InvalidDraft_When_Updating_Then_SaleIsUnchanged()
    {
        // The replacement items are built into a scratch list first precisely so
        // that a rejection partway through cannot leave the sale half updated.
        var sale = SaleTestData.GenerateSaleWithItems(2);
        var originalCustomer = sale.Customer;
        var originalTotal = sale.TotalAmount;
        var originalItemCount = sale.Items.Count;

        var act = () => sale.Update(
            DateTime.UtcNow,
            SaleTestData.GenerateCustomer(),
            SaleTestData.GenerateBranch(),
            [SaleTestData.GenerateDraft(quantity: 5), SaleTestData.GenerateDraft(quantity: 21)]);

        act.Should().Throw<DomainException>();

        sale.Customer.Should().Be(originalCustomer);
        sale.TotalAmount.Should().Be(originalTotal);
        sale.Items.Should().HaveCount(originalItemCount);
        sale.DomainEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "Updating should keep the identity of an item whose product is unchanged")]
    public void Given_DraftForExistingProduct_When_Updating_Then_ItemKeepsItsId()
    {
        // Items are reconciled by product rather than rebuilt, so an item id a client
        // already holds from the cancel endpoint stays valid across an update.
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var product = SaleTestData.GenerateProduct();
        var original = sale.AddItem(product, 2, 100.00m);
        sale.ClearDomainEvents();

        sale.Update(
            sale.SaleDate, sale.Customer, sale.Branch,
            [new Ambev.DeveloperEvaluation.Domain.ValueObjects.SaleItemDraft(product, 10, 100.00m)]);

        var item = sale.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(original.Id);
        item.Quantity.Should().Be(10);
        item.DiscountRate.Should().Be(0.20m);
    }

    [Fact(DisplayName = "Updating should remove active items no draft mentions")]
    public void Given_DraftsOmittingAProduct_When_Updating_Then_RemovesThatItem()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var kept = SaleTestData.GenerateProduct();
        var dropped = SaleTestData.GenerateProduct();
        sale.AddItem(kept, 2, 10.00m);
        sale.AddItem(dropped, 3, 10.00m);
        sale.ClearDomainEvents();

        sale.Update(
            sale.SaleDate, sale.Customer, sale.Branch,
            [new Ambev.DeveloperEvaluation.Domain.ValueObjects.SaleItemDraft(kept, 2, 10.00m)]);

        // PUT carries the whole sale, so a product left out of the request is no
        // longer part of it.
        sale.Items.Should().ContainSingle();
        sale.Items.Single().Product.Id.Should().Be(kept.Id);
    }

    [Fact(DisplayName = "Updating should preserve cancelled items")]
    public void Given_CancelledItem_When_Updating_Then_ItIsPreserved()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var cancelledProduct = SaleTestData.GenerateProduct();
        var activeProduct = SaleTestData.GenerateProduct();
        var toCancel = sale.AddItem(cancelledProduct, 2, 10.00m);
        sale.AddItem(activeProduct, 3, 10.00m);
        sale.CancelItem(toCancel.Id);
        sale.ClearDomainEvents();

        sale.Update(
            sale.SaleDate, sale.Customer, sale.Branch,
            [new Ambev.DeveloperEvaluation.Domain.ValueObjects.SaleItemDraft(activeProduct, 5, 10.00m)]);

        // A cancelled line is historical record and is not swept away by a later
        // update, even though no draft mentions it.
        sale.Items.Should().HaveCount(2);
        sale.Items.Single(i => i.Id == toCancel.Id).IsCancelled.Should().BeTrue();
    }

    [Fact(DisplayName = "Updating with a draft for a cancelled item's product should be rejected")]
    public void Given_DraftForCancelledProduct_When_Updating_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var product = SaleTestData.GenerateProduct();
        var item = sale.AddItem(product, 2, 10.00m);
        sale.CancelItem(item.Id);
        sale.ClearDomainEvents();

        var act = () => sale.Update(
            sale.SaleDate, sale.Customer, sale.Branch,
            [new Ambev.DeveloperEvaluation.Domain.ValueObjects.SaleItemDraft(product, 5, 10.00m)]);

        // Refused rather than silently reviving the cancelled line.
        act.Should().Throw<DomainException>().WithMessage("*was cancelled*");
    }

    [Fact(DisplayName = "Updating should refresh a changed product title")]
    public void Given_NewTitleForSameProduct_When_Updating_Then_RefreshesTitle()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var product = SaleTestData.GenerateProduct();
        sale.AddItem(product, 2, 10.00m);
        sale.ClearDomainEvents();

        var renamed = new Ambev.DeveloperEvaluation.Domain.ValueObjects.ProductReference(
            product.Id, "Renamed Product");

        sale.Update(
            sale.SaleDate, sale.Customer, sale.Branch,
            [new Ambev.DeveloperEvaluation.Domain.ValueObjects.SaleItemDraft(renamed, 2, 10.00m)]);

        sale.Items.Single().Product.Title.Should().Be("Renamed Product");
        sale.Items.Single().Product.Id.Should().Be(product.Id);
    }

    // -- Cancelling a sale ----------------------------------------------------

    [Fact(DisplayName = "Cancelling a sale should zero its total and raise SaleCancelled")]
    public void Given_ActiveSale_When_Cancelled_Then_ZeroesTotalAndRaisesEvent()
    {
        var sale = SaleTestData.GenerateSaleWithSingleItem(5, 100.00m);
        var totalBeforeCancel = sale.TotalAmount;

        sale.Cancel();

        sale.IsCancelled.Should().BeTrue();
        sale.CancelledAt.Should().NotBeNull();
        sale.TotalAmount.Should().Be(0m);

        // The items survive so the record of what was ordered is not lost.
        sale.Items.Should().ContainSingle();

        var cancelled = sale.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SaleCancelledEvent>().Subject;

        // The event carries the amount being voided, not the zeroed total.
        cancelled.TotalAmount.Should().Be(totalBeforeCancel);
    }

    [Fact(DisplayName = "Cancelling an already cancelled sale should be rejected")]
    public void Given_CancelledSale_When_CancelledAgain_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        sale.Cancel();

        var act = () => sale.Cancel();

        act.Should().Throw<DomainException>().WithMessage("*cancelled*");
    }

    [Fact(DisplayName = "A cancelled sale should refuse new items")]
    public void Given_CancelledSale_When_AddingItem_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        sale.Cancel();

        var act = () => sale.AddItem(SaleTestData.GenerateProduct(), 2, 10.00m);

        act.Should().Throw<DomainException>().WithMessage("*cancelled*");
    }

    [Fact(DisplayName = "A cancelled sale should refuse updates")]
    public void Given_CancelledSale_When_Updating_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);
        sale.Cancel();

        var act = () => sale.Update(
            DateTime.UtcNow,
            SaleTestData.GenerateCustomer(),
            SaleTestData.GenerateBranch(),
            [SaleTestData.GenerateDraft()]);

        act.Should().Throw<DomainException>().WithMessage("*cancelled*");
    }

    // -- Cancelling an item ---------------------------------------------------

    [Fact(DisplayName = "Cancelling an item should recalculate the total and raise ItemCancelled")]
    public void Given_SaleWithItems_When_CancellingItem_Then_RecalculatesAndRaisesEvent()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var kept = sale.AddItem(SaleTestData.GenerateProduct(), 2, 50.00m);   // 100.00
        var removed = sale.AddItem(SaleTestData.GenerateProduct(), 5, 20.00m); // 90.00
        sale.ClearDomainEvents();

        sale.CancelItem(removed.Id);

        // The cancelled line stops counting towards the total.
        sale.TotalAmount.Should().Be(kept.TotalAmount);
        sale.TotalAmount.Should().Be(100.00m);

        // The row stays, flagged, so the sale still records what was withdrawn.
        sale.Items.Should().HaveCount(2);
        sale.Items.Single(i => i.Id == removed.Id).IsCancelled.Should().BeTrue();
        sale.IsCancelled.Should().BeFalse();

        var evt = sale.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ItemCancelledEvent>().Subject;

        evt.SaleItemId.Should().Be(removed.Id);
        evt.SaleTotalAmount.Should().Be(100.00m);
    }

    [Fact(DisplayName = "Cancelling an unknown item should report it as not found")]
    public void Given_UnknownItemId_When_CancellingItem_Then_ThrowsKeyNotFound()
    {
        var sale = SaleTestData.GenerateSaleWithItems(1);

        var act = () => sale.CancelItem(Guid.NewGuid());

        // KeyNotFoundException rather than DomainException, because the middleware
        // maps it to 404 while a rule violation maps to 400.
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact(DisplayName = "Cancelling an already cancelled item should be rejected")]
    public void Given_CancelledItem_When_CancelledAgain_Then_ThrowsDomainException()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var item = sale.AddItem(SaleTestData.GenerateProduct(), 5, 20.00m);
        sale.CancelItem(item.Id);

        var act = () => sale.CancelItem(item.Id);

        // Rejecting the repeat keeps a duplicate ItemCancelled event off the bus.
        act.Should().Throw<DomainException>().WithMessage("*already cancelled*");
    }

    [Fact(DisplayName = "Cancelling every item should leave the sale total at zero but active")]
    public void Given_AllItemsCancelled_When_Recalculated_Then_TotalIsZeroAndSaleActive()
    {
        var sale = SaleTestData.GenerateSaleWithoutItems();
        var first = sale.AddItem(SaleTestData.GenerateProduct(), 2, 50.00m);
        var second = sale.AddItem(SaleTestData.GenerateProduct(), 5, 20.00m);

        sale.CancelItem(first.Id);
        sale.CancelItem(second.Id);

        sale.TotalAmount.Should().Be(0m);

        // Cancelling the last item is not the same as cancelling the sale; only an
        // explicit Cancel() does that.
        sale.IsCancelled.Should().BeFalse();
    }

    // -- Encapsulation --------------------------------------------------------

    [Fact(DisplayName = "The items collection should not be modifiable from outside")]
    public void Given_Sale_When_InspectingItems_Then_CollectionIsReadOnly()
    {
        var sale = SaleTestData.GenerateSaleWithItems(2);

        // Casting back to a mutable list is the obvious way to try to smuggle an
        // item past AddItem's checks; AsReadOnly makes that cast fail.
        sale.Items.Should().BeAssignableTo<IReadOnlyCollection<Ambev.DeveloperEvaluation.Domain.Entities.SaleItem>>();
        (sale.Items as ICollection<Ambev.DeveloperEvaluation.Domain.Entities.SaleItem>)
            ?.IsReadOnly.Should().NotBe(false);
    }

    [Fact(DisplayName = "A custom discount policy should be used when supplied")]
    public void Given_CustomPolicy_When_AddingItem_Then_UsesIt()
    {
        // Proves the policy is a genuine seam rather than decoration: swapping it
        // changes the money.
        var sale = SaleTestData.GenerateSaleWithoutItems();

        var item = sale.AddItem(SaleTestData.GenerateProduct(), 2, 100.00m, new FlatFiftyPercentPolicy());

        item.Discount.Should().Be(100.00m);
        item.TotalAmount.Should().Be(100.00m);
    }

    /// <summary>
    /// A stand-in policy that always grants 50%, used to prove the policy is
    /// injectable.
    /// </summary>
    private sealed class FlatFiftyPercentPolicy : IDiscountPolicy
    {
        public decimal GetDiscountRate(int quantity) => 0.50m;
    }
}
