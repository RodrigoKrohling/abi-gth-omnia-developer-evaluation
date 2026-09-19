using Ambev.DeveloperEvaluation.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Ambev.DeveloperEvaluation.ORM.Events;

/// <summary>
/// Fans a batch of domain events out to several publishers.
/// </summary>
/// <remarks>
/// Lets the application log events and archive them to MongoDB without either
/// publisher knowing about the other, and without <c>DefaultContext</c> holding a
/// list of sinks. Adding a broker later means registering one more publisher.
///
/// Each publisher is isolated: one that throws does not stop the rest from running.
/// A publisher is supposed to handle its own failures, but a composite that let the
/// first exception abort the loop would make the order of registration silently
/// significant.
/// </remarks>
public class CompositeDomainEventPublisher : IDomainEventPublisher
{
    private readonly IEnumerable<IDomainEventPublisher> _publishers;
    private readonly ILogger<CompositeDomainEventPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeDomainEventPublisher"/> class.
    /// </summary>
    /// <param name="publishers">The publishers to fan events out to.</param>
    /// <param name="logger">Logger used to record a publisher's failure.</param>
    public CompositeDomainEventPublisher(
        IEnumerable<IDomainEventPublisher> publishers,
        ILogger<CompositeDomainEventPublisher> logger)
    {
        _publishers = publishers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        // Materialized once: the sequence is enumerated by every publisher, and
        // re-enumerating a lazy query per sink would repeat any work behind it.
        var events = domainEvents as IReadOnlyList<IDomainEvent> ?? domainEvents.ToList();

        foreach (var publisher in _publishers)
        {
            try
            {
                await publisher.PublishAsync(events, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "The domain event publisher {Publisher} failed. Remaining publishers still ran.",
                    publisher.GetType().Name);
            }
        }
    }
}
