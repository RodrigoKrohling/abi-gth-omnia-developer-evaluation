using Ambev.DeveloperEvaluation.Domain.Entities;
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
