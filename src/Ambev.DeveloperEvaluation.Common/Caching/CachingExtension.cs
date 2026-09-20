using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// Registers the read-model cache.
/// </summary>
/// <remarks>
/// Follows the shape of the template's other cross-cutting registrations, such as
/// <c>HealthChecksExtension</c> and <c>AuthenticationExtension</c>: one extension
/// method that <c>Program.cs</c> calls, keeping the composition root readable.
/// </remarks>
public static class CachingExtension
{
    /// <summary>
    /// Adds the cache implementation selected by configuration.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <remarks>
    /// Whether caching is on is decided here and nowhere else. Every consumer takes
    /// an <see cref="ICacheService"/> and is unaware of which implementation it got,
    /// so switching Redis off is a configuration change rather than a code path.
    /// </remarks>
    public static void AddReadModelCache(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration
            .GetSection(CacheSettings.SectionName)
            .Get<CacheSettings>() ?? new CacheSettings();

        builder.Services.Configure<CacheSettings>(
            builder.Configuration.GetSection(CacheSettings.SectionName));

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            builder.Services.AddSingleton<ICacheService, NullCacheService>();
            return;
        }

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = settings.ConnectionString;
            options.InstanceName = settings.InstanceName;
        });

        builder.Services.AddSingleton<ICacheService, DistributedCacheService>();
    }
}
