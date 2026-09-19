namespace Ambev.DeveloperEvaluation.Domain.ValueObjects;

/// <summary>
/// Identifies a customer that lives in another domain, together with a
/// denormalized copy of its name.
/// </summary>
/// <remarks>
/// This is the <c>External Identities</c> pattern the project brief asks for:
///
/// <para>
/// "As we work with DDD, to reference entities from other domains, we use the
/// External Identities pattern with denormalization of entity descriptions."
/// </para>
///
/// The customer belongs to a different bounded context. Modelling it as a foreign
/// key with a navigation property would couple the two contexts, force a join on
/// every read, and make the sales service unable to answer a query when the
/// customer service is unavailable.
///
/// Instead the sale stores the customer's identifier plus the description it needs
/// to display. The identifier is how the two contexts agree on who the customer is;
/// the name is a copy taken at the time of sale.
///
/// That copy is intentionally not kept in sync. A sale is a historical record: if a
/// customer later changes their name, the sale should still show the name under
/// which the purchase was actually made. Denormalization here is the point, not an
/// optimisation.
///
/// Persisted as an EF Core owned type, so both values live in columns on the Sales
/// table rather than in a table of their own.
/// </remarks>
public sealed record CustomerReference
{
    /// <summary>
    /// Gets the customer's identifier in the customer domain.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the customer's name as it was at the time of the sale.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomerReference"/> class.
    /// </summary>
    /// <param name="id">The customer's identifier in the customer domain.</param>
    /// <param name="name">The customer's name at the time of the sale.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the identifier is empty or the name is blank. A reference that
    /// identifies nobody, or that cannot be displayed, is not a usable reference,
    /// so it is rejected at construction rather than allowed to reach the database.
    /// </exception>
    public CustomerReference(Guid id, string name)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The customer identifier is required.", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The customer name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
    }

    /// <summary>
    /// Parameterless constructor reserved for Entity Framework Core materialization.
    /// </summary>
    /// <remarks>
    /// EF needs a way to construct the instance before populating it from the
    /// database. It is private so that application code cannot bypass the
    /// validation in the public constructor.
    /// </remarks>
    private CustomerReference()
    {
        Name = string.Empty;
    }
}
