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

        // Outbound: application result -> API response. Every member matches by name
        // and type, Role being a string on both sides.
        CreateMap<AuthenticateUserResult, AuthenticateUserResponse>();
    }
}
