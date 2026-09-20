using Ambev.DeveloperEvaluation.Functional.Common;
using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ambev.DeveloperEvaluation.Functional.Auth;

/// <summary>
/// Contains end-to-end tests for which routes require a bearer token and what a
/// rejection looks like.
/// </summary>
/// <remarks>
/// <para>
/// These are separate from the feature tests on purpose. Every other functional
/// test uses an authenticated client and is about a feature's behaviour; asserting
/// the token requirement inside each of them would say the same thing thirty times
/// and still leave the interesting cases - a malformed token, an open route -
/// untested.
/// </para>
/// <para>
/// The client here is deliberately unauthenticated. Where a test needs a token it
/// builds one explicitly, so that the difference between the two is visible in the
/// test rather than hidden in a fixture.
/// </para>
/// </remarks>
public class AuthorizationTests : IClassFixture<SalesApiFactory>
{
    private readonly SalesApiFactory _factory;
    private readonly HttpClient _anonymous;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes the test with an unauthenticated client.
    /// </summary>
    /// <param name="factory">The shared application factory.</param>
    public AuthorizationTests(SalesApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    /// <summary>
    /// Enumerates the sale routes, so the token requirement is asserted for all of
    /// them rather than for a representative one.
    /// </summary>
    /// <remarks>
    /// A route left off an [Authorize] is the defect this guards against, and it is
    /// invisible unless every route is named.
    /// </remarks>
    public static TheoryData<string, string> ProtectedSaleRoutes() => new()
    {
        { "GET",    "/api/sales" },
        { "GET",    "/api/sales/11111111-1111-1111-1111-111111111111" },
        { "POST",   "/api/sales" },
        { "PUT",    "/api/sales/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/api/sales/11111111-1111-1111-1111-111111111111" },
        { "PATCH",  "/api/sales/11111111-1111-1111-1111-111111111111/cancel" },
        { "PATCH",  "/api/sales/11111111-1111-1111-1111-111111111111/items/22222222-2222-2222-2222-222222222222/cancel" }
    };

    // -- Rejection ------------------------------------------------------------

    [Theory(DisplayName = "Every sale route should refuse an unauthenticated request")]
    [MemberData(nameof(ProtectedSaleRoutes))]
    public async Task Given_NoToken_When_CallingASaleRoute_Then_ReturnsUnauthorized(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);

        // A body only matters for the routes that take one; an empty object is
        // enough for the rest and keeps the call shape uniform. Authorization runs
        // before model binding, so the body is never read on a rejected request.
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { });

        var response = await _anonymous.SendAsync(request);

