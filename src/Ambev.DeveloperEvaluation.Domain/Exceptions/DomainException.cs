namespace Ambev.DeveloperEvaluation.Domain.Exceptions;

/// <summary>
/// Signals that an operation was rejected by a business rule.
/// </summary>
/// <remarks>
/// This is the domain's way of refusing a state transition that the invariants do
/// not allow, such as selling more than twenty identical items in one sale or
/// modifying a sale that has already been cancelled.
///
/// It is distinct from a validation failure. A validation failure means the caller
/// sent something malformed (a negative quantity, a missing customer); a
/// <see cref="DomainException"/> means the input was well-formed but the operation
/// is not permitted in the current state. The API surfaces the two differently:
/// <c>ValidationError</c> versus <c>BusinessRuleViolation</c>, both as HTTP 400.
/// </remarks>
public class DomainException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="message">A description of the rule that rejected the operation.</param>
    public DomainException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="message">A description of the rule that rejected the operation.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
