using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using Bogus;

namespace Ambev.DeveloperEvaluation.Unit.Domain.Entities.TestData;

/// <summary>
/// Generates valid <see cref="Sale"/> data for tests.
/// </summary>
/// <remarks>
/// Follows the same approach as the template's <c>UserTestData</c>: Bogus produces
/// values that satisfy every rule, so a test that wants to prove something about
/// invalid input has to make it invalid explicitly. That keeps the reason a test
/// fails visible in the test itself rather than hidden in a shared fixture.
///
/// Quantities are drawn from 1 to 20 so generated data never trips the sale limit
/// by accident; tests about the limit pass the quantity themselves.
/// </remarks>
public static class SaleTestData
{
    private static readonly Faker Faker = new();

    /// <summary>
    /// Generates a customer reference with a real-looking name.
    /// </summary>
    public static CustomerReference GenerateCustomer() =>
        new(Guid.NewGuid(), Faker.Person.FullName);

    /// <summary>
    /// Generates a branch reference with a real-looking name.
    /// </summary>
    public static BranchReference GenerateBranch() =>
        new(Guid.NewGuid(), Faker.Company.CompanyName());

    /// <summary>
    /// Generates a product reference with a real-looking title.
    /// </summary>
    public static ProductReference GenerateProduct() =>
        new(Guid.NewGuid(), Faker.Commerce.ProductName());

    /// <summary>
    /// Generates a sale number in the format the business uses.
    /// </summary>
    public static string GenerateSaleNumber() =>
        $"SALE-{Faker.Random.Number(100000, 999999)}";

    /// <summary>
    /// Generates a unit price between 1.00 and 999.99.
    /// </summary>
    public static decimal GenerateUnitPrice() =>
        Math.Round(Faker.Random.Decimal(1m, 999.99m), 2);

    /// <summary>
    /// Creates a valid sale with no items.
    /// </summary>
    public static Sale GenerateSaleWithoutItems() =>
        Sale.Create(
            GenerateSaleNumber(),
            Faker.Date.Recent(30),
            GenerateCustomer(),
            GenerateBranch());

    /// <summary>
    /// Creates a valid sale holding the requested number of items.
    /// </summary>
    /// <param name="itemCount">How many distinct products to put on the sale.</param>
    /// <returns>The sale.</returns>
    /// <remarks>
    /// Each item gets its own product, because the aggregate refuses to hold the
    /// same product twice.
    /// </remarks>
    public static Sale GenerateSaleWithItems(int itemCount = 3)
    {
        var sale = GenerateSaleWithoutItems();

        for (var i = 0; i < itemCount; i++)
            sale.AddItem(GenerateProduct(), Faker.Random.Number(1, 20), GenerateUnitPrice());

        // The creation event is not interesting to a test that only wants a
        // populated sale, and leaving it in makes event assertions elsewhere
        // ambiguous.
        sale.ClearDomainEvents();

        return sale;
    }

    /// <summary>
    /// Creates a sale holding exactly one item with the given quantity and price.
    /// </summary>
    /// <param name="quantity">The quantity for the single item.</param>
    /// <param name="unitPrice">The unit price for the single item.</param>
    /// <returns>The sale.</returns>
    /// <remarks>
    /// The workhorse for discount and total assertions, where the test needs to know
    /// the exact numbers rather than generated ones.
    /// </remarks>
    public static Sale GenerateSaleWithSingleItem(int quantity, decimal unitPrice)
    {
        var sale = GenerateSaleWithoutItems();
        sale.AddItem(GenerateProduct(), quantity, unitPrice);
        sale.ClearDomainEvents();

        return sale;
    }

    /// <summary>
    /// Generates a draft suitable for <see cref="Sale.Update"/>.
    /// </summary>
    /// <param name="quantity">The quantity, or <c>null</c> to generate one.</param>
    /// <param name="product">The product, or <c>null</c> to generate one.</param>
    public static SaleItemDraft GenerateDraft(int? quantity = null, ProductReference? product = null) =>
        new(product ?? GenerateProduct(),
            quantity ?? Faker.Random.Number(1, 20),
            GenerateUnitPrice());
}
