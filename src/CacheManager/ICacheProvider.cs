namespace CacheManager;

/// <summary>
/// Provides a contract for implementing a cache provider.
/// </summary>
/// <remarks>
/// The ICacheProvider interface defines the essential operations for interacting with a caching mechanism.
/// Implementations are responsible for providing in-memory or distributed caching functionalities.
/// This interface includes methods for retrieving, storing, removing, and clearing items from the cache
/// both synchronously and asynchronously.
/// </remarks>
public interface ICacheProvider
{
    /// <summary>
    /// Retrieves a value from the cache associated with the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the value to be retrieved.</typeparam>
    /// <param name="key">The key of the cached value to retrieve.</param>
    /// <returns>
    /// The cached value when available; otherwise, <c>default</c> for <typeparamref name="T"/>.
    /// Reference types return <c>null</c> on cache miss.
    /// </returns>
    T? Get<T>(string key);

    /// <summary>
    /// Retrieves an item asynchronously from the cache for the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the item to retrieve.</typeparam>
    /// <param name="key">The key identifying the cached item.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the cached value when
    /// available; otherwise, <c>default</c> for <typeparamref name="T"/>. Reference types return
    /// <c>null</c> on cache miss.
    /// </returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value in the cache using the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the value to be stored in the cache.</typeparam>
    /// <param name="key">The key associated with the value to store in the cache. The key must be a valid, non-empty string.</param>
    /// <param name="value">The value to store in the cache. Null values are ignored by implementations.</param>
    void Set<T>(string key, T value);

    /// <summary>
    /// Asynchronously sets the specified value in the underlying data store with the given key.
    /// </summary>
    /// <typeparam name="T">The type of the value to be stored in the cache.</typeparam>
    /// <param name="key">The unique identifier for the value to be set in the data store.</param>
    /// <param name="value">The value to be stored associated with the specified key. Null values are ignored by implementations.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the cache entry to remove. Must not be null, empty, or invalid.</param>
    void Remove(string key);

    /// <summary>
    /// Removes a cache entry corresponding to the specified key asynchronously.
    /// </summary>
    /// <param name="key">The key of the cache entry to be removed.</param>
    /// <param name="cancellationToken">
    /// The cancellation token to observe while waiting for the remove operation to complete.
    /// The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous remove operation.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears cached items that belong to the provider namespace.
    /// </summary>
    /// <remarks>
    /// Implementations scope cache entries by the provider namespace configured in
    /// <see cref="Settings.CacheProviderSettings.Namespace"/>.
    /// </remarks>
    void Clear();

    /// <summary>
    /// Clears cached items that belong to the provider namespace asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the operation to complete. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation to clear the cache.
    /// </returns>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
