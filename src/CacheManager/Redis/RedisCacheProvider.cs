using System.Net.Sockets;
using System.Text.Json;
using CacheManager.Settings;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace CacheManager.Redis;

/// <summary>
/// Provides a caching mechanism using Redis as the underlying storage.
/// Implements <see cref="ICacheProvider" />.
/// </summary>
/// <remarks>
/// This class supports both synchronous and asynchronous operations for managing
/// cache data such as retrieval, addition, removal, and clearing. The Redis connection
/// is managed by <see cref="IRedisConnectionFactory" /> and configured through the <see cref="CacheProviderSettings" />.
/// The class also uses circuit breaker policies to handle connection issues
/// with Redis and to provide resilience in distributed applications.
/// Users of this class can serialize and deserialize objects from the cache
/// and configure expiration settings for cached items.
/// </remarks>
public sealed class RedisCacheProvider : ICacheProvider
{
    private const int DefaultExpirationMinutes = 1440;
    private const string LoggerId = "[CacheProvider][RedisCacheProvider]";
    private const int DefaultCircuitBreakerFailureThreshold = 3;
    private const int DefaultCircuitBreakerDisconnectionTimeSeconds = 30;

    private readonly ILogger<RedisCacheProvider> _logger;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;
    private readonly TimeSpan _ttl;
    private readonly IAsyncPolicy _circuitBreakerPolicyAsync;
    private readonly ISyncPolicy _circuitBreakerPolicySync;
    private readonly string _namespacePrefix;
    private readonly RedisValue _namespacePattern;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisCacheProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record Redis cache operations and failures.</param>
    /// <param name="redisConnectionFactory">The factory that owns and reuses Redis connections by provider configuration.</param>
    /// <param name="settings">The provider settings used to configure namespace, TTL, connection, and circuit breaker values.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/>, <paramref name="redisConnectionFactory"/>, or <paramref name="settings"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the Redis connection string is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the Redis connection cannot be established.</exception>
    public RedisCacheProvider(ILogger<RedisCacheProvider> logger, IRedisConnectionFactory redisConnectionFactory, CacheProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(redisConnectionFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ConnectionString);

        _logger = logger;
        _connectionMultiplexer = redisConnectionFactory.GetConnection(settings);

        if (!_connectionMultiplexer.IsConnected)
        {
            _logger.LogError("{logger} Failed to connect to Redis. Provider: {provider}",
                LoggerId, settings.Name);
            throw new InvalidOperationException("Failed to connect to Redis.");
        }

        _database = _connectionMultiplexer.GetDatabase();
        _namespacePrefix = BuildNamespacePrefix(settings);
        _namespacePattern = $"{EscapeRedisPattern(_namespacePrefix)}*";
        _ttl = TimeSpan.FromMinutes(settings.ExpirationMinutes is > 0
            ? settings.ExpirationMinutes.Value
            : DefaultExpirationMinutes);
        var circuitBreakerFailureThreshold = settings.FailureThreshold is > 0
            ? settings.FailureThreshold.Value
            : DefaultCircuitBreakerFailureThreshold;
        var circuitBreakerDisconnectionTimeSeconds = TimeSpan.FromSeconds(settings.DisconnectSeconds is > 0
            ? settings.DisconnectSeconds.Value
            : DefaultCircuitBreakerDisconnectionTimeSeconds);
        _circuitBreakerPolicyAsync =
            GetCircuitBreakerPolicyAsync(circuitBreakerFailureThreshold, circuitBreakerDisconnectionTimeSeconds, logger);
        _circuitBreakerPolicySync = GetCircuitBreakerPolicy(circuitBreakerFailureThreshold,
            circuitBreakerDisconnectionTimeSeconds, logger);
    }

