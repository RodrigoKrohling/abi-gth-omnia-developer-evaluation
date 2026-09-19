using Ambev.DeveloperEvaluation.Domain.Entities;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.Application.Users.CreateUser;

/// <summary>
/// Profile for mapping between User entity and CreateUserResponse
/// </summary>
public class CreateUserProfile : Profile
{
    /// <summary>
    /// Initializes the mappings for CreateUser operation
    /// </summary>
    public CreateUserProfile()
    {
        CreateMap<CreateUserCommand, User>()
            // Identity belongs to the database, which generates it with
            // gen_random_uuid() as configured in UserConfiguration. Mapping the
            // command's absent id over it would write an empty Guid.
            .ForMember(dest => dest.Id, opt => opt.Ignore())

            // The entity's constructor stamps CreatedAt, and UpdatedAt stays null
            // until the first change. Neither is the caller's to supply.
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<User, CreateUserResult>()
            // The entity calls it Username and the result calls it Name, so
            // convention-based matching does not pair them. Without this the result's
            // Name came back empty.
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Username));
    }
}
