namespace Ambev.DeveloperEvaluation.Common.Caching;

/// <summary>
/// An <see cref="ICacheService"/> that stores nothing and always reports a miss.
/// </summary>
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
