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
    /// <summary>
    /// Retrieves the cache provider associated with the specified name.
    /// </summary>
    /// <remarks>
    /// If the requested provider is not configured, or if the configured provider cannot be initialized,
    /// the implementation returns its internal fallback provider. The fallback is not registered in the
    /// provider configuration collection and is not cached under the requested name.
    /// </remarks>
    /// <param name="name">
    /// The name of the cache provider to retrieve. Must not be null, empty, or contain only whitespace.
    /// </param>
    /// <returns>
    /// The configured cache provider instance, or the internal fallback provider when the requested provider
    /// is unavailable.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the provided name is null, empty, or contains only whitespace.
    /// </exception>
    ICacheProvider GetCacheProvider(string name);
}
