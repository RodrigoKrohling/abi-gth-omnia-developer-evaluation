using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.ValueObjects;
using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.ORM.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ambev.DeveloperEvaluation.Integration.Common;

/// <summary>
/// Builds a <see cref="DefaultContext"/> and a <see cref="SaleRepository"/> over an
/// isolated SQLite in-memory database for a single test.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why SQLite rather than the EF in-memory provider.</b> SQLite is a real
/// relational database, so these tests exercise actual SQL generation, foreign keys,
/// cascade deletes and unique indexes. The EF in-memory provider is not a database
/// at all - it is a dictionary with a LINQ front end - and it silently accepts
/// models and operations that no relational provider would. It also cannot replace
/// an owned entity instance on a tracked parent, which is exactly what updating a
/// sale's customer does, so it failed on a model that is in fact correct.
/// </para>
/// <para>
/// <b>Why not Testcontainers.</b> A real PostgreSQL instance would additionally
/// prove that the Npgsql-specific column types and translations behave, but it needs
/// a Docker daemon, which is not available in every environment a reviewer might
/// use. SQLite keeps the suite runnable anywhere with <c>dotnet test</c> alone while
/// still being relational.
/// </para>
/// <para>
/// <b>What this does not prove.</b> SQLite is not PostgreSQL. It stores
/// <c>decimal</c> as a floating point value rather than as fixed-point
/// <c>numeric(18,2)</c>, and it does not implement every function Npgsql can
/// translate. So these tests cover repository behaviour, model configuration and
/// relational semantics, not PostgreSQL-specific SQL. The verification steps in the
/// README cover the real provider by running the stack.
/// </para>
/// <para>
/// Each instance opens its own private connection, so tests are isolated and can run
/// in parallel without seeing one another's rows.
/// </para>
/// </remarks>
public sealed class SaleRepositoryFixture : IDisposable
{
    /// <summary>
    /// The open connection that owns the in-memory database.
    /// </summary>
    /// <remarks>
    /// A SQLite in-memory database lives exactly as long as its connection is open,
    /// so this is held for the lifetime of the fixture. Letting it close between
    /// operations would discard the schema and every row.
    /// </remarks>
    private readonly SqliteConnection _connection;

    /// <summary>
    /// Gets the context backing this fixture.
    /// </summary>
    public DefaultContext Context { get; }

    /// <summary>
    /// Gets the repository under test.
    /// </summary>
    public SaleRepository Repository { get; }

    /// <summary>
    /// Initializes a new fixture over a fresh, private in-memory database.
    /// </summary>
    public SaleRepositoryFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DefaultContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new DefaultContext(options);

        // Builds the schema from the model rather than from the migrations, because
        // the migrations carry PostgreSQL-specific SQL. What is under test here is
        // the model, which both providers share.
        Context.Database.EnsureCreated();

        Repository = new SaleRepository(Context);
    }

    /// <summary>
    /// Creates a sale with a single item and stores it.
    /// </summary>
    /// <param name="saleNumber">The sale number to use.</param>
    /// <param name="customerName">The customer name to record.</param>
    /// <param name="branchName">The branch name to record.</param>
    /// <param name="saleDate">The sale date, defaulting to a fixed date.</param>
    /// <param name="quantity">The quantity for the single item.</param>
    /// <param name="unitPrice">The unit price for the single item.</param>
    /// <returns>The stored sale.</returns>
    public async Task<Sale> GivenStoredSaleAsync(
        string saleNumber,
        string customerName = "Maria Silva",
        string branchName = "Downtown",
        DateTime? saleDate = null,
        int quantity = 5,
        decimal unitPrice = 100m)
    {
        var sale = Sale.Create(
            saleNumber,
            saleDate ?? new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new CustomerReference(Guid.NewGuid(), customerName),
            new BranchReference(Guid.NewGuid(), branchName));

        sale.AddItem(new ProductReference(Guid.NewGuid(), "Backpack"), quantity, unitPrice);

        await Repository.CreateAsync(sale);

        // Detach everything so a subsequent read genuinely round-trips through the
        // model instead of returning the instance still held by the change tracker.
        Context.ChangeTracker.Clear();

        return sale;
    }

    /// <summary>
    /// Releases the context and the connection, which discards the database.
    /// </summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
