using Ambev.DeveloperEvaluation.Domain.Entities;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.Application.Auth.AuthenticateUser;

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
        CreateMap<User, AuthenticateUserResult>()
            // The token is not a property of the user. It is minted per
            // authentication by IJwtTokenGenerator, so AuthenticateUserHandler sets it
            // on the result after this map would have run.
            .ForMember(dest => dest.Token, opt => opt.Ignore())

            // The entity calls it Username and the result calls it Name, so the
            // convention-based matching that fills every other member does not pair
            // these two. Without this the map has an unfillable destination member and
            // the whole configuration fails validation.
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Username))

            // Role is a UserRole enum on the entity and a string on the result.
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()));
    }
}
