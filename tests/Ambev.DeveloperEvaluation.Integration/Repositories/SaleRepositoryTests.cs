using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using Ambev.DeveloperEvaluation.Integration.Common;
using FluentAssertions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Integration.Repositories;

/// <summary>
/// Contains integration tests for
/// <see cref="Ambev.DeveloperEvaluation.ORM.Repositories.SaleRepository"/> against a
/// real EF Core model.
/// </summary>
/// <remarks>
/// These tests are about the persistence layer specifically: that the aggregate
/// survives a round trip with its owned value objects and its private item
/// collection intact, that the cascade delete works, and that the list query
/// filters, orders and pages as the API contract requires.
/// </remarks>
public class SaleRepositoryTests
{
    /// <summary>
    /// A convenience for the filter parameter when a test applies none.
    /// </summary>
    private static readonly Dictionary<string, string?> NoFilters = [];

    // -- Round trip ----------------------------------------------------------

    [Fact(DisplayName = "A stored sale should be retrievable with its items")]
    public async Task Given_StoredSale_When_RetrievedById_Then_ReturnsSaleWithItems()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0001");

        var found = await fixture.Repository.GetByIdAsync(stored.Id);

        found.Should().NotBeNull();
        found!.SaleNumber.Should().Be("SALE-0001");

