using Ambev.DeveloperEvaluation.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ambev.DeveloperEvaluation.ORM.Mapping;

/// <summary>
/// Maps the <see cref="Sale"/> aggregate root to the Sales table.
/// </summary>
/// <remarks>
/// Discovered automatically by <c>DefaultContext.OnModelCreating</c>, which scans
/// this assembly for <see cref="IEntityTypeConfiguration{TEntity}"/> implementations.
///
/// Keeping the mapping here rather than in attributes on the entity is what allows
/// <see cref="Sale"/> to stay a plain domain object with no persistence concerns:
/// the domain project references FluentValidation and nothing else.
/// </remarks>
public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    /// <summary>
    /// Configures the <see cref="Sale"/> entity.
    /// </summary>
    /// <param name="builder">The builder for the Sale entity type.</param>
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales");

        builder.HasKey(s => s.Id);

        // The domain assigns the identifier in Sale.Create, not the database.
        //
        // This is not cosmetic. EF's convention for a Guid key is
        // ValueGeneratedOnAdd, and it uses that to decide whether a newly discovered
        // entity is an insert or an update: a non-default key value means "already
        // exists". Since Sale.Create fills the key with Guid.NewGuid(), a new sale
        // added to a tracked graph was classified as Modified and EF issued an UPDATE
        // against a row that did not exist, failing with "expected to affect 1 row(s),
        // but actually affected 0 row(s)".
        //
        // Declaring the key as never generated is also the correct model: an
        // aggregate owns its identity from the moment it is constructed, well before
        // anything is persisted.
        builder.Property(s => s.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(s => s.SaleNumber)
            .IsRequired()
            .HasMaxLength(50);

        // The sale number is what a customer quotes and what the business treats as
        // the sale's identity, so two sales must never share one. Enforced in the
        // database as well as in the handler, because a uniqueness check in
        // application code loses to a race between two concurrent requests.
        builder.HasIndex(s => s.SaleNumber)
            .IsUnique()
            .HasDatabaseName("IX_Sales_SaleNumber");

        builder.Property(s => s.SaleDate).IsRequired();

        // Money columns are fixed-point. The PostgreSQL default for an unqualified
        // numeric is arbitrary precision, and mapping decimal without saying so on
        // other providers silently truncates to 2 decimal places or fewer.
        builder.Property(s => s.TotalAmount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(s => s.IsCancelled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.CancelledAt);

        // Filtering and ordering by date is the most common list query, and
        // cancelled sales are routinely excluded from reports.
        builder.HasIndex(s => s.SaleDate).HasDatabaseName("IX_Sales_SaleDate");
        builder.HasIndex(s => s.IsCancelled).HasDatabaseName("IX_Sales_IsCancelled");

        // -- External identities ----------------------------------------------
        // Customer and branch are mapped as complex properties, so their two fields
        // each become columns on the Sales table rather than a separate table with a
        // join. That is the whole point of denormalizing the description: reading a
        // sale must not require the customer service to be reachable.
        //
        // ComplexProperty rather than OwnsOne, deliberately. An owned type is still
        // an entity to EF: it has its own identity, keyed by the owner. Assigning a
        // new CustomerReference to a tracked sale therefore made EF mark the old
        // instance Deleted and the new one Added at the same key, and the conflicting
        // statements failed with "expected to affect 1 row(s), but actually affected
        // 0 row(s)" on every relational provider.
        //
        // A complex property has no identity. It is part of the sale, exactly as a
        // value object should be, so replacing it is an ordinary property change and
        // EF emits a plain column update. This is the modelling primitive EF Core 8
        // introduced for precisely this case.
        builder.ComplexProperty(s => s.Customer, customer =>
        {
            customer.IsRequired();

            customer.Property(c => c.Id)
                .HasColumnName("CustomerId")
                .HasColumnType("uuid")
                .IsRequired();

            customer.Property(c => c.Name)
                .HasColumnName("CustomerName")
                .HasMaxLength(100)
                .IsRequired();
        });

        builder.ComplexProperty(s => s.Branch, branch =>
        {
            branch.IsRequired();

            branch.Property(b => b.Id)
                .HasColumnName("BranchId")
                .HasColumnType("uuid")
                .IsRequired();

            branch.Property(b => b.Name)
                .HasColumnName("BranchName")
                .HasMaxLength(100)
                .IsRequired();
        });

        // Indexes on CustomerId and BranchId are not declared here. EF Core 8 cannot
        // build an index over a complex property's columns: HasIndex takes entity
        // properties, and to the model these columns belong to the complex type
        // rather than to Sale. They are created directly in the AddSaleAggregate
        // migration instead, where only the table and column names matter.

        // -- Items ------------------------------------------------------------
        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey(i => i.SaleId)
            // Items have no life outside their sale, so deleting the sale must take
            // them with it rather than leaving orphan rows.
            .OnDelete(DeleteBehavior.Cascade);

        // Sale.Items is a read-only view over the private _items list, so reading it
        // returns a fresh wrapper and writing to it is impossible. EF must therefore
        // go through the backing field to populate the collection; without this it
        // would try to add to the read-only wrapper and fail at materialization.
        builder.Metadata
            .FindNavigation(nameof(Sale.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Domain events are raised in memory and published after the transaction
        // commits. They are not state, and must not become a column.
        builder.Ignore(s => s.DomainEvents);
    }
}
