using Ambev.DeveloperEvaluation.Application.Users.GetUser;
using AutoMapper;

namespace Ambev.DeveloperEvaluation.WebApi.Features.Users.GetUser;

/// <summary>
/// Profile for mapping GetUser feature requests to commands
/// </summary>
public class GetUserProfile : Profile
{
    /// <summary>
    /// Initializes the mappings for GetUser feature
    /// </summary>
    public GetUserProfile()
    {
        // Inbound: the route's bare Guid becomes the command. The command is a record
        // whose Id is set only through its constructor, so ConstructUsing supplies it.
        CreateMap<Guid, GetUserCommand>()
            .ConstructUsing(id => new GetUserCommand(id));

        // Outbound: application result -> API response.
        CreateMap<GetUserResult, GetUserResponse>();
    }
}
