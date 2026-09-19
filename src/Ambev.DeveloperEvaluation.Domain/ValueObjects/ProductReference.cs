namespace Ambev.DeveloperEvaluation.Domain.ValueObjects;

/// <summary>
/// Identifies the product sold on a sale item, together with a denormalized copy of
/// its title.
/// </summary>
/// <remarks>
/// The same External Identities reasoning as <see cref="CustomerReference"/>
/// applies. The property is named <c>Title</c> rather than <c>Name</c> to match the
/// field name the Products API uses in <c>.doc/products-api.md</c>.
///
/// The denormalized title matters most here. A product's title and price change
/// over time, and a sale must keep showing what was actually sold and what was
/// actually charged. Resolving the title through a join at read time would make
/// every historical receipt silently change whenever the catalogue was edited.
///
/// The unit price is deliberately not part of this type: it belongs to
/// <see cref="Entities.SaleItem"/>, because it is a property of this particular
/// line on this particular sale rather than of the product reference itself.
///
/// Persisted as an EF Core owned type, so both values live in columns on the
/// SaleItems table.
/// </remarks>
public sealed record ProductReference
{
    /// <summary>
    /// Gets the product's identifier in the product domain.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the product's title as it was at the time of the sale.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductReference"/> class.
    /// </summary>
    /// <param name="id">The product's identifier in the product domain.</param>
    /// <param name="title">The product's title at the time of the sale.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the identifier is empty or the title is blank.
    /// </exception>
    public ProductReference(Guid id, string title)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The product identifier is required.", nameof(id));

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("The product title is required.", nameof(title));

        Id = id;
        Title = title.Trim();
    }

    /// <summary>
    /// Parameterless constructor reserved for Entity Framework Core materialization.
    /// </summary>
    private ProductReference()
    {
        Title = string.Empty;
    }
}
