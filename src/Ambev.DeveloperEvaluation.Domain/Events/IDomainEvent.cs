namespace Ambev.DeveloperEvaluation.Domain.Events;

/// <summary>
/// Marks a record of something that has already happened inside the domain.
/// </summary>
/// <remarks>
/// Domain events are named in the past tense (<c>SaleCreated</c>, not
/// <c>CreateSale</c>) because they describe a fact, not a request. By the time one
/// exists, the state change has already been applied to the aggregate.
///
/// This interface deliberately carries no dependency on MediatR, Rebus or any other
/// messaging library. The domain raises events; choosing how they are dispatched is
/// an infrastructure decision made in the outer layers, which is what keeps the
/// domain project referencing nothing but FluentValidation.
///
/// Events are collected on the <see cref="Common.AggregateRoot"/> that raised them
/// and are published only after the transaction commits, so a rolled-back operation
/// never announces something that did not happen.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the instant, in UTC, at which the event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}
