namespace Ambev.DeveloperEvaluation.Domain.Exceptions;

/// <summary>
/// Signals that a well-formed request conflicts with the current state of the
/// system, such as creating a sale whose number is already taken.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated type rather than <see cref="InvalidOperationException"/>. The
/// exception middleware maps this to HTTP 409, and mapping the framework's generic
/// <see cref="InvalidOperationException"/> there instead turned out to be actively
/// harmful: the BCL throws it for a wide range of ordinary programming errors -
/// an unresolvable dependency, a misconfigured <c>DbContext</c>, a sequence with no
/// elements - and every one of those would have been reported to the client as a
/// business conflict and never logged as the fault it was.
/// </para>
/// <para>
/// That is not hypothetical: a misconfigured test host made every request in the
/// suite answer 409, including <c>/health</c>, which hid the real error completely
/// until the mapping was narrowed to this type.
/// </para>
/// <para>
/// Distinct from <see cref="DomainException"/>, which means an invariant refused a
/// state transition and maps to 400. A conflict is about collision with something
/// that already exists, and is worth its own status code because the caller's
/// remedy is different: change the identifier, rather than change the data.
/// </para>
/// </remarks>
public class ResourceConflictException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of what already exists.</param>
    public ResourceConflictException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of what already exists.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ResourceConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
