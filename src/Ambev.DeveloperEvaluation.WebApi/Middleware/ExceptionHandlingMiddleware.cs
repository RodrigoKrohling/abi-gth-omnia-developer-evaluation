using Ambev.DeveloperEvaluation.Domain.Exceptions;
using Ambev.DeveloperEvaluation.WebApi.Common;
using FluentValidation;
using System.Text.Json;

namespace Ambev.DeveloperEvaluation.WebApi.Middleware;

/// <summary>
/// Translates exceptions thrown anywhere in the request pipeline into the error
/// response documented in <c>.doc/general-api.md</c>.
/// </summary>
/// <remarks>
/// This is the single place where an exception becomes an HTTP status code, which
/// is what lets the rest of the codebase stay free of HTTP concerns: a handler
/// throws <see cref="KeyNotFoundException"/> and does not need to know that it
/// becomes a 404, and the domain throws <see cref="DomainException"/> without
/// referencing ASP.NET Core at all.
///
/// It replaces the template's <c>ValidationExceptionMiddleware</c>, which handled
/// only <see cref="ValidationException"/> and emitted a different body shape. Every
/// other exception escaped to the default handler and produced an HTML error page.
///
/// Register it first in the pipeline so that it wraps everything downstream.
/// </remarks>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Serializer settings for the error body.
    /// </summary>
    /// <remarks>
    /// Camel case matches the documented contract, whose fields are lower-case
    /// (<c>type</c>, <c>error</c>, <c>detail</c>) while the C# properties are Pascal
    /// case. Cached in a static field because <see cref="JsonSerializerOptions"/> is
    /// expensive to construct and is thread-safe once created.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionHandlingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate in the request pipeline.</param>
    /// <param name="logger">Logger used to record unhandled failures.</param>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the next middleware and converts any exception it raises into an
    /// <see cref="ApiErrorResponse"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    /// <summary>
    /// Maps an exception to a status code and writes the error body.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="exception">The exception to translate.</param>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // The response may already be streaming if the exception happened after the
        // first bytes were flushed. Rewriting the status code at that point throws,
        // so the only safe action left is to log and let the connection drop.
        if (context.Response.HasStarted)
        {
            _logger.LogError(exception, "An exception was thrown after the response had started");
            return;
        }

        var (statusCode, body) = Map(exception);

        // Unexpected failures are logged with the full exception; expected ones
        // (validation, not found) are ordinary control flow and would only be noise.
        if (statusCode == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }

    /// <summary>
    /// Chooses the status code and error body for a given exception.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>The HTTP status code paired with the body to serialize.</returns>
    private static (int StatusCode, ApiErrorResponse Body) Map(Exception exception) => exception switch
    {
        // A request that failed FluentValidation, either at the controller or in the
        // MediatR ValidationBehavior pipeline step.
        ValidationException validation => (
            StatusCodes.Status400BadRequest,
            new ApiErrorResponse
            {
                Type = "ValidationError",
                Error = "Invalid input data",
                // The contract carries a single detail string, so the individual
                // FluentValidation failures are joined rather than nested. Without
                // this the caller would be told only that something was invalid.
                Detail = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage))
            }),

        // A business rule rejected the operation, for example selling more than
        // twenty identical items. The input was well-formed, so this is not a
        // validation error; the domain simply refuses the transition.
        DomainException domain => (
            StatusCodes.Status400BadRequest,
            new ApiErrorResponse
            {
                Type = "BusinessRuleViolation",
                Error = "Operation rejected by a business rule",
                Detail = domain.Message
            }),

        // Handlers signal a missing aggregate by throwing KeyNotFoundException.
        KeyNotFoundException notFound => (
            StatusCodes.Status404NotFound,
            new ApiErrorResponse
            {
                Type = "ResourceNotFound",
                Error = "Resource not found",
                Detail = notFound.Message
            }),

        // Authentication failed, for example a bad username or password.
        UnauthorizedAccessException unauthorized => (
            StatusCodes.Status401Unauthorized,
            new ApiErrorResponse
            {
                Type = "AuthenticationError",
                Error = "Authentication failed",
                Detail = unauthorized.Message
            }),

        // A request that is well-formed but collides with something that already
        // exists, such as creating a sale whose number is taken.
        //
        // Matched on a dedicated exception type, never on InvalidOperationException.
        // The BCL throws that one for all manner of programming errors - an
        // unresolvable dependency, a misconfigured DbContext, a sequence with no
        // elements - and mapping it here reported every such fault to the client as a
        // business conflict while never logging it as the bug it was.
        ResourceConflictException conflict => (
            StatusCodes.Status409Conflict,
            new ApiErrorResponse
            {
                Type = "ResourceConflict",
                Error = "The request conflicts with the current state of the resource",
                Detail = conflict.Message
            }),

        // Anything unrecognised is a defect. The message is deliberately generic:
        // exception text can carry connection strings, file paths and SQL, none of
        // which belong in a response body. The detail is in the log instead.
        _ => (
            StatusCodes.Status500InternalServerError,
            new ApiErrorResponse
            {
                Type = "InternalServerError",
                Error = "An unexpected error occurred",
                Detail = "The request could not be processed. Please contact support if the problem persists."
            })
    };
}
