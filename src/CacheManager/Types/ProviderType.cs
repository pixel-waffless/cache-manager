namespace CacheManager.Types;

/// <summary>
/// Represents the type of cache provider to be used.
/// </summary>
/// <remarks>
/// This enum is used to specify the cache provider type, such as in-memory or Redis.
/// </remarks>
public enum ProviderType
{
    /// <summary>
    /// InMemory Cache Provider
    /// </summary>
    Memory,

    /// <summary>
    /// Redis Cache Provider
    /// </summary>
    Redis
}