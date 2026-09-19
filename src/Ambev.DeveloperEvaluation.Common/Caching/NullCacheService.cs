namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// An <see cref="ICacheService"/> that stores nothing and always reports a miss.
/// </summary>
/// <remarks>
/// Registered when caching is switched off, so that callers never have to ask
/// whether a cache exists. The alternative - a nullable <see cref="ICacheService"/>
/// and a null check at every call site - spreads the configuration decision across
/// every handler that reads.
///
/// This is the Null Object pattern, and it is also what the functional tests run
/// against: an API under test should not depend on Redis being up, and its results
/// should not vary with what a previous test happened to leave cached.
/// </remarks>
public class NullCacheService : ICacheService
{
    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class => Task.FromResult<T?>(null);

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
        where T : class => Task.CompletedTask;

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
