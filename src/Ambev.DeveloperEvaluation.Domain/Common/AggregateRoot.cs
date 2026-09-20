using Ambev.DeveloperEvaluation.Domain.Events;

namespace Ambev.DeveloperEvaluation.Domain.Common;

/// <summary>
/// Base class for the entity that owns a consistency boundary and records the
/// domain events raised inside it.
/// </summary>
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
