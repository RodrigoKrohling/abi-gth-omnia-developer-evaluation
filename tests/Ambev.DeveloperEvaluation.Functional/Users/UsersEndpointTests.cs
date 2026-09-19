using Ambev.DeveloperEvaluation.Functional.Common;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ambev.DeveloperEvaluation.Functional.Users;

/// <summary>
/// Contains end-to-end tests for the Users and Auth endpoints, driven over HTTP
/// against the real application.
/// </summary>
/// <remarks>
/// These endpoints shipped with the template and had no tests of any kind, which is
/// how three broken AutoMapper profiles survived: a map declared in the wrong
/// direction compiles and passes every unit test that does not exercise it, then
/// fails on the first real request.
///
/// Each test below corresponds to a mapping or envelope defect that was present
/// before these were written.
/// </remarks>
public class UsersEndpointTests : IClassFixture<SalesApiFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes the test with a client for the in-memory API.
    /// </summary>
    /// <param name="factory">The shared application factory.</param>
    public UsersEndpointTests(SalesApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Builds a valid create-user body.
    /// </summary>
    /// <param name="email">The email, which must be unique across the suite.</param>
    private static object BuildCreateBody(string email) => new
    {
        username = "mariasilva",
        email,
        phone = "+5511999999999",
        password = "Str0ng!Pass1",
        status = "Active",
        role = "Customer"
    };

    /// <summary>
    /// Creates a user and returns the parsed response body.
    /// </summary>
    private async Task<JsonElement> CreateUserAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/users", BuildCreateBody(email));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    // -- Create ---------------------------------------------------------------

    [Fact(DisplayName = "Creating a user should return the created user's details")]
    public async Task Given_ValidUser_When_Created_Then_ReturnsPopulatedDetails()
    {
        var body = await CreateUserAsync("create-details@example.com");
        var data = body.GetProperty("data");

        data.GetProperty("id").GetGuid().Should().NotBeEmpty();

        // CreateUserResult previously carried nothing but Id, while the response type
        // declared five more fields. They came back empty because AutoMapper had no
        // source for them.
        data.GetProperty("name").GetString().Should().Be("mariasilva");
        data.GetProperty("email").GetString().Should().Be("create-details@example.com");
        data.GetProperty("phone").GetString().Should().Be("+5511999999999");
        data.GetProperty("role").GetString().Should().Be("Customer");
        data.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact(DisplayName = "The create response should never echo the password")]
    public async Task Given_CreatedUser_When_Inspected_Then_PasswordIsAbsent()
    {
        var body = await CreateUserAsync("create-nopassword@example.com");
        var data = body.GetProperty("data");

        // The password is hashed before storage and must not travel back out.
        data.TryGetProperty("password", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "A malformed create request should answer 400 with the error contract")]
    public async Task Given_InvalidUser_When_Created_Then_ReturnsErrorContract()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            username = "",
            email = "not-an-email",
            phone = "123",
            password = "weak",
            status = "Active",
            role = "Customer"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // The controller previously returned the raw FluentValidation failure list,
        // which serialized as a JSON array rather than the documented object.
        body.ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("type").GetString().Should().Be("ValidationError");
        body.TryGetProperty("error", out _).Should().BeTrue();
        body.TryGetProperty("detail", out _).Should().BeTrue();
    }

    // -- Read -----------------------------------------------------------------

    [Fact(DisplayName = "A created user should be retrievable with all fields populated")]
    public async Task Given_CreatedUser_When_Retrieved_Then_ReturnsAllFields()
    {
        var created = await CreateUserAsync("get-user@example.com");
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/users/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        // GetUserProfile declared GetUserResponse -> GetUserResult, the opposite of
        // what the controller asks for, so this endpoint failed outright with
        // "Missing type map configuration".
        data.GetProperty("id").GetGuid().Should().Be(id);
        data.GetProperty("email").GetString().Should().Be("get-user@example.com");

        // And Name is sourced from the entity's Username, which does not pair by
        // convention and so came back empty.
        data.GetProperty("name").GetString().Should().Be("mariasilva");
    }

    [Fact(DisplayName = "The response envelope should not be double-wrapped")]
    public async Task Given_CreatedUser_When_Retrieved_Then_EnvelopeIsFlat()
    {
        var created = await CreateUserAsync("envelope@example.com");
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/users/{id}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // BaseController.Ok<T> wraps its argument, so passing an already-built
        // envelope produced {"data":{"data":{...}}}. The payload must sit directly
        // under data, not under a second one.
        var data = body.GetProperty("data");
        data.TryGetProperty("data", out _).Should().BeFalse("the envelope must not be wrapped twice");
        data.TryGetProperty("id", out _).Should().BeTrue();
    }

    [Fact(DisplayName = "Retrieving an unknown user should answer 404")]
    public async Task Given_UnknownId_When_Retrieved_Then_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("type").GetString().Should().Be("ResourceNotFound");
    }

    // -- Delete ---------------------------------------------------------------

    [Fact(DisplayName = "Deleting a user should remove it")]
    public async Task Given_CreatedUser_When_Deleted_Then_ItIsGone()
    {
        var created = await CreateUserAsync("delete-user@example.com");
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var deleteResponse = await _client.DeleteAsync($"/api/users/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.GetAsync($"/api/users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Authentication -------------------------------------------------------

    [Fact(DisplayName = "Authenticating should return a token and the user's details")]
    public async Task Given_ValidCredentials_When_Authenticating_Then_ReturnsToken()
    {
        await CreateUserAsync("auth-ok@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth", new
        {
            email = "auth-ok@example.com",
            password = "Str0ng!Pass1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        // AuthenticateUserProfile mapped from the User entity rather than from the
        // AuthenticateUserResult the controller passes, and ignored Token - the one
        // value this endpoint exists to produce. The request failed before that even
        // mattered, because Request -> Command was never mapped at all.
        data.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("email").GetString().Should().Be("auth-ok@example.com");
        data.GetProperty("name").GetString().Should().Be("mariasilva");
        data.GetProperty("role").GetString().Should().Be("Customer");
    }

    [Fact(DisplayName = "A JWT should be returned in three dot-separated parts")]
    public async Task Given_ValidCredentials_When_Authenticating_Then_TokenLooksLikeAJwt()
    {
        await CreateUserAsync("auth-jwt@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth", new
        {
            email = "auth-jwt@example.com",
            password = "Str0ng!Pass1"
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = body.GetProperty("data").GetProperty("token").GetString();

        // header.payload.signature - enough to show a real token was minted rather
        // than an empty string passing a not-null check.
        token!.Split('.').Should().HaveCount(3);
    }

    [Fact(DisplayName = "Bad credentials should answer 401 with the error contract")]
    public async Task Given_WrongPassword_When_Authenticating_Then_ReturnsUnauthorized()
    {
        await CreateUserAsync("auth-bad@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth", new
        {
            email = "auth-bad@example.com",
            password = "Wr0ng!Pass1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("type").GetString().Should().Be("AuthenticationError");
    }
}
