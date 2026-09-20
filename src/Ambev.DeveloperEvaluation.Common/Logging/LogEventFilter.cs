using Serilog.Events;

namespace Ambev.DeveloperEvaluation.Common.Logging;

/// <summary>
/// Decides which log events are noise and can be dropped.
/// </summary>
/// <remarks>
/// The only thing suppressed is the successful health probe: an orchestrator polls
/// <c>/health</c> every few seconds and each poll produces a request-completion
/// entry. Nothing else is treated as noise.
/// </remarks>
public static class LogEventFilter
{
    /// <summary>
    /// Returns whether a log event should be dropped rather than written to a sink.
    /// </summary>
    /// <param name="logEvent">The event being considered.</param>
    /// <returns>
    /// <see langword="true"/> to discard the event, <see langword="false"/> to keep it.
    /// </returns>
    /// <remarks>
    /// Mind the sense of the return value: Serilog's <c>Filter.ByExcluding</c>
    /// discards an event when the predicate returns <see langword="true"/>, so
    /// <see langword="false"/> is the safe default and only a recognised pattern
    /// returns <see langword="true"/>.
    /// </remarks>
    public static bool ShouldExclude(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Warnings and above always survive. Only routine Information is a candidate
        // for suppression.
        if (logEvent.Level != LogEventLevel.Information)
            return false;

        logEvent.Properties.TryGetValue("StatusCode", out var statusCode);
        logEvent.Properties.TryGetValue("Path", out var path);

        // A request-completion entry carries both properties. An ordinary
        // application message carries neither, and must not be mistaken for a
        // successful probe just because it has no status code.
        if (statusCode is null || path is null)
            return false;

        var succeeded = Render(statusCode).Equals("200", StringComparison.Ordinal);
        var isHealthProbe = Render(path).Contains("/health", StringComparison.OrdinalIgnoreCase);

        return succeeded && isHealthProbe;
    }

    /// <summary>
    /// Renders a property value as plain text, without the quoting Serilog applies
    /// to string scalars.
    /// </summary>
    /// <param name="value">The property value to render.</param>
    /// <returns>The value as text, unquoted.</returns>
    /// <remarks>
    /// <see cref="ScalarValue.ToString()"/> renders a string as <c>"200"</c>, quotation
    /// marks included, and an integer as <c>200</c> without them. Which shape a
    /// property holds depends on the middleware that logged it, so the text has to be
    /// unwrapped before it is compared.
    /// </remarks>
    private static string Render(LogEventPropertyValue value)
    {
        var text = value is ScalarValue { Value: not null } scalar
            ? scalar.Value.ToString() ?? string.Empty
            : value.ToString();

        return text;
    }
}
