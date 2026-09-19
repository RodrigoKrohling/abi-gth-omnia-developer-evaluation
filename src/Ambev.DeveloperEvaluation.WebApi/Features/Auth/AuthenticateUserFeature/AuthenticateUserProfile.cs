using Ambev.DeveloperEvaluation.Application.Auth.AuthenticateUser;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Auth.AuthenticateUserFeature;

/// <summary>
/// AutoMapper profile for authentication-related mappings
/// </summary>
public sealed class AuthenticateUserProfile : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticateUserProfile"/> class
    /// </summary>
    public AuthenticateUserProfile()
    {
        // Inbound: HTTP body -> application command.
        CreateMap<AuthenticateUserRequest, AuthenticateUserCommand>();

        // Outbound: application result -> API response.
        //
        // This previously mapped from the User entity and ignored Token, neither of
        // which matched how the endpoint works. AuthController maps the
        // AuthenticateUserResult that the handler returns, and Token is the single
        // value the login endpoint exists to produce - the handler generates the JWT
        // and puts it on the result, so ignoring it here emitted a response with an
        // empty token. Both were moot in practice, because the map the controller
        // asked for did not exist at all and the request failed with
        // "Missing type map configuration".
        //
        // Every member matches by name and type, Role being a string on both sides,
        // so no explicit member configuration is needed.
        CreateMap<AuthenticateUserResult, AuthenticateUserResponse>();
    }
}
