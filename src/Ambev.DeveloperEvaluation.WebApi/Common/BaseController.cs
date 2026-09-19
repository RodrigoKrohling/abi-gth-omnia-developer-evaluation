using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ambev.DeveloperEvaluation.WebApi.Common;

[Route("api/[controller]")]
[ApiController]
public class BaseController : ControllerBase
{
    /// <summary>
    /// Gets the identifier of the authenticated user from the NameIdentifier claim.
    /// </summary>
    /// <returns>The current user's identifier.</returns>
    /// <remarks>
    /// The claim is written by <c>JwtTokenGenerator</c> from <c>IUser.Id</c>, which is
    /// a <see cref="Guid"/> rendered as a string. This previously parsed the claim as an
    /// <c>int</c>, which threw a <see cref="FormatException"/> for every token the
    /// application itself issues.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the request is not authenticated.</exception>
    protected Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("The request does not carry an authenticated user."));

    /// <summary>
    /// Gets the email address of the authenticated user from the Email claim.
    /// </summary>
    /// <returns>The current user's email address.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the request carries no email claim.</exception>
    protected string GetCurrentUserEmail() =>
        User.FindFirst(ClaimTypes.Email)?.Value
            ?? throw new InvalidOperationException("The request does not carry an email claim.");

    protected IActionResult Ok<T>(T data) =>
            base.Ok(new ApiResponseWithData<T> { Data = data, Success = true });

    protected IActionResult Created<T>(string routeName, object routeValues, T data) =>
        base.CreatedAtRoute(routeName, routeValues, new ApiResponseWithData<T> { Data = data, Success = true });

    protected IActionResult BadRequest(string message) =>
        base.BadRequest(new ApiResponse { Message = message, Success = false });

    protected IActionResult NotFound(string message = "Resource not found") =>
        base.NotFound(new ApiResponse { Message = message, Success = false });

    protected IActionResult OkPaginated<T>(PaginatedList<T> pagedList) =>
            Ok(new PaginatedResponse<T>
            {
                Data = pagedList,
                CurrentPage = pagedList.CurrentPage,
                TotalPages = pagedList.TotalPages,
                TotalCount = pagedList.TotalCount,
                Success = true
            });
}
