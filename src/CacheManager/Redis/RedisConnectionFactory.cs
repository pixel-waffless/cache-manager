using System.Collections.Concurrent;
using CacheManager.Settings;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CacheManager.Redis;

/// <summary>
/// Default <see cref="IRedisConnectionFactory"/> implementation backed by a provider-name connection registry.
/// </summary>
/// <remarks>
/// A single factory instance can manage multiple Redis providers. Each provider name receives
/// one lazily-created <see cref="IConnectionMultiplexer"/> instance, and lookup is case-insensitive.
/// Connection strings are never written to logs.
/// </remarks>
public sealed class RedisConnectionFactory(ILogger<RedisConnectionFactory> logger) : IRedisConnectionFactory
{
    private const string LoggerId = "[CacheProvider][RedisConnectionFactory]";

    private readonly ConcurrentDictionary<string, Lazy<IConnectionMultiplexer>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private int _disposedState;

    /// <inheritdoc/>
    public IConnectionMultiplexer GetConnection(CacheProviderSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposedState != 0, this);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ConnectionString);

        var connection = _connections.GetOrAdd(
            settings.Name,
            _ => new Lazy<IConnectionMultiplexer>(
                () => CreateConnection(settings),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return connection.Value;
        }
        catch
        {
            _connections.TryRemove(settings.Name, out _);
            throw;
        }
    }

    /// <summary>
    /// Disposes all created Redis connections.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedState, 1) != 0)
            return;

        foreach (var connection in _connections.Values)
        {
            if (connection.IsValueCreated)
                connection.Value.Dispose();
        }

        _connections.Clear();
    }

    /// <summary>
    /// Asynchronously disposes all created Redis connections.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposedState, 1) != 0)
            return;

        foreach (var connection in _connections.Values)
        {
            if (connection.IsValueCreated)
                await connection.Value.DisposeAsync();
        }

        _connections.Clear();
    }

    private IConnectionMultiplexer CreateConnection(CacheProviderSettings settings)
    {
        try
        {
            var connection = ConnectionMultiplexer.Connect(settings.ConnectionString);
            if (connection.IsConnected)
                return connection;

            logger.LogError("{logger} Failed to connect to Redis. Provider: {provider}", LoggerId, settings.Name);
            connection.Dispose();
            throw new InvalidOperationException("Failed to connect to Redis.");
        }
        catch (Exception e)
        {
            logger.LogError(
                "{logger} Failed to create Redis connection. Provider: {provider}. ExceptionType: {exceptionType}",
                LoggerId,
                settings.Name,
                e.GetType().Name);
            throw;
        }
    }
}
