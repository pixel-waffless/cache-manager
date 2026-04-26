namespace CacheManager;

/// <summary>
/// Defines a contract for managing and retrieving cache providers.
/// </summary>
/// <remarks>
/// Implementations of this interface are responsible for maintaining a collection of cache providers and providing
/// access to them by name. Cache providers can represent various caching mechanisms, such as in-memory or distributed caches.
/// </remarks>
public interface ICacheManager
{
    /// Retrieves the cache provider associated with the specified name.
    /// <param name="name">
    /// The name of the cache provider to retrieve. Must not be null, empty, or contain only whitespace.
    /// </param>
    /// <returns>The cache provider instance associated with the specified name.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the provided name is null, empty, or contains only whitespace.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no cache provider with the specified name is registered.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the cache provider type is not supported.
    /// </exception>
    ICacheProvider GetCacheProvider(string name);
}