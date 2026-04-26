namespace CacheManager.Settings;

/// <summary>
/// Represents the root cache configuration section.
/// </summary>
/// <remarks>
/// The settings are typically bound from a <c>CacheSettings</c> configuration section
/// and contain the list of named cache providers available to the cache manager.
/// </remarks>
public class CacheSettings
{
    /// <summary>
    /// Gets or sets the collection of cache provider settings.
    /// </summary>
    /// <remarks>
    /// This property contains the configuration for all cache providers to be used in the application.
    /// Each provider in this collection includes the necessary details such as type, name, connection string,
    /// expiration policy, and thresholds. Only valid providers will be used during application initialization.
    /// </remarks>
    public List<CacheProviderSettings> Providers { get; set; } = [];
}