        // The aggregate must come back whole. A sale loaded without its items could
        // not enforce its own rules or recalculate its total.
        found.Items.Should().ContainSingle();
    }

    [Fact(DisplayName = "Owned value objects should survive a round trip")]
    public async Task Given_StoredSale_When_Retrieved_Then_ExternalIdentitiesArePreserved()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0002", "Maria Silva", "Downtown");

        var found = await fixture.Repository.GetByIdAsync(stored.Id);

        // Customer, Branch and Product are mapped as owned types into columns on the
        // parent tables. If that mapping were wrong they would come back null.
        found!.Customer.Id.Should().Be(stored.Customer.Id);
        found.Customer.Name.Should().Be("Maria Silva");
        found.Branch.Id.Should().Be(stored.Branch.Id);
        found.Branch.Name.Should().Be("Downtown");

        var item = found.Items.Single();
        item.Product.Id.Should().Be(stored.Items.Single().Product.Id);
        item.Product.Title.Should().Be("Backpack");
    }

    [Fact(DisplayName = "Calculated money should survive a round trip")]
    public async Task Given_StoredSale_When_Retrieved_Then_MoneyIsPreserved()
    {
        using var fixture = new SaleRepositoryFixture();
        // 5 units earns the 10% tier: gross 500, discount 50, total 450.
        var stored = await fixture.GivenStoredSaleAsync("SALE-0003", quantity: 5, unitPrice: 100m);

        var found = await fixture.Repository.GetByIdAsync(stored.Id);
        var item = found!.Items.Single();

        item.Quantity.Should().Be(5);
        item.UnitPrice.Should().Be(100m);
        item.DiscountRate.Should().Be(0.10m);
        item.Discount.Should().Be(50m);
        item.TotalAmount.Should().Be(450m);
        found.TotalAmount.Should().Be(450m);
    }

    [Fact(DisplayName = "A retrieved sale should still enforce its invariants")]
    public async Task Given_RetrievedSale_When_AddingDuplicateProduct_Then_StillRejected()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0004");

        var found = await fixture.Repository.GetByIdAsync(stored.Id);
        var existingProduct = found!.Items.Single().Product;

        // Proves the private _items list was genuinely repopulated through the
        // backing field. Had EF left it empty, this duplicate would be accepted.
        var act = () => found.AddItem(existingProduct, 2, 10m);

        act.Should().Throw<Ambev.DeveloperEvaluation.Domain.Exceptions.DomainException>();
    }

    [Fact(DisplayName = "An unknown id should return null")]
    public async Task Given_UnknownId_When_Retrieved_Then_ReturnsNull()
    {
        using var fixture = new SaleRepositoryFixture();

        var found = await fixture.Repository.GetByIdAsync(Guid.NewGuid());

        found.Should().BeNull();
    }

    [Fact(DisplayName = "A sale should be retrievable by its sale number")]
    public async Task Given_StoredSale_When_RetrievedByNumber_Then_ReturnsSale()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-0005");

        var found = await fixture.Repository.GetBySaleNumberAsync("SALE-0005");

        found.Should().NotBeNull();
        found!.SaleNumber.Should().Be("SALE-0005");
    }

    // -- Update ---------------------------------------------------------------

    [Fact(DisplayName = "Updating a sale should persist new details and added items")]
    public async Task Given_StoredSale_When_Updated_Then_PersistsChanges()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0006");
        var originalProduct = stored.Items.Single().Product;

        var loaded = await fixture.Repository.GetByIdAsync(stored.Id);
        loaded!.Update(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new CustomerReference(Guid.NewGuid(), "Joao Souza"),
            new BranchReference(Guid.NewGuid(), "Uptown"),
            [
                // Keeps the original product but changes its quantity, and adds a
                // second product alongside it.
                new SaleItemDraft(new ProductReference(originalProduct.Id, originalProduct.Title), 10, 50m),
                new SaleItemDraft(new ProductReference(Guid.NewGuid(), "Wallet"), 2, 25m)
            ]);

        await fixture.Repository.UpdateAsync(loaded);
        fixture.Context.ChangeTracker.Clear();

        var reloaded = await fixture.Repository.GetByIdAsync(stored.Id);

        reloaded!.Customer.Name.Should().Be("Joao Souza");
        reloaded.Branch.Name.Should().Be("Uptown");
        reloaded.SaleDate.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        reloaded.Items.Should().HaveCount(2);

        // 10 x 50 less 20% = 400, plus 2 x 25 with no discount = 50.
        reloaded.TotalAmount.Should().Be(450m);
    }

    [Fact(DisplayName = "Updating should keep the identity of an item whose product is unchanged")]
    public async Task Given_StoredSale_When_Updated_Then_SurvivingItemKeepsItsId()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0009");
        var originalItemId = stored.Items.Single().Id;
        var originalProduct = stored.Items.Single().Product;

        var loaded = await fixture.Repository.GetByIdAsync(stored.Id);
        loaded!.Update(
            loaded.SaleDate,
            loaded.Customer,
            loaded.Branch,
            [new SaleItemDraft(new ProductReference(originalProduct.Id, originalProduct.Title), 12, 10m)]);

        await fixture.Repository.UpdateAsync(loaded);
        fixture.Context.ChangeTracker.Clear();

        var reloaded = await fixture.Repository.GetByIdAsync(stored.Id);
        var item = reloaded!.Items.Single();

        // The item is reconciled in place rather than deleted and recreated, so an
        // item id a client already holds stays valid across an update.
        item.Id.Should().Be(originalItemId);
        item.Quantity.Should().Be(12);
        item.DiscountRate.Should().Be(0.20m);
    }

    [Fact(DisplayName = "Cancelling an item should persist the flag and the new total")]
    public async Task Given_StoredSale_When_ItemCancelled_Then_PersistsFlagAndTotal()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0007");

        var loaded = await fixture.Repository.GetByIdAsync(stored.Id);
        loaded!.CancelItem(loaded.Items.Single().Id);
        await fixture.Repository.UpdateAsync(loaded);
        fixture.Context.ChangeTracker.Clear();

        var reloaded = await fixture.Repository.GetByIdAsync(stored.Id);

        // The row survives, flagged, so the record of what was ordered is kept.
        reloaded!.Items.Should().ContainSingle();
        reloaded.Items.Single().IsCancelled.Should().BeTrue();
        reloaded.Items.Single().CancelledAt.Should().NotBeNull();

        reloaded.TotalAmount.Should().Be(0m);
        reloaded.IsCancelled.Should().BeFalse();
    }

    // -- Delete ---------------------------------------------------------------

    [Fact(DisplayName = "Deleting a sale should remove it and its items")]
    public async Task Given_StoredSale_When_Deleted_Then_SaleAndItemsAreGone()
    {
        using var fixture = new SaleRepositoryFixture();
        var stored = await fixture.GivenStoredSaleAsync("SALE-0008");

        var deleted = await fixture.Repository.DeleteAsync(stored.Id);

        deleted.Should().BeTrue();
        (await fixture.Repository.GetByIdAsync(stored.Id)).Should().BeNull();

        // Items have no life outside their sale, so the cascade must take them too
        // rather than leaving orphan rows behind.
        fixture.Context.Set<SaleItem>()
            .Count(i => i.SaleId == stored.Id)
            .Should().Be(0);
    }

    [Fact(DisplayName = "Deleting an unknown sale should report that nothing was deleted")]
    public async Task Given_UnknownId_When_Deleted_Then_ReturnsFalse()
    {
        using var fixture = new SaleRepositoryFixture();

        var deleted = await fixture.Repository.DeleteAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    // -- Listing --------------------------------------------------------------

    [Fact(DisplayName = "Listing should page the results and report the full count")]
    public async Task Given_ManySales_When_Listed_Then_PagesAndCountsCorrectly()
    {
        using var fixture = new SaleRepositoryFixture();
        for (var i = 1; i <= 7; i++)
            await fixture.GivenStoredSaleAsync($"SALE-{i:D4}");

        var (sales, totalCount) = await fixture.Repository.ListAsync(2, 3, null, NoFilters);

        sales.Should().HaveCount(3);

        // The count is of everything that matched, not of the page, because the
        // pagination envelope needs it to compute the number of pages.
        totalCount.Should().Be(7);
    }

    [Fact(DisplayName = "The last page should return only the remaining sales")]
    public async Task Given_PartialLastPage_When_Listed_Then_ReturnsRemainder()
    {
        using var fixture = new SaleRepositoryFixture();
        for (var i = 1; i <= 7; i++)
            await fixture.GivenStoredSaleAsync($"SALE-{i:D4}");

        var (sales, totalCount) = await fixture.Repository.ListAsync(3, 3, null, NoFilters);

        sales.Should().ContainSingle();
        totalCount.Should().Be(7);
    }

    [Fact(DisplayName = "Listing should order by the requested clause")]
    public async Task Given_OrderClause_When_Listed_Then_AppliesIt()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-A", saleDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await fixture.GivenStoredSaleAsync("SALE-B", saleDate: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await fixture.GivenStoredSaleAsync("SALE-C", saleDate: new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var (sales, _) = await fixture.Repository.ListAsync(1, 10, "saleDate asc", NoFilters);

        sales.Select(s => s.SaleNumber).Should().ContainInOrder("SALE-A", "SALE-C", "SALE-B");
    }

    [Fact(DisplayName = "Listing without an order clause should return newest first")]
    public async Task Given_NoOrderClause_When_Listed_Then_ReturnsNewestFirst()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-A", saleDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await fixture.GivenStoredSaleAsync("SALE-B", saleDate: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var (sales, _) = await fixture.Repository.ListAsync(1, 10, null, NoFilters);

        sales.First().SaleNumber.Should().Be("SALE-B");
    }

    [Fact(DisplayName = "Listing should apply a field filter")]
    public async Task Given_FieldFilter_When_Listed_Then_FiltersResults()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-A", branchName: "Downtown");
        await fixture.GivenStoredSaleAsync("SALE-B", branchName: "Uptown");
        await fixture.GivenStoredSaleAsync("SALE-C", branchName: "Downtown");

        // A dotted path into the owned BranchReference, exactly as the documented
        // filtering convention implies.
        var filters = new Dictionary<string, string?> { ["branch.name"] = "Downtown" };
        var (sales, totalCount) = await fixture.Repository.ListAsync(1, 10, null, filters);

        sales.Should().HaveCount(2);

        // The count must reflect the filter, not the table.
        totalCount.Should().Be(2);
    }

    [Fact(DisplayName = "Listing should apply a wildcard filter")]
    public async Task Given_WildcardFilter_When_Listed_Then_MatchesPartially()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-A", customerName: "Maria Silva");
        await fixture.GivenStoredSaleAsync("SALE-B", customerName: "Maria Santos");
        await fixture.GivenStoredSaleAsync("SALE-C", customerName: "Joao Souza");

        var filters = new Dictionary<string, string?> { ["customer.name"] = "Maria*" };
        var (sales, _) = await fixture.Repository.ListAsync(1, 10, null, filters);

        sales.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Listing should apply a date range filter")]
    public async Task Given_DateRangeFilter_When_Listed_Then_AppliesBounds()
    {
        using var fixture = new SaleRepositoryFixture();
        await fixture.GivenStoredSaleAsync("SALE-A", saleDate: new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        await fixture.GivenStoredSaleAsync("SALE-B", saleDate: new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        await fixture.GivenStoredSaleAsync("SALE-C", saleDate: new DateTime(2024, 12, 15, 0, 0, 0, DateTimeKind.Utc));

        var filters = new Dictionary<string, string?>
        {
            ["_minSaleDate"] = "2024-03-01",
            ["_maxSaleDate"] = "2024-09-01"
        };
        var (sales, _) = await fixture.Repository.ListAsync(1, 10, null, filters);

        sales.Should().ContainSingle().Which.SaleNumber.Should().Be("SALE-B");
    }

    [Fact(DisplayName = "Paging should be stable across pages")]
    public async Task Given_SalesSharingASortKey_When_Paged_Then_NoRowIsRepeatedOrLost()
    {
        using var fixture = new SaleRepositoryFixture();
        var sameDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Every sale shares a sale date, so ordering by that column alone leaves the
        // order undefined and a row can drift between pages.
        for (var i = 1; i <= 6; i++)
            await fixture.GivenStoredSaleAsync($"SALE-{i:D4}", saleDate: sameDate);

        var (firstPage, _) = await fixture.Repository.ListAsync(1, 3, "saleDate desc", NoFilters);
        var (secondPage, _) = await fixture.Repository.ListAsync(2, 3, "saleDate desc", NoFilters);

        var seen = firstPage.Concat(secondPage).Select(s => s.SaleNumber).ToList();

        // The unique tiebreaker appended by the repository makes the order total, so
        // the two pages together are exactly the six distinct sales.
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(6);
    }
}
