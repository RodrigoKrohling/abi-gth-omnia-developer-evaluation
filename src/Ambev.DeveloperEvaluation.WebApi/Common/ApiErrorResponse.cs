namespace Ambev.DeveloperEvaluation.WebApi.Common;

/// <summary>
/// The error body returned by every failing request.
/// </summary>
/// <remarks>
/// The three fields and their meaning are fixed by the API specification in
/// <c>.doc/general-api.md</c>:
///
/// <code>
/// {
///   "type": "ResourceNotFound",
///   "error": "Sale not found",
///   "detail": "The sale with ID 12345 does not exist in our database"
/// }
/// </code>
///
/// Successful responses keep using <see cref="ApiResponseWithData{T}"/>; this type
/// is only ever produced by <c>ExceptionHandlingMiddleware</c>. Keeping the two
/// shapes separate means a client can tell an error from a payload by shape alone,
/// without inspecting a status code or a success flag.
/// </remarks>
public class ApiErrorResponse
{
    /// <summary>
    /// Gets the machine-readable error type identifier, for example
    /// <c>ValidationError</c> or <c>ResourceNotFound</c>.
    /// </summary>
    /// <remarks>
    /// Clients should branch on this value rather than on <see cref="Error"/>, which
    /// is prose and may be reworded.
    /// </remarks>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets a short, human-readable summary of the problem.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Gets a human-readable explanation specific to this occurrence of the problem.
    /// </summary>
    public string Detail { get; init; } = string.Empty;
}
