using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text.Json;

namespace Ambev.DeveloperEvaluation.WebApi.Common;

/// <summary>
/// Makes the authentication and authorization failures emit the same error body as
/// every other failure in the API.
/// </summary>
/// <remarks>
/// The JWT handler rejects by writing a bare 401, not by throwing, and does so
/// downstream of <c>ExceptionHandlingMiddleware</c> - so without this the API would
/// answer every failure with <c>{ type, error, detail }</c> except the two that
/// authorization produces.
/// </remarks>
public static class AuthenticationErrorContract
{
    /// <summary>
    /// The serializer options used for the error body.
    /// </summary>
    /// <remarks>
    /// camelCase to match the rest of the API. This writes the body directly rather
    /// than going through MVC's output formatters, because the pipeline rejects the
    /// request before it reaches a controller, so no formatter is in play.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Builds the JWT bearer events that render the documented error body.
    /// </summary>
    /// <returns>Events to assign to the bearer handler's options.</returns>
    public static JwtBearerEvents CreateEvents() => new()
    {
        // Raised when the request is unauthenticated, or carries a token that is
        // missing, malformed, expired or signed with the wrong key.
        OnChallenge = async context =>
        {
            // Suppresses the handler's own empty-bodied response. Without this, the
            // default 401 is written after this delegate returns and the body below
            // is discarded.
            context.HandleResponse();

            // AuthenticateFailure carries the reason the token was rejected. It is
            // null when no token was sent at all, which is the ordinary case rather
            // than an error worth describing.
            var detail = context.AuthenticateFailure switch
            {
                null => "This endpoint requires a bearer token. Authenticate at POST /api/auth and send the token in the Authorization header.",
                _ => "The bearer token is missing, malformed or expired. Authenticate again at POST /api/auth."
            };

            await WriteAsync(
                context.Response,
                StatusCodes.Status401Unauthorized,
                "AuthenticationError",
                "Authentication failed",
                detail);
        },

        // Raised when the token is valid but does not satisfy the policy - a role
        // requirement, for instance. No endpoint sets one today, so this path is
        // unreachable until one does; it is wired up now so that adding a role
        // requirement later does not silently reintroduce an undocumented shape.
        OnForbidden = async context => await WriteAsync(
            context.Response,
            StatusCodes.Status403Forbidden,
            "AuthorizationError",
            "Access denied",
            "The authenticated user is not allowed to perform this operation.")
    };

    /// <summary>
    /// Writes an <see cref="ApiErrorResponse"/> to the response.
    /// </summary>
    /// <param name="response">The response being written.</param>
    /// <param name="statusCode">The HTTP status code to send.</param>
    /// <param name="type">The machine-readable error type.</param>
    /// <param name="error">The short summary.</param>
    /// <param name="detail">The explanation specific to this occurrence.</param>
    private static async Task WriteAsync(
        HttpResponse response,
        int statusCode,
        string type,
        string error,
        string detail)
    {
        // A handler further along may already have started the response; writing a
        // second status code onto it would throw.
        if (response.HasStarted)
            return;

        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var body = new ApiErrorResponse
        {
            Type = type,
            Error = error,
            Detail = detail
        };

        await response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