        // 401, not 404 - even for the ids that do not exist. Authorization runs
        // before the handler, so an unauthenticated caller cannot learn whether a
        // resource is there.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "{0} {1} should require a token", method, route);
    }

    [Fact(DisplayName = "A rejection should use the documented error body")]
    public async Task Given_NoToken_When_Refused_Then_ReturnsTheErrorContract()
    {
        var response = await _anonymous.GetAsync("/api/sales");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The default JWT challenge writes an empty body, which would be a second
        // error shape in an API that documents exactly one. AuthenticationErrorContract
        // exists to stop that, and this is the test that would notice if it were
        // removed or never wired up.
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotBeNullOrWhiteSpace("a 401 must carry the documented body, not an empty response");

        var body = JsonSerializer.Deserialize<JsonElement>(raw, JsonOptions);

        body.GetProperty("type").GetString().Should().Be("AuthenticationError");
        body.TryGetProperty("error", out _).Should().BeTrue();
        body.TryGetProperty("detail", out _).Should().BeTrue();

        // ASP.NET's own failure shape must not leak through alongside ours.
        body.TryGetProperty("traceId", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "A malformed token should be refused")]
    public async Task Given_GarbageToken_When_CallingASaleRoute_Then_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not.a.real.token");

        var response = await client.GetAsync("/api/sales");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("type").GetString().Should().Be("AuthenticationError");
    }

    [Fact(DisplayName = "A token signed with the wrong key should be refused")]
    public async Task Given_ForeignToken_When_CallingASaleRoute_Then_ReturnsUnauthorized()
    {
        // Structurally a JWT, and signed - just not by this application. Proves the
        // signature is actually verified rather than the token merely being parsed.
        const string foreignToken =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +
            ".eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ" +
            ".SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", foreignToken);

        var response = await client.GetAsync("/api/sales");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Reading a user should require a token")]
    public async Task Given_NoToken_When_ReadingAUser_Then_ReturnsUnauthorized()
    {
        var response = await _anonymous.GetAsync("/api/users/11111111-1111-1111-1111-111111111111");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Deleting a user should require a token")]
    public async Task Given_NoToken_When_DeletingAUser_Then_ReturnsUnauthorized()
    {
        var response = await _anonymous.DeleteAsync("/api/users/11111111-1111-1111-1111-111111111111");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -- Routes that must stay open -------------------------------------------

    [Fact(DisplayName = "Registration should stay open to unauthenticated callers")]
    public async Task Given_NoToken_When_Registering_Then_Succeeds()
    {
        // If this ever required a token the API would be sealed: a token comes from
        // /api/auth, which needs an account, which only this route can create.
        var response = await _anonymous.PostAsJsonAsync("/api/users", new
        {
            username = "openregistration",
            email = "open-registration@example.com",
            phone = "+5511977776666",
            password = "Str0ng!Pass1",
            status = "Active",
            role = "Customer"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "Authentication should stay open to unauthenticated callers")]
    public async Task Given_NoToken_When_Authenticating_Then_Succeeds()
    {
        await _anonymous.PostAsJsonAsync("/api/users", new
        {
            username = "openauth",
            email = "open-auth@example.com",
            phone = "+5511966665555",
            password = "Str0ng!Pass1",
            status = "Active",
            role = "Customer"
        });

        var response = await _anonymous.PostAsJsonAsync("/api/auth", new
        {
            email = "open-auth@example.com",
            password = "Str0ng!Pass1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Health checks should stay open to unauthenticated callers")]
    public async Task Given_NoToken_When_CheckingHealth_Then_Succeeds()
    {
        // A probe that needed credentials would be a liveness check that reports the
        // service as down whenever authentication is misconfigured.
        var response = await _anonymous.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -- The token is what makes the difference -------------------------------

    [Fact(DisplayName = "The same request should succeed once a token is supplied")]
    public async Task Given_AToken_When_CallingASaleRoute_Then_Succeeds()
    {
        var refused = await _anonymous.GetAsync("/api/sales");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var authenticated = _factory.CreateAuthenticatedClient();
        var allowed = await authenticated.GetAsync("/api/sales");

        // Same route, same verb, same body. Only the Authorization header differs,
        // which is what makes this a test of authorization rather than of routing.
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "The issued token should carry the claims the API reads")]
    public async Task Given_ValidCredentials_When_Authenticating_Then_TokenCarriesEmailClaim()
    {
        await _anonymous.PostAsJsonAsync("/api/users", new
        {
            username = "claimscheck",
            email = "claims-check@example.com",
            phone = "+5511955554444",
            password = "Str0ng!Pass1",
            status = "Active",
            role = "Customer"
        });

        var authentication = await _anonymous.PostAsJsonAsync("/api/auth", new
        {
            email = "claims-check@example.com",
            password = "Str0ng!Pass1"
        });

        var body = await authentication.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = body.GetProperty("data").GetProperty("token").GetString();

        // BaseController reads ClaimTypes.NameIdentifier and ClaimTypes.Email, but on
        // the wire the handler writes the short JWT names and maps them back on read.
        // Asserting the short names keeps this about the bytes actually sent.
        var payload = DecodePayload(token!);

        payload.Should().Contain("\"nameid\"");
        payload.Should().Contain("\"email\"");
        payload.Should().Contain("claims-check@example.com");
        payload.Should().Contain("\"role\"");
    }

    /// <summary>
    /// Base64url-decodes the payload segment of a JWT.
    /// </summary>
    /// <param name="token">The token to read.</param>
    /// <returns>The payload as JSON text.</returns>
    /// <remarks>
    /// Done by hand rather than with a token handler, so the assertion is about the
    /// bytes actually sent rather than about how a library chooses to rename claims
    /// when it reads them back.
    /// </remarks>
    private static string DecodePayload(string token)
    {
        var payload = token.Split('.')[1];

        // Base64url drops the padding that Convert.FromBase64String requires.
        var padded = payload.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
