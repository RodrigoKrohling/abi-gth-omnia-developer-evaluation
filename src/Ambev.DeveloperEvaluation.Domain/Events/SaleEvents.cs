namespace Ambev.DeveloperEvaluation.Domain.Events;

/// <summary>
/// Records that a new sale was registered.
/// </summary>
/// <remarks>
/// Every sale event carries the sale's identifier and number rather than the
/// <c>Sale</c> object itself. An event is an immutable statement about the past, so
/// handing out a reference to a live, mutable aggregate would let a subscriber
/// change the thing the event describes, and would mean a handler running later
/// sees a different state than the one that produced the event.
///
/// The template's existing <c>UserRegisteredEvent</c> does carry its entity; the
/// sale events do not follow that example for the reason above.
/// </remarks>
/// <param name="SaleId">The identifier of the sale.</param>
/// <param name="SaleNumber">The business-facing number of the sale.</param>
/// <param name="CustomerId">The external identifier of the customer.</param>
/// <param name="BranchId">The external identifier of the branch.</param>
/// <param name="TotalAmount">The sale total at the moment it was created.</param>
/// <param name="ItemCount">How many items the sale held.</param>
public sealed record SaleCreatedEvent(
    Guid SaleId,
    string SaleNumber,
    Guid CustomerId,
    Guid BranchId,
    decimal TotalAmount,
    int ItemCount) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// Records that an existing sale was modified.
/// </summary>
/// <remarks>
/// Raised once per update rather than once per changed field. A caller replacing
/// the item list and the branch in one request has performed one modification, and
/// emitting several events for it would misrepresent what happened.
/// </remarks>
/// <param name="SaleId">The identifier of the sale.</param>
/// <param name="SaleNumber">The business-facing number of the sale.</param>
/// <param name="TotalAmount">The sale total after the modification.</param>
/// <param name="ItemCount">How many items the sale holds after the modification.</param>
public sealed record SaleModifiedEvent(
    Guid SaleId,
    string SaleNumber,
    decimal TotalAmount,
    int ItemCount) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// Records that a whole sale was cancelled.
/// </summary>
/// <param name="SaleId">The identifier of the sale.</param>
/// <param name="SaleNumber">The business-facing number of the sale.</param>
/// <param name="TotalAmount">The total that was cancelled.</param>
public sealed record SaleCancelledEvent(
    Guid SaleId,
    string SaleNumber,
    decimal TotalAmount) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// Records that a single item was cancelled while the sale itself remains active.
/// </summary>
/// <remarks>
/// Distinct from <see cref="SaleCancelledEvent"/> because the two mean different
/// things downstream: cancelling one item releases the stock for that product and
/// reduces the sale total, while cancelling the sale voids the whole transaction.
/// </remarks>
/// <param name="SaleId">The identifier of the sale the item belongs to.</param>
/// <param name="SaleNumber">The business-facing number of the sale.</param>
/// <param name="SaleItemId">The identifier of the cancelled item.</param>
/// <param name="ProductId">The external identifier of the product on that item.</param>
/// <param name="Quantity">The quantity that was cancelled.</param>
/// <param name="SaleTotalAmount">The sale total after the item was removed from it.</param>
public sealed record ItemCancelledEvent(
    Guid SaleId,
    string SaleNumber,
    Guid SaleItemId,
    Guid ProductId,
    int Quantity,
    decimal SaleTotalAmount) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
