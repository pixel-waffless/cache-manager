using System.Collections.Concurrent;
using CacheManager.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CacheManager.Memory;

/// <summary>
/// Provides an in-memory implementation of the <see cref="ICacheProvider"/> interface using <see cref="IMemoryCache"/>.
/// </summary>
/// <remarks>
/// The InMemoryCacheProvider class manages cache operations such as retrieving, storing, removing, and clearing cached entries.
/// Cache expiration is managed based on the Time-to-Live (TTL) settings provided in the configuration.
/// </remarks>
public class InMemoryCacheProvider(ILogger<InMemoryCacheProvider> logger, IMemoryCache memoryCache, CacheProviderSettings settings) : ICacheProvider
{
    /// <summary>
    /// Represents the default expiration time, in minutes, for cached items.
    /// If no specific expiration time is provided, the value of this constant
    /// will be used to determine the time-to-live (TTL) for cache entries.
    /// </summary>
    private const int DefaultExpirationMinutes = 1440;

    /// <summary>
    /// Identifier used to associate log messages with the <see cref="InMemoryCacheProvider"/> class.
    /// This unique identifier helps distinguish logs generated within this provider from logs of other components.
    /// </summary>
    private const string LoggerId = "[CacheProvider][InMemoryCacheProvider]";

    /// <summary>
    /// Represents the time-to-live (TTL) duration used for cached items in the in-memory cache.
    /// Determines the expiration time for cache entries if not provided explicitly.
    /// </summary>
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(settings.ExpirationMinutes is > 0
        ? settings.ExpirationMinutes.Value
        : DefaultExpirationMinutes);
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new();
    private readonly string _namespacePrefix = BuildNamespacePrefix(settings);

    /// <summary>
    /// Retrieves a value of the specified type from the in-memory cache based on the provided key.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve from the cache.</typeparam>
    /// <param name="key">The unique key associated with the cached value.</param>
    /// <returns>
    /// The value of type <typeparamref name="T"/> if found in the cache; otherwise, null.
    /// </returns>
    public T? Get<T>(string key)
    {
        if (!ValidateKey(key))
            return default;

        var cacheKey = BuildCacheKey(key);

        try
        {
            return memoryCache.TryGetValue(cacheKey, out T? value) ? value : default;
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error getting value from cache. Key: {key} Exception: {exception}", LoggerId, key, e);
            return default;
        }
    }

    /// <summary>
    /// Retrieves an item asynchronously from the in-memory cache for the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the item to retrieve.</typeparam>
    /// <param name="key">The key identifying the cached item.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the item of type <typeparamref name="T"/> retrieved from the cache.</returns>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Get<T>(key);
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled<T?>(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error getting value from cache. Key: {key} Exception: {exception}", LoggerId, key, e);
            return Task.FromResult<T?>(default);
        }
    }

    /// <summary>
    /// Sets a value in the in-memory cache with the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the value to be stored in the cache.</typeparam>
    /// <param name="key">The key associated with the value to store in the cache. The key must be valid and non-empty.</param>
    /// <param name="value">The value to store in the cache. <remarks>The value must be valid and non-null.</remarks></param>
    public void Set<T>(string key, T value)
    {
        if (!ValidateKey(key) || !ValidateValue(value))
            return;

        try
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl
            };
            options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
            {
                EvictionCallback = (evictedKey, _, _, _) =>
                {
                    if (evictedKey is string cacheKey)
                        _trackedKeys.TryRemove(cacheKey, out _);
                }
            });

            var cacheKey = BuildCacheKey(key);
            memoryCache.Set(cacheKey, value, options);
            _trackedKeys[cacheKey] = 0;
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error setting value in cache. Key: {key} Exception: {exception}", LoggerId, key, e);
        }
    }

    /// <summary>
    /// Asynchronously stores a specified key-value pair in the cache.
    /// </summary>
    /// <typeparam name="T">The type of the value to be stored.</typeparam>
    /// <param name="key">The key under which the value will be stored. Must be unique within the cache.</param>
    /// <param name="value">The value to store in the cache associated with the specified key.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation of storing the key-value pair in the cache.</returns>
    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(key, value);
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error setting value in cache. Key: {key} Exception: {exception}", LoggerId, key, e);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Removes the specified key from the in-memory cache.
    /// </summary>
    /// <param name="key">The key of the cache entry to remove. Must not be null or empty.</param>
    public void Remove(string key)
    {
        if (!ValidateKey(key))
            return;

        var cacheKey = BuildCacheKey(key);

        try
        {
            memoryCache.Remove(cacheKey);
            _trackedKeys.TryRemove(cacheKey, out _);
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error removing value from cache. Key: {key} Exception: {exception}", LoggerId, key, e);
        }
    }

    /// <summary>
    /// Asynchronously removes a cached object associated with the specified key.
    /// </summary>
    /// <param name="key">The unique identifier for the cache entry to be removed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error removing value from cache. Key: {key} Exception: {exception}", LoggerId, key, e);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Clears entries from the configured provider namespace in the in-memory cache.
    /// </summary>
    /// <remarks>
    /// This method removes tracked cache keys that start with the configured provider namespace prefix.
    /// </remarks>
    /// <exception cref="Exception">
    /// Thrown when an error occurs during the attempt to clear the cache contents.
    /// </exception>
    public void Clear()
    {
        try
        {
            foreach (var key in _trackedKeys.Keys)
            {
                if (!key.StartsWith(_namespacePrefix, StringComparison.Ordinal))
                    continue;

                memoryCache.Remove(key);
                _trackedKeys.TryRemove(key, out _);
            }
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error clearing cache namespace. Namespace: {namespace} Exception: {exception}", LoggerId, _namespacePrefix, e);
        }
    }

    /// <summary>
    /// Asynchronously clears entries from the configured provider namespace in the in-memory cache.
    /// </summary>
    /// <param name="cancellationToken">A CancellationToken used to cancel the operation, if needed.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Clear();
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError("{logger} Error clearing cache namespace. Namespace: {namespace} Exception: {exception}", LoggerId, _namespacePrefix, e);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Validates if the provided key is non-null, non-empty, and non-whitespace.
    /// </summary>
    /// <param name="key">The cache key to validate.</param>
    /// <returns>True if the key is valid; otherwise, false.</returns>
    private bool ValidateKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key)) return true;
        logger.LogWarning("{logger} Key is null or whitespace. Bypassing cache.", LoggerId);
        return false;
    }

    /// <summary>
    /// Validates if the provided value is not null.
    /// </summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="value">The value to be validated.</param>
    /// <returns>True if the value is not null, otherwise false.</returns>
    private bool ValidateValue<T>(T value)
    {
        if (value != null) return true;
        logger.LogWarning("{logger} Value is null. Bypassing cache on set.", LoggerId);
        return false;
    }

    private string BuildCacheKey(string key) => $"{_namespacePrefix}{key}";

    private static string BuildNamespacePrefix(CacheProviderSettings settings)
    {
        var namespaceName = string.IsNullOrWhiteSpace(settings.Namespace)
            ? settings.Name
            : settings.Namespace;

        var normalizedNamespace = namespaceName.Trim().TrimEnd(':');

        if (string.IsNullOrWhiteSpace(normalizedNamespace))
            normalizedNamespace = settings.Name.Trim().TrimEnd(':');

        return $"{normalizedNamespace}:";
    }
}
