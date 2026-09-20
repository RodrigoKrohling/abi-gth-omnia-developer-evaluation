using Ambev.DeveloperEvaluation.Common.Logging;
using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Ambev.DeveloperEvaluation.Unit.Common.Logging;

/// <summary>
/// Contains unit tests for <see cref="LogEventFilter"/>.
/// </summary>
/// <remarks>
/// A filter that wrongly drops an event leaves no trace of having done so - the
/// application just looks quiet - so the levels are asserted explicitly.
/// </remarks>
public class LogEventFilterTests
{
    /// <summary>
    /// Builds a log event at the given level, optionally carrying the properties a
    /// request-completion entry would have.
    /// </summary>
    /// <param name="level">The level to raise the event at.</param>
    /// <param name="statusCode">The StatusCode property, or null to omit it.</param>
    /// <param name="path">The Path property, or null to omit it.</param>
    /// <param name="statusCodeAsText">
    /// When true, StatusCode is logged as a string rather than an integer. Which of
    /// the two occurs depends on the middleware doing the logging, and Serilog
    /// renders a string scalar with quotation marks around it - so a rule that
    /// compares the rendered text has to cope with both.
    /// </param>
    private static LogEvent CreateEvent(
        LogEventLevel level,
        int? statusCode = null,
        string? path = null,
        bool statusCodeAsText = false)
    {
        var properties = new List<LogEventProperty>();

        if (statusCode is not null)
        {
            var value = statusCodeAsText
                ? new ScalarValue(statusCode.Value.ToString())
                : new ScalarValue(statusCode.Value);

            properties.Add(new LogEventProperty("StatusCode", value));
        }

        if (path is not null)
            properties.Add(new LogEventProperty("Path", new ScalarValue(path)));

        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception: null,
            new MessageTemplate("a message", []),
            properties);
    }

    // -- Levels ---------------------------------------------------------------

    [Theory(DisplayName = "An event above Information should never be dropped")]
    [InlineData(LogEventLevel.Warning)]
    [InlineData(LogEventLevel.Error)]
    [InlineData(LogEventLevel.Fatal)]
    public void Given_ALevelAboveInformation_When_Filtered_Then_ItIsKept(LogEventLevel level)
    {
        // The defect this exists to catch. ExceptionHandlingMiddleware logs unhandled
        // exceptions with LogError, MongoDomainEventStore logs a failed audit write
        // with LogError, and CompositeDomainEventPublisher logs a publisher that threw
        // with LogError. Dropping this level made all three write into nothing.
        LogEventFilter.ShouldExclude(CreateEvent(level)).Should().BeFalse();
    }

    [Theory(DisplayName = "An event above Information should survive even on a health probe")]
    [InlineData(LogEventLevel.Warning)]
    [InlineData(LogEventLevel.Error)]
    public void Given_AFailureOnHealth_When_Filtered_Then_ItIsKept(LogEventLevel level)
    {
        // The health suppression is about routine success. A health endpoint that
        // starts failing is exactly what an operator needs to see.
        var logEvent = CreateEvent(level, statusCode: 500, path: "/health");

        LogEventFilter.ShouldExclude(logEvent).Should().BeFalse();
    }

    [Theory(DisplayName = "Verbose and Debug should not be dropped by this filter")]
    [InlineData(LogEventLevel.Verbose)]
    [InlineData(LogEventLevel.Debug)]
    public void Given_ALevelBelowInformation_When_Filtered_Then_ItIsKept(LogEventLevel level)
    {
        // Whether these are emitted at all is a minimum-level decision, taken in
        // configuration. It is not this filter's job to second-guess it.
        LogEventFilter.ShouldExclude(CreateEvent(level)).Should().BeFalse();
    }

    // -- The health probe, which is the one thing worth dropping ---------------

    [Fact(DisplayName = "A successful health probe should be dropped")]
    public void Given_ASuccessfulHealthProbe_When_Filtered_Then_ItIsDropped()
    {
        var logEvent = CreateEvent(LogEventLevel.Information, statusCode: 200, path: "/health");

        LogEventFilter.ShouldExclude(logEvent).Should().BeTrue();
    }

    [Theory(DisplayName = "The liveness and readiness probes should be dropped too")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH")]
    public void Given_AProbeVariant_When_Filtered_Then_ItIsDropped(string path)
    {
        var logEvent = CreateEvent(LogEventLevel.Information, statusCode: 200, path: path);

        LogEventFilter.ShouldExclude(logEvent).Should().BeTrue();
    }

    [Fact(DisplayName = "A probe logged with a textual status code should be dropped too")]
    public void Given_ATextualStatusCode_When_Filtered_Then_ItIsDropped()
    {
        // Serilog renders a string scalar as "200", quotation marks included, and an
        // integer as 200 without them. The original rule compared the rendered text
        // straight against "200", so it recognised one shape and silently failed on
        // the other - and which shape occurs is decided by whichever middleware did
        // the logging, not by this rule.
        var logEvent = CreateEvent(
            LogEventLevel.Information,
            statusCode: 200,
            path: "/health",
            statusCodeAsText: true);

        LogEventFilter.ShouldExclude(logEvent).Should().BeTrue();
    }

    [Fact(DisplayName = "A failing health probe should be kept")]
    public void Given_AFailingHealthProbe_When_Filtered_Then_ItIsKept()
    {
        var logEvent = CreateEvent(LogEventLevel.Information, statusCode: 503, path: "/health");

        LogEventFilter.ShouldExclude(logEvent).Should().BeFalse();
    }

    // -- Ordinary traffic and ordinary messages --------------------------------

    [Fact(DisplayName = "A successful business request should be kept")]
    public void Given_ASuccessfulSaleRequest_When_Filtered_Then_ItIsKept()
    {
        var logEvent = CreateEvent(LogEventLevel.Information, statusCode: 200, path: "/api/sales");

        LogEventFilter.ShouldExclude(logEvent).Should().BeFalse();
    }

    [Fact(DisplayName = "An application message carrying neither property should be kept")]
    public void Given_APlainInformationMessage_When_Filtered_Then_ItIsKept()
    {
        // "Domain event published: SaleCreatedEvent" and friends. These carry no
        // StatusCode and no Path, and must not be mistaken for a successful probe
        // on the grounds that they have no status code to disagree with.
        LogEventFilter.ShouldExclude(CreateEvent(LogEventLevel.Information)).Should().BeFalse();
    }

    [Fact(DisplayName = "An event with a status code but no path should be kept")]
    public void Given_AStatusCodeWithoutAPath_When_Filtered_Then_ItIsKept()
    {
        var logEvent = CreateEvent(LogEventLevel.Information, statusCode: 200);

        LogEventFilter.ShouldExclude(logEvent).Should().BeFalse();
    }

    [Fact(DisplayName = "An event on the health path but with no status code should be kept")]
    public void Given_APathWithoutAStatusCode_When_Filtered_Then_ItIsKept()
    {
        var logEvent = CreateEvent(LogEventLevel.Information, path: "/health");

        LogEventFilter.ShouldExclude(logEvent).Should().BeFalse();
    }

    [Fact(DisplayName = "A null event should be rejected rather than silently kept")]
    public void Given_ANullEvent_When_Filtered_Then_ItThrows()
    {
        var act = () => LogEventFilter.ShouldExclude(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
