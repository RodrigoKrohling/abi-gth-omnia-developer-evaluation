using Ambev.DeveloperEvaluation.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ambev.DeveloperEvaluation.ORM.Mapping;

/// <summary>
/// Maps <see cref="SaleItem"/> to the SaleItems table.
/// </summary>
/// <remarks>
/// A sale item is an entity with its own table rather than an owned collection on
/// the sale, because it has an identity the outside world names: cancelling an item
/// addresses it by id through
/// <c>PATCH /api/sales/{id}/items/{itemId}/cancel</c>.
///
/// It is still not an aggregate root. There is no <c>DbSet&lt;SaleItem&gt;</c> on the
/// context, so nothing can query items independently of their sale, which is what
/// keeps the sale's invariants enforceable.
/// </remarks>
public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    /// <summary>
    /// Configures the <see cref="SaleItem"/> entity.
    /// </summary>
    /// <param name="builder">The builder for the SaleItem entity type.</param>
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems");

        builder.HasKey(i => i.Id);

        // Assigned by the domain in SaleItem.Create, not by the database. See the
        // note in SaleConfiguration: without this, an item added to an already
        // tracked sale is mistaken for an existing row and updated instead of
        // inserted.
        builder.Property(i => i.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(i => i.SaleId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(i => i.Quantity).IsRequired();

        // Every money column is fixed-point at two decimal places, matching the
        // rounding the domain applies when it calculates them.
        builder.Property(i => i.UnitPrice).IsRequired().HasPrecision(18, 2);
        builder.Property(i => i.Discount).IsRequired().HasPrecision(18, 2);
        builder.Property(i => i.TotalAmount).IsRequired().HasPrecision(18, 2);

        // The rate is a fraction such as 0.10 or 0.20. Four decimal places leaves
        // room for a future tier expressed in basis points without a migration.
        builder.Property(i => i.DiscountRate).IsRequired().HasPrecision(5, 4);

        builder.Property(i => i.IsCancelled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(i => i.CancelledAt);

        // -- External identity --------------------------------------------------
        // The product reference becomes two columns on this table. Storing the title
        // on the item is what lets a sale show what was actually sold even after the
        // catalogue renames the product.
        //
        // ComplexProperty rather than OwnsOne, for the same reason as Customer and
        // Branch on Sale: an owned type carries its own identity keyed by the owner,
        // so refreshing the denormalized title would be treated as deleting one
        // dependent and inserting another at the same key. A complex property has no
        // identity, so it is simply part of the item.
        builder.ComplexProperty(i => i.Product, product =>
        {
            product.IsRequired();

            product.Property(p => p.Id)
                .HasColumnName("ProductId")
                .HasColumnType("uuid")
                .IsRequired();

            product.Property(p => p.Title)
                .HasColumnName("ProductTitle")
                .HasMaxLength(200)
                .IsRequired();
        });

        // An index on ProductId is not declared here, for the same reason as the
        // customer and branch indexes on Sale: EF Core 8 cannot build one over a
        // complex property's columns. It is created in the AddSaleAggregate migration.

        // Loading a sale always loads its items, so the foreign key is indexed.
        builder.HasIndex(i => i.SaleId).HasDatabaseName("IX_SaleItems_SaleId");

        // A unique index on (SaleId, ProductId) would back the aggregate's
        // one-line-per-product rule with a database constraint. It is not declared
        // here because ProductId belongs to the owned ProductReference rather than to
        // SaleItem, and EF Core cannot compose an index across that boundary: the
        // owned type sees only its own properties plus a shadow key back to the item.
        //
        // The rule is therefore enforced in the aggregate alone - Sale.AddItem
        // refuses a product already present, and Sale.Update rejects a duplicated
        // draft - which covers every path the application actually takes. The gap is
        // two concurrent requests adding the same product to the same sale, each
        // reading before the other writes. Closing that properly calls for an
        // optimistic concurrency token on the sale rather than an index on the item,
        // since it is one instance of a general problem with concurrent edits to an
        // aggregate.
    }
}
