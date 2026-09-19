using Ambev.DeveloperEvaluation.ORM.Common;
using FluentAssertions;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.ORM.Common;

/// <summary>
/// Contains unit tests for <see cref="QueryableExtensions"/>, which implements the
/// ordering and filtering conventions described in <c>.doc/general-api.md</c>.
/// </summary>
/// <remarks>
/// The tests run against <c>List&lt;T&gt;.AsQueryable()</c> rather than a database.
/// That still exercises the expression trees the extensions build (LINQ to Objects
/// compiles and executes them), while keeping the suite free of any infrastructure.
/// Whether those same trees translate to SQL is covered by the integration tests.
/// </remarks>
public class QueryableExtensionsTests
{
    /// <summary>
    /// A stand-in for an entity, shaped like the Sale aggregate: scalar columns of
    /// several types plus a nested object reached through a dotted path.
    /// </summary>
    private class Widget
    {
        public string Title { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Quantity { get; init; }
        public bool IsCancelled { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid ExternalId { get; init; }
        public WidgetStatus Status { get; init; }
        public string? Nickname { get; init; }
        public Owner Owner { get; init; } = new();
    }

    private class Owner
    {
        public string Name { get; init; } = string.Empty;
    }

    private enum WidgetStatus
    {
        Draft,
        Published
    }

    private static readonly Guid KnownId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Builds the fixed data set every test queries against.
    /// </summary>
    private static IQueryable<Widget> Widgets() => new List<Widget>
    {
        new() { Title = "Alpha Backpack", Price = 30m, Quantity = 5,  IsCancelled = false,
                CreatedAt = new DateTime(2024, 01, 10), ExternalId = KnownId,
                Status = WidgetStatus.Published, Nickname = "ace",  Owner = new Owner { Name = "Downtown" } },
        new() { Title = "Beta Backpack",  Price = 20m, Quantity = 15, IsCancelled = true,
                CreatedAt = new DateTime(2024, 06, 15), ExternalId = Guid.NewGuid(),
                Status = WidgetStatus.Draft, Nickname = null,     Owner = new Owner { Name = "Uptown" } },
        new() { Title = "Gamma Satchel",  Price = 30m, Quantity = 1,  IsCancelled = false,
                CreatedAt = new DateTime(2024, 12, 01), ExternalId = Guid.NewGuid(),
                Status = WidgetStatus.Published, Nickname = "gem",  Owner = new Owner { Name = "Downtown" } }
    }.AsQueryable();

    // -- Ordering ------------------------------------------------------------

    [Fact(DisplayName = "Ordering should sort ascending when no direction is given")]
    public void Given_FieldWithoutDirection_When_Ordering_Then_SortsAscending()
    {
        var result = Widgets().ApplyOrdering("title").ToList();

        result.Select(w => w.Title)
            .Should().ContainInOrder("Alpha Backpack", "Beta Backpack", "Gamma Satchel");
    }

    [Fact(DisplayName = "Ordering should sort descending when desc is given")]
    public void Given_DescDirection_When_Ordering_Then_SortsDescending()
    {
        var result = Widgets().ApplyOrdering("price desc").ToList();

        result.First().Price.Should().Be(30m);
        result.Last().Price.Should().Be(20m);
    }

    [Fact(DisplayName = "Ordering should apply multiple fields in sequence")]
    public void Given_MultipleFields_When_Ordering_Then_AppliesThenBy()
    {
        // Alpha and Gamma share a price of 30; the second term breaks the tie, so
        // Gamma must precede Alpha. This is what distinguishes a correct ThenBy
        // from a second OrderBy that discards the first ordering.
        var result = Widgets().ApplyOrdering("price desc, title desc").ToList();

        result.Select(w => w.Title)
            .Should().ContainInOrder("Gamma Satchel", "Alpha Backpack", "Beta Backpack");
    }

    [Fact(DisplayName = "Ordering should tolerate a quoted clause")]
    public void Given_QuotedClause_When_Ordering_Then_StripsQuotes()
    {
        // .doc/general-api.md shows _order="price desc, title asc" with the quotes
        // included, so they can arrive as part of the value.
        var result = Widgets().ApplyOrdering("\"price desc, title asc\"").ToList();

        result.First().Price.Should().Be(30m);
    }

    [Fact(DisplayName = "Ordering should ignore an unknown field")]
    public void Given_UnknownField_When_Ordering_Then_LeavesQueryUnchanged()
    {
        var result = Widgets().ApplyOrdering("doesNotExist desc").ToList();

        result.Should().HaveCount(3);
        result.First().Title.Should().Be("Alpha Backpack");
    }

    [Theory(DisplayName = "Ordering should leave the query untouched for empty input")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyOrder_When_Ordering_Then_LeavesQueryUnchanged(string? order)
    {
        var result = Widgets().ApplyOrdering(order).ToList();

        result.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Ordering should resolve a field by its JSON casing")]
    public void Given_CamelCaseField_When_Ordering_Then_ResolvesProperty()
    {
        // Responses serialise CreatedAt as createdAt, and the doc says to use the
        // field names in the same format as the JSON response.
        var result = Widgets().ApplyOrdering("createdAt desc").ToList();

        result.First().Title.Should().Be("Gamma Satchel");
    }

    // -- Filtering: exact match ----------------------------------------------

    [Fact(DisplayName = "Filtering should match a string field exactly")]
    public void Given_ExactStringValue_When_Filtering_Then_ReturnsMatch()
    {
        var result = Filter(("title", "Gamma Satchel"));

        result.Should().ContainSingle().Which.Title.Should().Be("Gamma Satchel");
    }

    [Fact(DisplayName = "Filtering should match a boolean field")]
    public void Given_BooleanValue_When_Filtering_Then_ReturnsMatch()
    {
        var result = Filter(("isCancelled", "true"));

        result.Should().ContainSingle().Which.Title.Should().Be("Beta Backpack");
    }

    [Fact(DisplayName = "Filtering should match an enum field by name")]
    public void Given_EnumName_When_Filtering_Then_ReturnsMatch()
    {
        var result = Filter(("status", "Draft"));

        result.Should().ContainSingle().Which.Title.Should().Be("Beta Backpack");
    }

    [Fact(DisplayName = "Filtering should match a Guid field")]
    public void Given_GuidValue_When_Filtering_Then_ReturnsMatch()
    {
        var result = Filter(("externalId", KnownId.ToString()));

        result.Should().ContainSingle().Which.Title.Should().Be("Alpha Backpack");
    }

    [Fact(DisplayName = "Filtering should resolve a dotted path into a nested object")]
    public void Given_DottedPath_When_Filtering_Then_ResolvesNestedProperty()
    {
        // Sale exposes customer and branch as owned value objects, so the query
        // string needs a way to reach customer.name.
        var result = Filter(("owner.name", "Downtown"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(w => w.Owner.Name == "Downtown");
    }

    // -- Filtering: wildcards ------------------------------------------------

    [Fact(DisplayName = "Filtering should treat a trailing asterisk as starts-with")]
    public void Given_TrailingAsterisk_When_Filtering_Then_MatchesPrefix()
    {
        var result = Filter(("title", "Alpha*"));

        result.Should().ContainSingle().Which.Title.Should().Be("Alpha Backpack");
    }

    [Fact(DisplayName = "Filtering should treat a leading asterisk as ends-with")]
    public void Given_LeadingAsterisk_When_Filtering_Then_MatchesSuffix()
    {
        var result = Filter(("title", "*Backpack"));

        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Filtering should treat surrounding asterisks as contains")]
    public void Given_SurroundingAsterisks_When_Filtering_Then_MatchesSubstring()
    {
        var result = Filter(("title", "*ack*"));

        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Wildcard matching should be case-insensitive")]
    public void Given_DifferentCase_When_WildcardFiltering_Then_StillMatches()
    {
        var result = Filter(("title", "alpha*"));

        result.Should().ContainSingle();
    }

    [Fact(DisplayName = "Wildcard matching should not throw on a null column")]
    public void Given_NullColumn_When_WildcardFiltering_Then_SkipsRowWithoutThrowing()
    {
        // Beta Backpack has a null Nickname. Without the null guard in the built
        // predicate this call throws instead of simply not matching that row.
        var act = () => Filter(("nickname", "*e*"));

        act.Should().NotThrow();
        act().Should().HaveCount(2);
    }

    // -- Filtering: ranges ---------------------------------------------------

    [Fact(DisplayName = "Filtering should apply _min as greater than or equal")]
    public void Given_MinPrefix_When_Filtering_Then_AppliesLowerBound()
    {
        var result = Filter(("_minQuantity", "5"));

        // Inclusive: the widget with exactly 5 must be kept.
        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Quantity == 5);
    }

    [Fact(DisplayName = "Filtering should apply _max as less than or equal")]
    public void Given_MaxPrefix_When_Filtering_Then_AppliesUpperBound()
    {
        var result = Filter(("_maxQuantity", "5"));

        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Quantity == 5);
    }

    [Fact(DisplayName = "Filtering should combine _min and _max into a range")]
    public void Given_MinAndMax_When_Filtering_Then_AppliesBothBounds()
    {
        var result = Filter(("_minPrice", "25"), ("_maxPrice", "35"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(w => w.Price == 30m);
    }

    [Fact(DisplayName = "Filtering should apply a date range")]
    public void Given_DateRange_When_Filtering_Then_AppliesBounds()
    {
        var result = Filter(("_minCreatedAt", "2024-05-01"));

        result.Should().HaveCount(2);
        result.Should().NotContain(w => w.Title == "Alpha Backpack");
    }

    [Fact(DisplayName = "Filtering should combine multiple terms with AND")]
    public void Given_MultipleFilters_When_Filtering_Then_CombinesWithAnd()
    {
        var result = Filter(("owner.name", "Downtown"), ("_minPrice", "25"), ("isCancelled", "false"));

        result.Should().HaveCount(2);
    }

    // -- Filtering: resilience -----------------------------------------------

    [Fact(DisplayName = "Filtering should ignore pagination keys")]
    public void Given_ReservedKeys_When_Filtering_Then_IgnoresThem()
    {
        // _page, _size and _order share the query string with field filters and
        // must never be interpreted as column names.
        var result = Filter(("_page", "2"), ("_size", "50"), ("_order", "title desc"));

        result.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Filtering should ignore an unknown field")]
    public void Given_UnknownField_When_Filtering_Then_IgnoresTerm()
    {
        var result = Filter(("doesNotExist", "whatever"));

        result.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Filtering should ignore a value it cannot convert")]
    public void Given_UnparseableValue_When_Filtering_Then_IgnoresTerm()
    {
        // "abc" is not a number. The term is dropped rather than failing the whole
        // request with a 500.
        var result = Filter(("_minPrice", "abc"));

        result.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Filtering should ignore a term with an empty value")]
    public void Given_EmptyValue_When_Filtering_Then_IgnoresTerm()
    {
        var result = Filter(("title", ""));

        result.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Filtering should leave the query untouched for an empty filter set")]
    public void Given_NoFilters_When_Filtering_Then_LeavesQueryUnchanged()
    {
        var result = Widgets()
            .ApplyFilters(new Dictionary<string, string?>())
            .ToList();

        result.Should().HaveCount(3);
    }

    /// <summary>
    /// Applies the given query string pairs as filters and materializes the result.
    /// </summary>
    private static List<Widget> Filter(params (string Key, string? Value)[] pairs) =>
        Widgets()
            .ApplyFilters(pairs.ToDictionary(p => p.Key, p => p.Value))
            .ToList();
}
