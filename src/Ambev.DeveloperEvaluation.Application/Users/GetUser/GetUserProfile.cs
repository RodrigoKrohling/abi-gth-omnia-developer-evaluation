using Ambev.DeveloperEvaluation.Domain.Entities;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.Application.Users.GetUser;

/// <summary>
/// Profile for mapping between User entity and GetUserResponse
/// </summary>
public class GetUserProfile : Profile
{
    /// <summary>
    /// Initializes the mappings for GetUser operation
    /// </summary>
    public GetUserProfile()
    {
        CreateMap<User, GetUserResult>()
            // Username on the entity, Name on the result: the two do not pair by
            // convention, so GET /api/users/{id} returned an empty name.
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Username));
    }
}
