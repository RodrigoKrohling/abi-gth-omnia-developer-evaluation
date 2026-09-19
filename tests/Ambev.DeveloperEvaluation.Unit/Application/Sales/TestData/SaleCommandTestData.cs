using Ambev.DeveloperEvaluation.Application.Sales.Common;
using Ambev.DeveloperEvaluation.Application.Sales.CreateSale;
using Ambev.DeveloperEvaluation.Application.Sales.UpdateSale;
using AutoMapper;
using Bogus;

namespace Ambev.DeveloperEvaluation.Unit.Application.Sales.TestData;

/// <summary>
/// Generates valid commands for the sale handler tests.
/// </summary>
/// <remarks>
/// Every generator produces a command that passes validation, so a test asserting a
/// rejection has to break something explicitly. That keeps the reason a test fails
/// visible in the test rather than buried in shared setup.
/// </remarks>
public static class SaleCommandTestData
{
    private static readonly Faker Faker = new();

    /// <summary>
    /// Builds a real AutoMapper instance from the production profile.
    /// </summary>
    /// <remarks>
    /// The handler tests substitute the repository but use the genuine mapper. A
    /// substituted <c>IMapper</c> returns nulls unless every call is stubbed, which
    /// makes the tests assert on the stubs rather than on the handler, and would hide
    /// a broken mapping entirely. Building the real configuration also fails the test
    /// suite if <see cref="SaleProfile"/> is ever misconfigured.
    /// </remarks>
    public static IMapper CreateMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<SaleProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

    /// <summary>
    /// Generates a valid create command with the requested number of items.
    /// </summary>
    /// <param name="itemCount">How many distinct products to put on the sale.</param>
    /// <param name="quantity">The quantity for every item, or null to generate one.</param>
    public static CreateSaleCommand GenerateCreateCommand(int itemCount = 1, int? quantity = null) =>
        new()
        {
            SaleNumber = $"SALE-{Faker.Random.Number(100000, 999999)}",
            SaleDate = Faker.Date.Recent(30),
            CustomerId = Guid.NewGuid(),
            CustomerName = Faker.Person.FullName,
            BranchId = Guid.NewGuid(),
            BranchName = Faker.Company.CompanyName(),
            Items = Enumerable.Range(0, itemCount)
                .Select(_ => new CreateSaleItemCommand
                {
                    ProductId = Guid.NewGuid(),
                    ProductTitle = Faker.Commerce.ProductName(),
                    Quantity = quantity ?? Faker.Random.Number(1, 20),
                    UnitPrice = Math.Round(Faker.Random.Decimal(1m, 500m), 2)
                })
                .ToList()
        };

    /// <summary>
    /// Generates a valid update command for the given sale.
    /// </summary>
    /// <param name="saleId">The sale to update.</param>
    /// <param name="itemCount">How many distinct products the sale should end up with.</param>
    public static UpdateSaleCommand GenerateUpdateCommand(Guid saleId, int itemCount = 1) =>
        new()
        {
            Id = saleId,
            SaleDate = Faker.Date.Recent(30),
            CustomerId = Guid.NewGuid(),
            CustomerName = Faker.Person.FullName,
            BranchId = Guid.NewGuid(),
            BranchName = Faker.Company.CompanyName(),
            Items = Enumerable.Range(0, itemCount)
                .Select(_ => new UpdateSaleItemCommand
                {
                    ProductId = Guid.NewGuid(),
                    ProductTitle = Faker.Commerce.ProductName(),
                    Quantity = Faker.Random.Number(1, 20),
                    UnitPrice = Math.Round(Faker.Random.Decimal(1m, 500m), 2)
                })
                .ToList()
        };
}

/// <summary>
/// A logger factory that produces loggers which discard everything.
/// </summary>
/// <remarks>
/// AutoMapper 15 requires an <c>ILoggerFactory</c> when constructing a
/// <c>MapperConfiguration</c>. The tests have nothing to do with logging, so this
/// supplies the dependency without pulling in a logging package.
/// </remarks>
internal sealed class NullLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
{
    public static readonly NullLoggerFactory Instance = new();

    public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => NullLogger.Instance;

    public void Dispose() { }

    private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }
    }
}
