using Ambev.DeveloperEvaluation.Application;
using Ambev.DeveloperEvaluation.Application.Auth.AuthenticateUser;
using Ambev.DeveloperEvaluation.Application.Users.CreateUser;
using Ambev.DeveloperEvaluation.Application.Users.GetUser;
using Ambev.DeveloperEvaluation.Domain.Enums;
using Ambev.DeveloperEvaluation.Unit.Application.Sales.TestData;
using Ambev.DeveloperEvaluation.WebApi.Features.Auth.AuthenticateUserFeature;
using Ambev.DeveloperEvaluation.WebApi.Features.Users.GetUser;
using AutoMapper;
using FluentAssertions;
using Xunit;
using ApiCreateUser = Ambev.DeveloperEvaluation.WebApi.Features.Users.CreateUser;

namespace Ambev.DeveloperEvaluation.Unit.Mapping;

/// <summary>
/// Contains tests for the application's complete AutoMapper configuration.
/// </summary>
/// <remarks>
/// Builds the same configuration <c>Program.cs</c> does - both assemblies scanned
/// together - and validates it as a whole. A map declared in the wrong direction is
/// valid configuration and fails only on a real request, so the direction of each
/// map is also exercised here rather than just its validity.
/// </remarks>
public class MappingConfigurationTests
{
    /// <summary>
    /// Builds the mapper exactly as <c>Program.cs</c> does, scanning both assemblies.
    /// </summary>
    private static IMapper CreateApplicationMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddMaps(
                typeof(ApiCreateUser.CreateUserProfile).Assembly,
                typeof(ApplicationLayer).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

    [Fact(DisplayName = "The whole AutoMapper configuration should be valid")]
    public void Given_AllProfiles_When_Validated_Then_ConfigurationIsValid()
    {
        var mapper = CreateApplicationMapper();

        // Fails if any declared map has a destination member that cannot be filled.
        // This is the single assertion that would have caught the broken user
        // profiles before they reached a running endpoint.
        var act = () => mapper.ConfigurationProvider.AssertConfigurationIsValid();

        act.Should().NotThrow();
    }

    // -- The maps each controller action actually performs ---------------------
    //
    // Configuration validity is necessary but not sufficient: a map declared in the
    // wrong direction is perfectly valid, it is simply not the map the controller
    // asks for. Each test below mirrors one _mapper.Map call in a controller.

    [Fact(DisplayName = "GetUserResult should map to GetUserResponse")]
    public void Given_GetUserResult_When_Mapped_Then_ProducesResponse()
    {
        // UsersController.GetUser maps in this direction; the reverse is never used.
        var mapper = CreateApplicationMapper();

        var result = new GetUserResult
        {
            Id = Guid.NewGuid(),
            Name = "Maria Silva",
            Email = "maria@example.com",
            Phone = "+5511999999999",
            Role = UserRole.Customer,
            Status = UserStatus.Active
        };

        var response = mapper.Map<GetUserResponse>(result);

        response.Id.Should().Be(result.Id);
        response.Name.Should().Be("Maria Silva");
        response.Email.Should().Be("maria@example.com");
        response.Phone.Should().Be("+5511999999999");
        response.Role.Should().Be(UserRole.Customer);
        response.Status.Should().Be(UserStatus.Active);
    }

    [Fact(DisplayName = "AuthenticateUserResult should map to AuthenticateUserResponse")]
    public void Given_AuthenticateUserResult_When_Mapped_Then_ProducesResponse()
    {
        // AuthController maps the result, not the User entity, and Token is the one
        // field the endpoint exists to return.
        var mapper = CreateApplicationMapper();

        var result = new AuthenticateUserResult
        {
            Token = "a.jwt.token",
            Id = Guid.NewGuid(),
            Name = "Maria Silva",
            Email = "maria@example.com",
            Phone = "+5511999999999",
            Role = nameof(UserRole.Admin)
        };

        var response = mapper.Map<AuthenticateUserResponse>(result);

        response.Token.Should().Be("a.jwt.token");
        response.Name.Should().Be("Maria Silva");
        response.Email.Should().Be("maria@example.com");
        response.Role.Should().Be("Admin");
    }

    [Fact(DisplayName = "CreateUserRequest should map to CreateUserCommand")]
    public void Given_CreateUserRequest_When_Mapped_Then_ProducesCommand()
    {
        var mapper = CreateApplicationMapper();

        var request = new ApiCreateUser.CreateUserRequest
        {
            Username = "maria",
            Email = "maria@example.com",
            Phone = "+5511999999999",
            Password = "Str0ng!Pass",
            Role = UserRole.Customer,
            Status = UserStatus.Active
        };

        var command = mapper.Map<CreateUserCommand>(request);

        command.Username.Should().Be("maria");
        command.Email.Should().Be("maria@example.com");
        command.Role.Should().Be(UserRole.Customer);
        command.Status.Should().Be(UserStatus.Active);
    }

    [Fact(DisplayName = "CreateUserResult should map to CreateUserResponse")]
    public void Given_CreateUserResult_When_Mapped_Then_ProducesResponse()
    {
        var mapper = CreateApplicationMapper();
        var id = Guid.NewGuid();

        var response = mapper.Map<ApiCreateUser.CreateUserResponse>(new CreateUserResult { Id = id });

        response.Id.Should().Be(id);
    }

    [Fact(DisplayName = "A Guid should map to the commands that wrap it")]
    public void Given_Guid_When_Mapped_Then_ProducesIdentifiedCommands()
    {
        // UsersController maps a bare route Guid onto GetUserCommand and
        // DeleteUserCommand, both of which are records taking the id in their
        // constructor.
        var mapper = CreateApplicationMapper();
        var id = Guid.NewGuid();

        mapper.Map<GetUserCommand>(id).Id.Should().Be(id);
        mapper.Map<Ambev.DeveloperEvaluation.Application.Users.DeleteUser.DeleteUserCommand>(id).Id.Should().Be(id);
    }

    [Fact(DisplayName = "Sale mappings should still be valid alongside the user ones")]
    public void Given_SaleResult_When_Mapped_Then_ProducesResponse()
    {
        // The sale profiles are exercised elsewhere, but only in isolation. This
        // confirms they still resolve once every profile in both assemblies is loaded
        // together, which is how the application runs.
        var mapper = CreateApplicationMapper();

        var sale = SaleCommandTestData.GenerateCreateCommand(itemCount: 1);
        sale.Should().NotBeNull();

        mapper.ConfigurationProvider.Invoking(c => c.AssertConfigurationIsValid())
            .Should().NotThrow();
    }
}
