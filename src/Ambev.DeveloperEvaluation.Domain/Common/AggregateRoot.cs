using Ambev.DeveloperEvaluation.Domain.Events;

namespace Ambev.DeveloperEvaluation.Domain.Common;

/// <summary>
/// Base class for the entity that owns a consistency boundary and records the
/// domain events raised inside it.
/// </summary>
/// <remarks>
/// An aggregate root is the only entity in its cluster that the outside world may
/// hold a reference to. For the sale cluster that is <see cref="Entities.Sale"/>:
/// nothing outside loads or modifies a <see cref="Entities.SaleItem"/> directly,
/// which is what lets the sale guarantee rules that span several items, such as the
/// twenty-item cap and the recalculated total.
///
/// This type extends <see cref="BaseEntity"/> rather than changing it, because
/// <c>User</c> already derives from <see cref="BaseEntity"/> and has no need of
/// domain events. Adding the event list there would have imposed it on every
/// entity in the system.
///
/// Events are accumulated in memory and deliberately not published as they are
/// raised. The infrastructure drains them after <c>SaveChangesAsync</c> succeeds,
/// so an operation that fails and rolls back never announces a change that was
/// not persisted.
/// </remarks>
public abstract class AggregateRoot : BaseEntity
{
    /// <summary>
    /// Backing store for events raised since the aggregate was loaded or created.
    /// </summary>
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the domain events raised by this aggregate and not yet published.
    /// </summary>
    /// <remarks>
    /// Exposed as a read-only view so that callers can inspect the events, which
    /// unit tests rely on, without being able to add or remove entries. The only
    /// supported ways to change the list are <see cref="RaiseEvent"/> from inside
    /// the aggregate and <see cref="ClearDomainEvents"/> from the dispatcher.
    /// </remarks>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Records that something happened inside this aggregate.
    /// </summary>
    /// <param name="domainEvent">The event to record.</param>
    /// <remarks>
    /// Protected because only the aggregate itself may decide that one of its
    /// invariants produced an event; letting outside code raise events on another
    /// object's behalf would make the event stream untrustworthy.
    /// </remarks>
    protected void RaiseEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Discards the recorded events.
    /// </summary>
    /// <remarks>
    /// Called by the dispatcher once the events have been handed to the publisher,
    /// so that a second call to <c>SaveChangesAsync</c> in the same unit of work
    /// does not publish them again.
    /// </remarks>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
