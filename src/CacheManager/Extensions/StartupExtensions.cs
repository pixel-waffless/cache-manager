using CacheManager.Redis;
using CacheManager.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CacheManager.Extensions;

/// <summary>
/// Provides extension methods for configuring and adding cache management services
/// to the dependency injection container.
/// </summary>
public static class StartupExtensions
{
    private const string LoggerId = "[StartupExtensions]";

    /// <summary>
    /// Adds the cache manager services and provider configuration to the dependency injection container.
    /// </summary>
    /// <remarks>
    /// When <paramref name="settings"/> is not provided, configuration is bound from the
    /// <c>CacheSettings</c> section. The method validates configured providers, registers
    /// valid provider settings in an <see cref="ICacheProvidersCollection"/>, adds memory cache
    /// support, and registers Redis connection management through a singleton
    /// <see cref="IRedisConnectionFactory"/>.
    /// </remarks>
    /// <param name="services">The service collection to which the cache manager is added.</param>
    /// <param name="configuration">The configuration source used to bind cache settings.</param>
    /// <param name="logger">Optional logger to log information or warnings during the setup process.</param>
    /// <param name="settings">Optional action to define custom cache settings.</param>
    /// <returns>The updated service collection to support method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddCacheManager(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger? logger = null,
        Action<CacheSettings>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cacheSettings = new CacheSettings();

        if (settings == null)
            configuration.GetSection(nameof(CacheSettings)).Bind(cacheSettings);
        else
            settings.Invoke(cacheSettings);

        if (cacheSettings.Providers == null || cacheSettings.Providers.Count == 0)
        {
            logger?.LogWarning("{logger} No cache providers configured", LoggerId);
            logger?.LogWarning("{logger} Whole service will fall-back to Default In-Memory Cache", LoggerId);
        }

        var validProviders = new List<CacheProviderSettings>();

        if (cacheSettings.Providers != null)
            foreach (var provider in cacheSettings.Providers)
            {
                var errors = provider.Validate();
                if (errors.Length > 0)
                {
                    logger?.LogWarning("{logger} Cache provider {name} with errors skipped due to: {error}", LoggerId,
                        provider.Name, string.Join(", ", errors));
                    continue;
                }

                validProviders.Add(provider);
            }

        switch (validProviders.Count)
        {
            case 0:
                logger?.LogWarning("{logger} No valid cache providers configured", LoggerId);
                break;
            case > 0:
                logger?.LogInformation("{logger} {count} cache providers configurations found", LoggerId,
                    validProviders.Count);
                break;
        }

        var providersCollection = new CacheProvidersCollection();

        foreach (var provider in validProviders)
        {
            var added = providersCollection.TryAddProvider(provider.Name, provider);

            if (!added)
                logger?.LogWarning("{logger} Cache provider '{name}' already exists. Skipping.", LoggerId, provider.Name);
        }

        services.AddSingleton<ICacheProvidersCollection>(providersCollection);
        services.TryAddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();
        services.AddMemoryCache();
        services.AddSingleton<ICacheManager, CacheManager>();
        return services;
    }
}
