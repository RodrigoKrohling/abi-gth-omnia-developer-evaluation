using Ambev.DeveloperEvaluation.Domain.Common;
using Ambev.DeveloperEvaluation.Domain.Entities;
using Ambev.DeveloperEvaluation.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Ambev.DeveloperEvaluation.ORM;

/// <summary>
/// The application's Entity Framework Core database context (PostgreSQL).
/// </summary>
/// <remarks>
/// This context owns every persistent aggregate in the system. Entity-to-table
/// mapping is deliberately kept out of this class: each aggregate has its own
/// <see cref="IEntityTypeConfiguration{TEntity}"/> under the Mapping folder, and
/// <see cref="OnModelCreating"/> discovers them all by assembly scan. Adding a new
/// aggregate therefore means adding a <c>DbSet</c> here and a configuration class
/// there, with no central mapping file to edit.
/// </remarks>
public class DefaultContext : DbContext
{
    /// <summary>
    /// Gets the users of the system.
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// Gets the sales records.
    /// </summary>
    /// <remarks>
    /// There is deliberately no <c>DbSet&lt;SaleItem&gt;</c>. Items are reachable only
    /// through their sale, which is what stops a caller from loading or modifying one
    /// outside the aggregate and stepping around the rules <see cref="Sale"/>
    /// enforces.
    /// </remarks>
    public DbSet<Sale> Sales { get; set; }

    /// <summary>
    /// Publishes domain events once a save has succeeded.
    /// </summary>
    /// <remarks>
    /// Optional so that the design-time factory and any test constructing the context
    /// directly do not have to supply one. When absent, events are simply discarded
    /// along with the rest of the in-memory state.
    /// </remarks>
    private readonly IDomainEventPublisher? _domainEventPublisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The options used to configure the context, supplied by dependency injection
    /// from <c>Program.cs</c> (runtime) or by <see cref="DefaultContextFactory"/>
    /// (design time).
    /// </param>
    public DefaultContext(DbContextOptions<DefaultContext> options) : base(options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultContext"/> class that
    /// publishes domain events after each successful save.
    /// </summary>
    /// <param name="options">The options used to configure the context.</param>
    /// <param name="domainEventPublisher">The publisher to hand drained events to.</param>
    public DefaultContext(
        DbContextOptions<DefaultContext> options,
        IDomainEventPublisher domainEventPublisher) : base(options)
    {
        _domainEventPublisher = domainEventPublisher;
    }

    /// <summary>
    /// Saves pending changes and then publishes any domain events the saved
    /// aggregates raised.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    /// <remarks>
    /// <para>
    /// The order matters and is the whole point of dispatching here. Events are
    /// collected before the save, because saving clears the change tracker's view of
    /// what was modified, but they are published only after it succeeds. An operation
    /// that throws and rolls back therefore announces nothing, so no subscriber ever
    /// reacts to a change that did not happen.
    /// </para>
    /// <para>
    /// The events are cleared from their aggregates before publishing, so a second
    /// save in the same unit of work cannot emit them twice.
    /// </para>
    /// <para>
    /// This is deliberately not a distributed transaction. The database commit and
    /// the publish are two separate operations, and a crash between them loses the
    /// announcement. Closing that gap properly calls for an outbox - events written
    /// to a table inside the same transaction and relayed by a separate process -
    /// which is more machinery than an evaluation prototype needs, but is the reason
    /// the publisher sits behind an interface rather than being called inline.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot the aggregates carrying events before saving. ChangeTracker
        // entries are re-evaluated by SaveChanges, so reading them afterwards is not
        // reliable.
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        // Past this line the change is durable, so announcing it is honest.
        if (_domainEventPublisher is not null && domainEvents.Count > 0)
        {
            foreach (var aggregate in aggregates)
                aggregate.ClearDomainEvents();

            await _domainEventPublisher.PublishAsync(domainEvents, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Builds the model by applying every <see cref="IEntityTypeConfiguration{TEntity}"/>
    /// found in this assembly.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Scans this assembly for mapping classes (Mapping/UserConfiguration.cs and
        // friends) so that new aggregates are picked up without touching this method.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Creates a <see cref="DefaultContext"/> for design-time tooling.
/// </summary>
/// <remarks>
/// The EF Core CLI (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>)
/// cannot start the web host, so it looks for an
/// <see cref="IDesignTimeDbContextFactory{TContext}"/> to build the context instead.
/// This factory reads the same <c>appsettings.json</c> connection string the
/// application uses, so migrations are always generated against the real provider.
/// </remarks>
public class DefaultContextFactory : IDesignTimeDbContextFactory<DefaultContext>
{
    /// <summary>
    /// Builds a <see cref="DefaultContext"/> for the EF Core command-line tools.
    /// </summary>
    /// <param name="args">Arguments forwarded by the EF Core tooling; not used.</param>
    /// <returns>A context configured against the PostgreSQL connection string.</returns>
    public DefaultContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var builder = new DbContextOptionsBuilder<DefaultContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        builder.UseNpgsql(
               connectionString,
               // The migrations live in this project (Migrations/), so the design-time
               // factory must name this assembly. It previously named the WebApi
               // assembly, which disagreed with the runtime registration in Program.cs
               // and made the EF tooling unable to find the existing migrations.
               b => b.MigrationsAssembly("Ambev.DeveloperEvaluation.ORM")
        );

        return new DefaultContext(builder.Options);
    }
}
