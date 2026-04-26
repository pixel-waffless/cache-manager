using CacheManager.Settings;
using StackExchange.Redis;

namespace CacheManager.Redis;

/// <summary>
/// Provides shared Redis connections for configured cache providers.
/// </summary>
/// <remarks>
/// Implementations are responsible for reusing Redis connections by provider configuration
/// and disposing those connections when the factory is disposed by the dependency injection container.
/// </remarks>
public interface IRedisConnectionFactory : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets or creates a Redis connection for the specified provider settings.
    /// </summary>
    /// <param name="settings">The cache provider settings that identify and configure the Redis connection.</param>
    /// <returns>A shared Redis connection for the configured provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the provider name or Redis connection string is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the factory has already been disposed.</exception>
    IConnectionMultiplexer GetConnection(CacheProviderSettings settings);
}
