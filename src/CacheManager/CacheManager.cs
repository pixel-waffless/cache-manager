using System.Collections.Concurrent;
using CacheManager.Memory;
using CacheManager.Redis;
using CacheManager.Settings;
using CacheManager.Types;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheManager;

/// <summary>
/// The CacheManager class is responsible for managing and providing cache providers based on specified configurations.
/// It allows retrieving an appropriate cache provider instance by name, supporting both in-memory and Redis cache types.
/// </summary>
/// <remarks>
/// This class implements the <see cref="ICacheManager"/> interface.
/// </remarks>
/// <example>
/// Instances of CacheManager typically rely on dependency injection. The providers' configuration is passed
/// during dependency injection setup using the settings provided in <see cref="CacheProviderSettings"/>.
/// </example>
public class CacheManager : ICacheManager
{
    private const string LoggerId = "[CacheManager]";

    /// <summary>
    /// Represents the logger used for logging messages and events within the <see cref="CacheManager"/> class.
    /// </summary>
    /// <remarks>
    /// This logger is provided by the dependency injection container and is specific to the <see cref="CacheManager"/>.
    /// It is used to log informational messages, warnings, errors, or debug traces related to cache provider operations
    /// such as initialization, retrieval, or errors.
    /// </remarks>
    private readonly ILogger<CacheManager> _logger;

    /// <summary>
    /// A thread-safe dictionary that stores and manages registered cache providers.
    /// </summary>
    /// <remarks>
    /// Each entry in the dictionary maps a cache provider name to its corresponding implementation of <see cref="ICacheProvider"/>.
    /// The cache provider is instantiated based on the configuration provided in <see cref="CacheProviderSettings"/>.
    /// New entries are added lazily and only when a cache provider is accessed via <see cref="CacheManager.GetCacheProvider"/>.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ICacheProvider> _cacheProviders;

    /// <summary>
    /// Provides access to the application's dependency injection container for resolving required services.
    /// Used for managing and instantiating various cache providers based on application settings.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    private readonly ICacheProvidersCollection _cacheProvidersCollection;
    private readonly Lazy<ICacheProvider> _fallbackProvider;

    /// Manages cache providers and their configurations.
    /// Provides functionality to retrieve specific cache providers
    /// based on their names and types.
    /// This class supports managing and retrieving multiple cache
    /// providers, including in-memory and Redis, based on the provider
    /// settings configured during initialization.
    public CacheManager(ILogger<CacheManager> logger, IServiceProvider serviceProvider, ICacheProvidersCollection cacheProvidersCollection)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _cacheProvidersCollection = cacheProvidersCollection;
        _cacheProviders = new ConcurrentDictionary<string, ICacheProvider>(StringComparer.OrdinalIgnoreCase);
        _fallbackProvider = new Lazy<ICacheProvider>(CreateFallbackProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Retrieves a cache provider by its name.
    /// </summary>
    /// <remarks>
    /// Configured providers are created lazily and cached by provider name using a case-insensitive lookup.
    /// If the provider is missing or cannot be initialized, this method returns the internal fallback provider
    /// without caching that fallback under the requested provider name.
    /// </remarks>
    /// <param name="name">The unique name of the cache provider.</param>
    /// <returns>
    /// An instance of the configured cache provider, or the internal fallback provider when the requested
    /// provider is unavailable.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the cache provider name is null, empty, or consists only of whitespace.</exception>
    public ICacheProvider GetCacheProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Cache provider name cannot be null or empty.", nameof(name));

        if (_cacheProviders.TryGetValue(name, out var cacheProvider))
            return cacheProvider;

        _cacheProvidersCollection.TryGetProvider(name, out var setting);
        if (setting == null)
        {
            _logger.LogWarning("{logger} Cache provider '{name}' not found in configuration. Using fallback.", LoggerId, name);
            return GetFallBackProvider();
        }

        try
        {
            var createdProvider = CreateCacheProvider(setting);
            return _cacheProviders.GetOrAdd(setting.Name, createdProvider);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error initializing cache provider '{name}'. Using fallback. Error: {error}",
                LoggerId, name, e.Message);
            return GetFallBackProvider();
        }
    }

    /// <summary>
    /// Creates a specific cache provider instance based on the provided settings.
    /// </summary>
    /// <param name="setting">The settings used to create the cache provider.</param>
    /// <returns>An instance of the cache provider corresponding to the given name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the cache provider type is unsupported.</exception>
    private ICacheProvider CreateCacheProvider(CacheProviderSettings setting)
    {
        return setting.Type switch
        {
            ProviderType.Memory => GetInMemoryCacheProvider(setting),
            ProviderType.Redis => GetRedisCacheProvider(setting),
            _ => throw new ArgumentOutOfRangeException($"Unsupported cache provider type: {setting.Type}")
        };
    }

    /// <summary>
    /// Provides the lazy fallback cache provider for scenarios where a configured provider is unavailable.
    /// </summary>
    /// <remarks>
    /// The fallback is an in-memory provider named <c>Fallback</c>, scoped to the <c>Fallback</c> namespace,
    /// with a 1440-minute expiration.
    /// </remarks>
    /// <returns>An instance of the in-memory cache provider configured as a fallback.</returns>
    private ICacheProvider GetFallBackProvider()
    {
        return _fallbackProvider.Value;
    }

    private ICacheProvider CreateFallbackProvider()
    {
        var fallbackSettings = new CacheProviderSettings
        {
            Name = "Fallback",
            Namespace = "Fallback",
            Type = ProviderType.Memory,
            ExpirationMinutes = 1440
        };

        return GetInMemoryCacheProvider(fallbackSettings);
    }

    /// <summary>
    /// Retrieves an instance of <see cref="InMemoryCacheProvider"/> configured with the specified settings.
    /// </summary>
    /// <param name="setting">The configuration settings for the in-memory cache provider.</param>
    /// <returns>An instance of <see cref="InMemoryCacheProvider"/> configured according to the provided settings.</returns>
    private InMemoryCacheProvider GetInMemoryCacheProvider(CacheProviderSettings setting)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<InMemoryCacheProvider>>();
        var memoryCache = _serviceProvider.GetRequiredService<IMemoryCache>();
        return new InMemoryCacheProvider(logger, memoryCache, setting);
    }

    /// <summary>
    /// Creates and returns an instance of a RedisCacheProvider based on the provided settings.
    /// </summary>
    /// <param name="settings">The settings to configure the Redis cache provider.</param>
    /// <returns>An instance of RedisCacheProvider initialized with the specified settings.</returns>
    private RedisCacheProvider GetRedisCacheProvider(CacheProviderSettings settings)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<RedisCacheProvider>>();
        var redisConnectionFactory = _serviceProvider.GetRequiredService<IRedisConnectionFactory>();
        return new RedisCacheProvider(logger, redisConnectionFactory, settings);
    }
}
