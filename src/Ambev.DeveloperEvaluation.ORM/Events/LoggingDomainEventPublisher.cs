using Ambev.DeveloperEvaluation.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Ambev.DeveloperEvaluation.ORM.Events;

/// <summary>
/// Announces domain events by writing them to the application log.
/// </summary>
/// <remarks>
/// This is what the brief asks for: "If you write the code, it's not required to
/// actually publish to any Message Broker. You can log a message in the application
/// log or however you find most convenient."
///
/// The events are logged as structured properties rather than interpolated into the
/// message, so Serilog records <c>SaleId</c> and <c>SaleNumber</c> as queryable
/// fields. Against a structured sink that is the difference between grepping text
/// and filtering by <c>EventType = 'SaleCancelled'</c>.
///
/// Replacing this with a real broker means registering a different
/// <see cref="IDomainEventPublisher"/> and changing nothing else - no handler, no
/// controller and no part of the domain refers to it.
/// </remarks>
public class LoggingDomainEventPublisher : IDomainEventPublisher
{
    private readonly ILogger<LoggingDomainEventPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingDomainEventPublisher"/> class.
    /// </summary>
    /// <param name="logger">The logger to write events to.</param>
    public LoggingDomainEventPublisher(ILogger<LoggingDomainEventPublisher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            // The event type is logged as its own property so that a sink can filter
            // on it, and the whole event is destructured (@) so its fields are
            // recorded individually rather than as a ToString().
            _logger.LogInformation(
                "Domain event published: {EventType} at {OccurredOn} {@DomainEvent}",
                domainEvent.GetType().Name,
                domainEvent.OccurredOn,
                domainEvent);
        }

        return Task.CompletedTask;
    }
}
