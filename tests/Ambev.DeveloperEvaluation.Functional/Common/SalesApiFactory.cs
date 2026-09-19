using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.WebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ambev.DeveloperEvaluation.Functional.Common;

/// <summary>
/// Hosts the real API in memory for functional tests, backed by SQLite instead of
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This starts the genuine application: the real <c>Program</c>, the real middleware
/// pipeline, the real controllers, MediatR, AutoMapper, FluentValidation and the real
/// repository. Only the database provider is swapped. So these tests exercise model
/// binding, routing, the exception middleware's status codes and error bodies, and
/// the JSON actually sent over the wire - none of which unit tests can reach.
/// </para>
/// <para>
/// The provider swap is the one concession to portability: PostgreSQL would need a
/// running container, and Docker is not available in every environment a reviewer
/// might use. SQLite is still a relational database, so foreign keys, cascades and
/// SQL translation are genuinely exercised.
/// </para>
/// <para>
/// One connection is held open for the factory's lifetime, because a SQLite
/// in-memory database exists only as long as a connection to it does.
/// </para>
/// </remarks>
public class SalesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// Initializes a new factory over a fresh, private in-memory database.
    /// </summary>
    public SalesApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testing rather than Development, so Program skips Database.Migrate() and
        // Swagger. The migrations carry PostgreSQL-specific SQL and cannot run on
        // SQLite; the schema is created from the model in CreateHost instead.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Program registered DefaultContext against Npgsql. Removing the options
            // descriptor is what actually unregisters that provider - calling
            // AddDbContext again without this leaves two providers configured, and EF
            // then throws while resolving the context.
            //
            // Only the generic DbContextOptions<DefaultContext> is removed. An earlier
            // version also removed the non-generic DbContextOptions and the context
            // itself, which broke resolution outright and made every request in the
            // suite fail identically.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<DefaultContext>));

            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<DefaultContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// The schema is created here, on the finished host, rather than inside
    /// <c>ConfigureServices</c>. Calling <c>BuildServiceProvider</c> there would build
    /// a second, throwaway container from a half-configured collection, which
    /// resolves different singletons than the application ends up using.
    /// </remarks>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DefaultContext>();
        context.Database.EnsureCreated();

        return host;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }
}
