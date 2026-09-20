using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.WebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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

        // Task.Run pushes the work off whatever synchronization context the test
        // runner is on before it is waited on. Blocking directly on the task from a
        // captured context is the classic way to deadlock a TestServer call.
        _token = new Lazy<string>(() =>
            Task.Run(AcquireTokenAsync).GetAwaiter().GetResult());
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
            // then throws while resolving the context. Only the generic
            // DbContextOptions<DefaultContext> may be removed: taking the non-generic
            // one or the context itself breaks resolution outright.
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

    /// <summary>
    /// A bearer token for a user registered on first use, acquired once per factory.
    /// </summary>
    /// <remarks>
    /// Lazy because the host has to exist before a request can be made, and cached
    /// because the token is valid for hours - re-registering and re-authenticating
    /// for every test would add two round trips each and prove nothing.
    /// </remarks>
    private readonly Lazy<string> _token;

    /// <summary>
    /// Creates a client that carries a valid bearer token.
    /// </summary>
    /// <returns>An authenticated client for the in-memory API.</returns>
    /// <remarks>
    /// Most tests are about a feature's behaviour, not about authentication, so they
    /// take this client and say nothing further about tokens. The tests that assert
    /// what happens <i>without</i> a token use <see cref="WebApplicationFactory{T}.CreateClient"/>
    /// directly.
    /// </remarks>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _token.Value);

        return client;
    }

    /// <summary>
    /// Registers a user and exchanges the credentials for a token.
    /// </summary>
    /// <remarks>
    /// Goes through the real endpoints rather than minting a token directly or
    /// seeding the table. If registration or authentication were to break, these
    /// tests should fail rather than quietly carry on with a token the application
    /// itself could never have issued.
    /// </remarks>
    private async Task<string> AcquireTokenAsync()
    {
        const string email = "functional-suite@example.com";
        const string password = "Str0ng!Pass1";

        using var client = CreateClient();

        var registration = await client.PostAsJsonAsync("/api/users", new
        {
            username = "functionalsuite",
            email,
            phone = "+5511988887777",
            password,
            status = "Active",
            role = "Customer"
        });

        registration.EnsureSuccessStatusCode();

        var authentication = await client.PostAsJsonAsync("/api/auth", new { email, password });
        authentication.EnsureSuccessStatusCode();

        var body = await authentication.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("data").GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Authentication returned no token.");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }
}
