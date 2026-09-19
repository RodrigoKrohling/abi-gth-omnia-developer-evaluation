namespace Ambev.DeveloperEvaluation.Domain.ValueObjects;

/// <summary>
/// Identifies the branch where a sale was made, together with a denormalized copy
/// of its name.
/// </summary>
/// <remarks>
/// The same External Identities reasoning as <see cref="CustomerReference"/>
/// applies: the branch is owned by another bounded context, so the sale stores its
/// identifier plus the description it needs to display, rather than a foreign key
/// and a navigation property.
///
/// The name is a snapshot taken when the sale was made and is not refreshed. A
/// branch that is later renamed or closed does not rewrite the history of the
/// sales made there.
///
/// Persisted as an EF Core owned type, so both values live in columns on the Sales
/// table.
/// </remarks>
public sealed record BranchReference
{
    /// <summary>
    /// Gets the branch's identifier in the branch domain.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the branch's name as it was at the time of the sale.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BranchReference"/> class.
    /// </summary>
    /// <param name="id">The branch's identifier in the branch domain.</param>
    /// <param name="name">The branch's name at the time of the sale.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the identifier is empty or the name is blank.
    /// </exception>
    public BranchReference(Guid id, string name)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The branch identifier is required.", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The branch name is required.", nameof(name));

        Id = id;
        Name = name.Trim();
    }

    /// <summary>
    /// Parameterless constructor reserved for Entity Framework Core materialization.
    /// </summary>
    private BranchReference()
    {
        Name = string.Empty;
    }
}
