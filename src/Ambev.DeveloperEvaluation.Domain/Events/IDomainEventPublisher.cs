namespace Ambev.DeveloperEvaluation.Domain.Events;

/// <summary>
/// Announces domain events to whatever the application has been configured to do
/// with them.
/// </summary>
/// <remarks>
/// Declared in the domain but implemented in the infrastructure, so the dependency
/// points inwards: the aggregate raises events without knowing whether they end up
/// in a log, a message broker or a database.
///
/// The interface is deliberately free of any messaging library. The brief notes that
/// events need not reach a real broker - "you can log a message in the application
/// log or however you find most convenient" - and this is the seam that makes that a
/// configuration choice rather than a code change. Swapping the logging publisher for
/// one that writes to Rebus or SQS means registering a different implementation and
/// touching nothing else.
///
/// Implementations must not throw. Publishing happens after the business transaction
/// has already committed, so a failure to announce a change must not be reported to
/// the caller as a failure to make it.
/// </remarks>
public interface IDomainEventPublisher
{
    /// <summary>
    /// Publishes a batch of events that have already been persisted.
    /// </summary>
    /// <param name="domainEvents">The events to announce.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
