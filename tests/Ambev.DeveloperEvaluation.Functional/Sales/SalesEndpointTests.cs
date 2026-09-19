using Ambev.DeveloperEvaluation.Functional.Common;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ambev.DeveloperEvaluation.Functional.Sales;

/// <summary>
/// Contains end-to-end tests for the Sales endpoints, driven over HTTP against the
/// real application.
/// </summary>
/// <remarks>
/// These are the tests that correspond to the acceptance criteria in the project
/// brief. They assert on status codes and on the JSON a client actually receives, so
/// they cover routing, model binding, the exception middleware's error contract and
/// the response envelope - none of which the unit or integration tests touch.
/// </remarks>
public class SalesEndpointTests : IClassFixture<SalesApiFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Deserialization settings matching the API's camelCase output.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes the test with a client for the in-memory API.
    /// </summary>
    /// <param name="factory">The shared application factory.</param>
    public SalesEndpointTests(SalesApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Builds a valid create-sale body with one item.
    /// </summary>
    /// <param name="saleNumber">The sale number, which must be unique.</param>
    /// <param name="quantity">The quantity for the single item.</param>
    /// <param name="unitPrice">The unit price for the single item.</param>
    private static object BuildCreateBody(string saleNumber, int quantity, decimal unitPrice = 100m) => new
    {
        saleNumber,
        saleDate = "2024-06-01T00:00:00Z",
        customerId = Guid.NewGuid(),
        customerName = "Maria Silva",
        branchId = Guid.NewGuid(),
        branchName = "Downtown",
        items = new[]
        {
            new
            {
                productId = Guid.NewGuid(),
                productTitle = "Backpack",
                quantity,
                unitPrice
            }
        }
    };

    /// <summary>
    /// Creates a sale and returns the parsed response body.
    /// </summary>
    private async Task<JsonElement> CreateSaleAsync(string saleNumber, int quantity, decimal unitPrice = 100m)
    {
        var response = await _client.PostAsJsonAsync("/api/sales", BuildCreateBody(saleNumber, quantity, unitPrice));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    // -- The four acceptance criteria from the brief --------------------------

    [Fact(DisplayName = "A quantity below 4 should receive no discount")]
    public async Task Given_ThreeItems_When_Creating_Then_NoDiscount()
    {
        var body = await CreateSaleAsync("FUNC-0001", quantity: 3);

        var item = body.GetProperty("data").GetProperty("items")[0];

        item.GetProperty("discount").GetDecimal().Should().Be(0m);
        item.GetProperty("discountRate").GetDecimal().Should().Be(0m);
        item.GetProperty("totalAmount").GetDecimal().Should().Be(300m);
        body.GetProperty("data").GetProperty("totalAmount").GetDecimal().Should().Be(300m);
    }

    [Fact(DisplayName = "A quantity of 4 or more should receive a 10% discount")]
    public async Task Given_FiveItems_When_Creating_Then_TenPercentDiscount()
    {
        var body = await CreateSaleAsync("FUNC-0002", quantity: 5);

        var item = body.GetProperty("data").GetProperty("items")[0];

        item.GetProperty("discountRate").GetDecimal().Should().Be(0.10m);
        item.GetProperty("discount").GetDecimal().Should().Be(50m);
        item.GetProperty("totalAmount").GetDecimal().Should().Be(450m);
    }

    [Fact(DisplayName = "A quantity between 10 and 20 should receive a 20% discount")]
    public async Task Given_FifteenItems_When_Creating_Then_TwentyPercentDiscount()
    {
        var body = await CreateSaleAsync("FUNC-0003", quantity: 15);

        var item = body.GetProperty("data").GetProperty("items")[0];

        item.GetProperty("discountRate").GetDecimal().Should().Be(0.20m);
        item.GetProperty("discount").GetDecimal().Should().Be(300m);
        item.GetProperty("totalAmount").GetDecimal().Should().Be(1200m);
    }

    [Fact(DisplayName = "A quantity above 20 should be refused with the documented error body")]
    public async Task Given_TwentyOneItems_When_Creating_Then_BadRequestWithErrorContract()
    {
        var response = await _client.PostAsJsonAsync("/api/sales", BuildCreateBody("FUNC-0004", 21));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // The shape is fixed by .doc/general-api.md: { type, error, detail }.
        body.TryGetProperty("type", out _).Should().BeTrue();
        body.TryGetProperty("error", out _).Should().BeTrue();
        body.TryGetProperty("detail", out _).Should().BeTrue();

        body.GetProperty("detail").GetString().Should().Contain("20");
    }

    [Fact(DisplayName = "The tier boundary should be at exactly 4 items")]
    public async Task Given_ThreeAndFourItems_When_Creating_Then_BoundaryIsAtFour()
    {
        // The brief's prose says "above 4" while its tier list says "4+". This asserts
        // the resolution end to end, not just in the policy.
        var three = await CreateSaleAsync("FUNC-0005", quantity: 3);
        var four = await CreateSaleAsync("FUNC-0006", quantity: 4);

        three.GetProperty("data").GetProperty("items")[0]
            .GetProperty("discountRate").GetDecimal().Should().Be(0m);

        four.GetProperty("data").GetProperty("items")[0]
            .GetProperty("discountRate").GetDecimal().Should().Be(0.10m);
    }

    // -- CRUD -----------------------------------------------------------------

    [Fact(DisplayName = "Creating a sale should answer 201 with a Location header")]
    public async Task Given_ValidSale_When_Creating_Then_ReturnsCreatedWithLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/sales", BuildCreateBody("FUNC-0007", 2));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The Location header lets a client follow the new resource without building
        // the URL itself.
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/Sales/");
    }

    [Fact(DisplayName = "A created sale should be retrievable")]
    public async Task Given_CreatedSale_When_Retrieved_Then_ReturnsIt()
    {
        var created = await CreateSaleAsync("FUNC-0008", quantity: 5);
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/sales/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        data.GetProperty("saleNumber").GetString().Should().Be("FUNC-0008");

        // External identities are serialized as { id, description }.
        data.GetProperty("customer").GetProperty("description").GetString().Should().Be("Maria Silva");
        data.GetProperty("branch").GetProperty("description").GetString().Should().Be("Downtown");
    }

    [Fact(DisplayName = "Retrieving a missing sale should answer 404")]
    public async Task Given_UnknownId_When_Retrieved_Then_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/sales/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("type").GetString().Should().Be("ResourceNotFound");
    }

    [Fact(DisplayName = "A duplicate sale number should answer 409")]
    public async Task Given_DuplicateSaleNumber_When_Creating_Then_ReturnsConflict()
    {
        await CreateSaleAsync("FUNC-0009", quantity: 2);

        var response = await _client.PostAsJsonAsync("/api/sales", BuildCreateBody("FUNC-0009", 2));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("type").GetString().Should().Be("ResourceConflict");
    }

    [Fact(DisplayName = "A malformed request should answer 400 with a validation error")]
    public async Task Given_MissingSaleNumber_When_Creating_Then_ReturnsValidationError()
    {
        var response = await _client.PostAsJsonAsync("/api/sales", new
        {
            saleNumber = "",
            saleDate = "2024-06-01T00:00:00Z",
            customerId = Guid.NewGuid(),
            customerName = "Maria Silva",
            branchId = Guid.NewGuid(),
            branchName = "Downtown",
            items = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Updating a sale should recalculate its discounts")]
    public async Task Given_CreatedSale_When_Updated_Then_RecalculatesDiscounts()
    {
        var created = await CreateSaleAsync("FUNC-0010", quantity: 2);
        var data = created.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        var productId = data.GetProperty("items")[0].GetProperty("product").GetProperty("id").GetGuid();

        var response = await _client.PutAsJsonAsync($"/api/sales/{id}", new
        {
            saleDate = "2025-01-01T00:00:00Z",
            customerId = Guid.NewGuid(),
            customerName = "Joao Souza",
            branchId = Guid.NewGuid(),
            branchName = "Uptown",
            items = new[]
            {
                new { productId, productTitle = "Backpack", quantity = 12, unitPrice = 100m }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var updated = body.GetProperty("data");

        updated.GetProperty("customer").GetProperty("description").GetString().Should().Be("Joao Souza");

        // Going from 2 units to 12 crosses two tiers, so the discount is recalculated
        // from nothing to 20%.
        updated.GetProperty("items")[0].GetProperty("discountRate").GetDecimal().Should().Be(0.20m);
        updated.GetProperty("totalAmount").GetDecimal().Should().Be(960m);
    }

    [Fact(DisplayName = "Deleting a sale should remove it")]
    public async Task Given_CreatedSale_When_Deleted_Then_ItIsGone()
    {
        var created = await CreateSaleAsync("FUNC-0011", quantity: 2);
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var deleteResponse = await _client.DeleteAsync($"/api/sales/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/sales/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Cancellation ---------------------------------------------------------

    [Fact(DisplayName = "Cancelling a sale should zero its total but keep the record")]
    public async Task Given_CreatedSale_When_Cancelled_Then_TotalIsZeroAndRecordRemains()
    {
        var created = await CreateSaleAsync("FUNC-0012", quantity: 5);
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var response = await _client.PatchAsync($"/api/sales/{id}/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        data.GetProperty("isCancelled").GetBoolean().Should().BeTrue();
        data.GetProperty("totalAmount").GetDecimal().Should().Be(0m);

        // Cancelling is not deleting: the sale is still there to be audited.
        (await _client.GetAsync($"/api/sales/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Cancelling an item should recalculate the sale total")]
    public async Task Given_SaleWithItem_When_ItemCancelled_Then_TotalIsRecalculated()
    {
        var created = await CreateSaleAsync("FUNC-0013", quantity: 5);
        var data = created.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        var itemId = data.GetProperty("items")[0].GetProperty("id").GetGuid();

        var response = await _client.PatchAsync($"/api/sales/{id}/items/{itemId}/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var updated = body.GetProperty("data");

        updated.GetProperty("totalAmount").GetDecimal().Should().Be(0m);

        // The sale itself stays active, and the cancelled line is still reported.
        updated.GetProperty("isCancelled").GetBoolean().Should().BeFalse();
        updated.GetProperty("items")[0].GetProperty("isCancelled").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "Cancelling a sale twice should answer 400")]
    public async Task Given_CancelledSale_When_CancelledAgain_Then_ReturnsBadRequest()
    {
        var created = await CreateSaleAsync("FUNC-0014", quantity: 2);
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        await _client.PatchAsync($"/api/sales/{id}/cancel", null);
        var response = await _client.PatchAsync($"/api/sales/{id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // A rule violation is reported distinctly from a malformed request.
        body.GetProperty("type").GetString().Should().Be("BusinessRuleViolation");
    }

    [Fact(DisplayName = "Cancelling an unknown item should answer 404")]
    public async Task Given_UnknownItem_When_Cancelled_Then_ReturnsNotFound()
    {
        var created = await CreateSaleAsync("FUNC-0015", quantity: 2);
        var id = created.GetProperty("data").GetProperty("id").GetGuid();

        var response = await _client.PatchAsync($"/api/sales/{id}/items/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Listing --------------------------------------------------------------

    [Fact(DisplayName = "Listing should return the documented pagination envelope")]
    public async Task Given_Sales_When_Listed_Then_ReturnsPaginationEnvelope()
    {
        await CreateSaleAsync("FUNC-LIST-1", quantity: 2);
        await CreateSaleAsync("FUNC-LIST-2", quantity: 2);

        var response = await _client.GetAsync("/api/sales?_page=1&_size=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        body.GetProperty("data").GetArrayLength().Should().Be(1);
        body.GetProperty("currentPage").GetInt32().Should().Be(1);
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("totalPages").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact(DisplayName = "Listing should honour a field filter")]
    public async Task Given_Filter_When_Listing_Then_FiltersResults()
    {
        await CreateSaleAsync("FUNC-FILTER-1", quantity: 2);

        var response = await _client.GetAsync("/api/sales?saleNumber=FUNC-FILTER-1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        body.GetProperty("data").GetArrayLength().Should().Be(1);
        body.GetProperty("data")[0].GetProperty("saleNumber").GetString().Should().Be("FUNC-FILTER-1");
    }

    [Fact(DisplayName = "Listing should honour a wildcard filter")]
    public async Task Given_WildcardFilter_When_Listing_Then_MatchesPartially()
    {
        await CreateSaleAsync("FUNC-WILD-1", quantity: 2);
        await CreateSaleAsync("FUNC-WILD-2", quantity: 2);

        var response = await _client.GetAsync("/api/sales?saleNumber=FUNC-WILD*");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact(DisplayName = "An unknown filter field should be ignored rather than rejected")]
    public async Task Given_UnknownFilterField_When_Listing_Then_StillSucceeds()
    {
        var response = await _client.GetAsync("/api/sales?notAField=whatever");

        // Failing a list request over one stray query parameter would be unhelpful.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -- Infrastructure -------------------------------------------------------

    [Fact(DisplayName = "The health endpoint should report the service as healthy")]
    public async Task Given_RunningApi_When_HealthChecked_Then_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