    /// <summary>
    /// Provides settings for customizing the serialization and deserialization behavior
    /// when working with JSON data using the Newtonsoft.Json framework.
    /// </summary>
    private static readonly JsonSerializerOptions JsonSerializerSettings = new JsonSerializerOptions()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Retrieves a value from the Redis cache by the given key.
    /// </summary>
    /// <typeparam name="T">The type of the value to be retrieved.</typeparam>
    /// <param name="key">The key used to identify and retrieve the cached value.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/> if the key is found; otherwise returns null.</returns>
    public T? Get<T>(string key)
    {
        if (!ValidateKey(key))
            return default;

        var cacheKey = BuildCacheKey(key);

        try
        {
            var value = _circuitBreakerPolicySync.Execute(() => _database.StringGet(cacheKey));
            return !value.HasValue ? default : JsonSerializer.Deserialize<T>((string)value!, JsonSerializerSettings);
        }
        catch (JsonException e)
        {
            _logger.LogError("{logger} Error deserializing value from cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
            return default;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on get key: {key}", LoggerId,
                key);
            return default;
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error getting value from cache. Key: {key} Exception: {exception}", LoggerId,
                key, e);
            return default;
        }
    }

    /// <summary>
    /// Retrieves a value of the specified type from the cache asynchronously.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve from the cache.</typeparam>
    /// <param name="key">The key associated with the value in the cache.</param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is the value retrieved from the cache if the
    /// operation succeeded; otherwise, returns null.
    /// </returns>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!ValidateKey(key))
            return default;

        var cacheKey = BuildCacheKey(key);

        try
        {
            var value = await _circuitBreakerPolicyAsync.ExecuteAsync(_ => _database.StringGetAsync(cacheKey),
                cancellationToken);
            return !value.HasValue ? default : JsonSerializer.Deserialize<T>((string)value!, JsonSerializerSettings);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{logger} Get operation was cancelled for key: {key}", LoggerId, key);
            throw;
        }
        catch (JsonException e)
        {
            _logger.LogError("{logger} Error deserializing value from cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
            return default;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on get key: {key}", LoggerId,
                key);
            return default;
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error getting value from cache. Key: {key} Exception: {exception}", LoggerId,
                key, e);
            return default;
        }
    }

    /// <summary>
    /// Stores a key-value pair in the Redis cache.
    /// </summary>
    /// <typeparam name="T">The type of the value to be cached.</typeparam>
    /// <param name="key">The key used to retrieve the cached value. Must be a non-empty string.</param>
    /// <param name="value">The value to be stored in the cache. Must not be null.</param>
    /// <remarks>
    /// This method serializes the provided value to JSON format before storing it in the cache.
    /// If the serialization fails or the circuit breaker is open, the value will not be cached.
    /// Any errors during the caching process are logged.
    /// </remarks>
    public void Set<T>(string key, T value)
    {
        if (!ValidateKey(key) || !ValidateValue(value))
            return;

        var cacheKey = BuildCacheKey(key);

        try
        {
            var json = JsonSerializer.Serialize(value, JsonSerializerSettings);
            _circuitBreakerPolicySync.Execute(() =>
                _database.StringSet(cacheKey, json, _ttl));
        }
        catch (JsonException e)
        {
            _logger.LogError("{logger} Error serializing value to cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on set key: {key}", LoggerId,
                key);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error setting value on cache. Key: {key} Exception: {exception}", LoggerId,
                key, e);
        }
    }

    /// Asynchronously sets a value in the cache with the specified key.
    /// If the key or value is invalid, the operation will return immediately.
    /// Handles serialization errors, operation cancellation, and circuit breaker conditions during execution.
    /// Logs relevant information or errors as they occur during the cache operation.
    /// <typeparam name="T">The type of the value to be stored in the cache.</typeparam>
    /// <param name="key">The unique key representing the cached entry. Must be a valid non-null, non-empty string.</param>
    /// <param name="value">The value to be stored in the cache. Must not be null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. If the operation is canceled or fails, appropriate exceptions may be thrown or logged.</returns>
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        if (!ValidateKey(key) || !ValidateValue(value))
            return;

        try
        {
            var json = JsonSerializer.Serialize(value, JsonSerializerSettings);
            await _circuitBreakerPolicyAsync.ExecuteAsync(_ =>
                _database.StringSetAsync(BuildCacheKey(key), json, _ttl), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{logger} Set operation was cancelled for key: {key}", LoggerId, key);
            throw;
        }
        catch (JsonException e)
        {
            _logger.LogError("{logger} Error serializing value to cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on set key: {key}", LoggerId,
                key);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error setting value on cache. Key: {key} Exception: {exception}", LoggerId,
                key, e);
        }
    }

    /// <summary>
    /// Removes the cached value associated with the specified key from the Redis cache.
    /// </summary>
    /// <param name="key">The cache key to be removed. This must be a valid key string.</param>
    /// <remarks>
    /// If the key is invalid, the method will exit without attempting to remove the cache entry.
    /// The method uses a circuit breaker policy to handle cache operations. If the circuit breaker is open,
    /// a warning is logged, and the cache is bypassed. Any unexpected exceptions during the removal process
    /// are logged as errors.
    /// </remarks>
    public void Remove(string key)
    {
        if (!ValidateKey(key))
            return;

        var cacheKey = BuildCacheKey(key);

        try
        {
            _circuitBreakerPolicySync.Execute(() => _database.KeyDelete(cacheKey));
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on remove key: {key}", LoggerId,
                key);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error removing value from cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
        }
    }

    /// <summary>
    /// Removes a cache entry corresponding to the specified key asynchronously.
    /// </summary>
    /// <param name="key">The key of the cache entry to be removed.</param>
    /// <param name="cancellationToken">
    /// The cancellation token to observe while waiting for the remove operation to complete. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous remove operation.</returns>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ValidateKey(key))
            return;

        var cacheKey = BuildCacheKey(key);

        try
        {
            await _circuitBreakerPolicyAsync.ExecuteAsync(_ => _database.KeyDeleteAsync(cacheKey), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{logger} Remove operation was cancelled for key: {key}", LoggerId, key);
            throw;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on remove key: {key}", LoggerId,
                key);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error removing value from cache. Key: {key} Exception: {exception}",
                LoggerId, key, e);
        }
    }

    /// <summary>
    /// Clears cached items in the configured provider namespace from the Redis-backed cache provider.
    /// </summary>
    /// <remarks>
    /// This method removes keys that match the configured provider namespace pattern. It is executed in the context of a circuit breaker policy to manage
    /// transient connection issues. If the circuit breaker is open, a warning is logged, and the cache clearing is bypassed.
    /// In case of unexpected errors during the flush operation, the exception is logged, helping to diagnose and address potential issues.
    /// </remarks>
    /// <exception cref="BrokenCircuitException">
    /// Thrown when the circuit breaker is open and the method is bypassing cache operations.
    /// </exception>
    /// <exception cref="Exception">
    /// Thrown when an error occurs during the cache flush operation.
    /// </exception>
    public void Clear()
    {
        try
        {
            _circuitBreakerPolicySync.Execute(() => FlushNamespace(_namespacePattern));
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on clear namespace: {namespace}", LoggerId, _namespacePrefix);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error clearing cache namespace. Namespace: {namespace} Exception: {exception}", LoggerId, _namespacePrefix, e);
        }
    }

    /// <summary>
    /// Clears entries from the configured provider namespace in the Redis cache asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the operation to complete. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous clear operation.
    /// </returns>
    /// <remarks>
    /// If the operation is canceled, an informational log entry is generated. If the circuit breaker is open,
    /// a warning log entry is provided indicating the cache bypass. Exceptions during the process are logged as errors.
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _circuitBreakerPolicyAsync.ExecuteAsync(_ => FlushNamespaceAsync(_namespacePattern, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{logger} Clear operation was cancelled for namespace: {namespace}", LoggerId, _namespacePrefix);
            throw;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("{logger} Circuit breaker is open. Bypassing cache on clear namespace: {namespace}", LoggerId, _namespacePrefix);
        }
        catch (Exception e)
        {
            _logger.LogError("{logger} Error clearing cache namespace. Namespace: {namespace} Exception: {exception}", LoggerId, _namespacePrefix, e);
        }
    }

    /// Validates whether the provided cache key is valid for use in caching operations.
    /// The validation checks if the key is not null, empty, or consists exclusively of whitespace.
    /// Logs a warning if the key is invalid and returns false.
    /// <param name="key">The cache key to validate.</param>
    /// <returns>True if the key is valid; otherwise, false.</returns>
    private bool ValidateKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key)) return true;
        _logger.LogWarning("{logger} Key is null or whitespace. Bypassing cache.", LoggerId);
        return false;
    }

    /// Validates the provided value to determine if it is acceptable for caching.
    /// Logs a warning if the value is null and returns false in such cases.
    /// <typeparam name="T">The type of the value to validate.</typeparam>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value is valid; otherwise, false.</returns>
    private bool ValidateValue<T>(T value)
    {
        if (value != null) return true;
        _logger.LogWarning("{logger} Value is null. Bypassing cache on set.", LoggerId);
        return false;
    }

    private void FlushNamespace(RedisValue pattern)
    {
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = _connectionMultiplexer.GetServer(endpoint);

            foreach (var key in server.Keys(_database.Database, pattern))
            {
                _database.KeyDelete(key);
            }
        }
    }

    private async Task FlushNamespaceAsync(RedisValue pattern, CancellationToken cancellationToken = default)
    {
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = _connectionMultiplexer.GetServer(endpoint);

            foreach (var key in server.Keys(_database.Database, pattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _database.KeyDeleteAsync(key);
            }
        }
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

    private static string EscapeRedisPattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("*", @"\*", StringComparison.Ordinal)
            .Replace("?", @"\?", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal)
            .Replace("]", @"\]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Asynchronously retrieves or creates a circuit breaker policy instance with predefined configurations.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the circuit breaker policy instance.
    /// </returns>
    private static IAsyncPolicy GetCircuitBreakerPolicyAsync(int circuitBreakerFailureThreshold, TimeSpan circuitBreakerDisconnectionTimeSeconds, ILogger logger)
    {
        return Policy
            .Handle<SocketException>()
            .Or<TimeoutException>()
            .Or<InvalidOperationException>()
            .Or<IOException>()
            .Or<RedisException>()
            .Or<RedisConnectionException>()
            .Or<RedisTimeoutException>()
            .CircuitBreakerAsync(
                circuitBreakerFailureThreshold,
                circuitBreakerDisconnectionTimeSeconds,
                onBreak: (ex, timeSpan) =>
                    logger.LogWarning(
                        "{logger} Circuit breaker is open. Duration: {duration}s. Exception: {exception}", LoggerId,
                        timeSpan.TotalSeconds, ex.Message),
                onHalfOpen: () => logger.LogInformation("{logger} Circuit breaker half-open", LoggerId),
                onReset: () => logger.LogInformation("{logger} Circuit breaker reset", LoggerId)
            );
    }

    /// Creates and returns a synchronous circuit breaker policy.
    /// This policy handles specific types of exceptions such as SocketException, TimeoutException,
    /// InvalidOperationException, IOException, RedisException, RedisConnectionException,
    /// and RedisTimeoutException. It triggers the circuit breaker after a specified number of consecutive
    /// failures and keeps the circuit open for a predefined duration.
    /// The policy includes logging when the circuit breaker is triggered (onBreak), is reset (onReset),
    /// or moves to a half-open state (onHalfOpen).
    /// <returns>
    /// A synchronous Polly circuit breaker policy configured with the predefined failure threshold
    /// and disconnection duration.
    /// </returns>
    private static ISyncPolicy GetCircuitBreakerPolicy(int circuitBreakerFailureThreshold, TimeSpan circuitBreakerDisconnectionTimeSeconds, ILogger logger)
    {
        return Policy
            .Handle<SocketException>()
            .Or<TimeoutException>()
            .Or<InvalidOperationException>()
            .Or<IOException>()
            .Or<RedisException>()
            .Or<RedisConnectionException>()
            .Or<RedisTimeoutException>()
            .CircuitBreaker(
                circuitBreakerFailureThreshold,
                circuitBreakerDisconnectionTimeSeconds,
                onBreak: (ex, timeSpan) =>
                    logger.LogWarning(
                        "{logger} Circuit breaker is open. Duration: {duration}s. Exception: {exception}", LoggerId,
                        timeSpan.TotalSeconds, ex.Message),
                onHalfOpen: () => logger.LogInformation("{logger} Circuit breaker half-open", LoggerId),
                onReset: () => logger.LogInformation("{logger} Circuit breaker reset", LoggerId)
            );
    }
}
