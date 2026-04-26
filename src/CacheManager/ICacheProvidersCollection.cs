using CacheManager.Settings;

namespace CacheManager;

/// <summary>
/// Represents a collection of cache providers and their associated settings.
/// </summary>
/// <remarks>
/// This is responsible for managing the collection of cache provider configurations
/// by providing functionality to add, retrieve, and remove provider settings.
/// Each provider is identified by a unique string key.
/// </remarks>
public interface ICacheProvidersCollection
{
    /// <summary>
    /// Attempts to add a new cache provider to the collection.
    /// </summary>
    /// <param name="name">The unique name of the cache provider to be added.</param>
    /// <param name="settings">The <see cref="CacheProviderSettings"/> object containing the configuration for the cache provider.</param>
    /// <returns>
    /// Returns true if the cache provider is successfully added to the collection;
    /// otherwise, returns false if a provider with the same name already exists.
    /// </returns>
    bool TryAddProvider(string name, CacheProviderSettings settings);

    /// <summary>
    /// Attempts to retrieve the configuration settings for a cache provider with the specified name.
    /// </summary>
    /// <param name="name">The name of the cache provider to look for.</param>
    /// <param name="settings">
    /// When this method returns, contains the <see cref="CacheProviderSettings"/> associated with the specified name,
    /// if the name exists in the collection; otherwise, it contains the default value for the type.
    /// </param>
    /// <returns>
    /// Returns true if a cache provider with the specified name exists in the collection; otherwise, false.
    /// </returns>
    bool TryGetProvider(string name, out CacheProviderSettings? settings);

    /// <summary>
    /// Attempts to remove a cache provider by its name from the collection.
    /// </summary>
    /// <param name="name">The name of the cache provider to be removed.</param>
    /// <returns>
    /// A boolean value indicating whether the cache provider was successfully removed or not.
    /// Returns true if the cache provider was found and removed; otherwise, false.
    /// </returns>
    bool TryRemoveProvider(string name);
}
