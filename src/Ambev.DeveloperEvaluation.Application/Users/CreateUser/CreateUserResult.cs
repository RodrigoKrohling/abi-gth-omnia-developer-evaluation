using Ambev.DeveloperEvaluation.Domain.Enums;

namespace Ambev.DeveloperEvaluation.Application.Users.CreateUser;

/// <summary>
/// Represents the response returned after successfully creating a new user.
/// </summary>
/// <remarks>
/// <para>
/// Carries the created user's details, not only its identifier. The API's
/// <c>CreateUserResponse</c> has always declared Name, Email, Phone, Role and
/// Status, but this result carried nothing but <see cref="Id"/>, so AutoMapper had
/// no source for any of them and <c>POST /api/users</c> answered with an id beside
/// five empty fields.
/// </para>
/// <para>
/// Filling the result rather than trimming the response is the right way round: the
/// response is the published contract, and the server already holds every one of
/// these values at the point it replies. Returning them saves the caller an
/// immediate follow-up GET.
/// </para>
/// <para>
/// The password is deliberately absent. It is hashed before storage and must never
/// travel back out.
/// </para>
/// </remarks>
public class CreateUserResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the newly created user.
    /// </summary>
    /// <value>A GUID that uniquely identifies the created user in the system.</value>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user's name.
    /// </summary>
    /// <remarks>
    /// Sourced from the entity's <c>Username</c>, which the mapping profile pairs
    /// explicitly because the two names do not match by convention.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's phone number.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's role in the system.
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// Gets or sets the user's current status.
    /// </summary>
    public UserStatus Status { get; set; }
}
