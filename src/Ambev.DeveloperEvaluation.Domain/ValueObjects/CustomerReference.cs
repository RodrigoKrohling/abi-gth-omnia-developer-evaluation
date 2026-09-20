namespace Ambev.DeveloperEvaluation.Domain.ValueObjects;

/// <summary>
/// Identifies a customer that lives in another domain, together with a
/// denormalized copy of its name.
/// </summary>
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
    private CustomerReference()
    {
        Name = string.Empty;
    }
}
