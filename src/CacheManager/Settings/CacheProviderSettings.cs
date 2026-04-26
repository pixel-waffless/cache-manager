using CacheManager.Types;

namespace CacheManager.Settings;

/// <summary>
/// Represents the settings required to configure a cache provider.
/// </summary>
/// <remarks>
/// This class is used to define configuration details such as type, name, namespace, connection string, expiration policy, and thresholds for cache providers.
/// Supported provider types include memory and external providers such as Redis.
/// </remarks>
public class CacheProviderSettings
{
    /// Gets or sets the type of cache provider being used.
    /// The property uses the enumerated `ProviderType` to define
    /// the available cache types, such as Memory or Redis.
    /// This property is required for identifying the desired cache
    /// implementation in the caching system.
    public ProviderType Type { get; set; }

    /// <summary>
    /// Gets or sets the name of the cache provider.
    /// </summary>
    /// <remarks>
    /// The name is used to uniquely identify a cache provider within the system.
    /// It is used to match specific cache provider settings during configuration
    /// and allows retrieval of the appropriate provider instance at runtime.
    /// </remarks>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the key namespace used by the provider.
    /// </summary>
    /// <remarks>
    /// The namespace is applied internally to all keys using the "{namespace}:" prefix.
    /// When not provided, the provider name is used as the namespace.
    /// </remarks>
    public string? Namespace { get; set; }

    /// Gets or sets the connection string used for configuring the cache provider.
    /// This property is necessary when configuring providers that require a connection
    /// to an external cache system, such as Redis. For in-memory cache providers,
    /// this parameter can be left unset.
    /// If the connection string is not provided for applicable cache types,
    /// the validation or initialization of the provider may fail.
    public string ConnectionString { get; set; } = string.Empty;

    /// Gets or sets the default time-to-live (TTL) value, in minutes, for cached items.
    /// This property defines the duration, in minutes, before a cached entry expires and is removed from the cache.
    /// If no value is specified, a default expiration time may be applied by the cache implementation.
    /// A value of null or a non-positive number may indicate that the default expiration configuration of the specific provider will be used.
    public int? ExpirationMinutes { get; set; }

    /// Gets or sets the failure threshold value for the circuit breaker mechanism.
    /// This determines the number of allowed failures before the circuit breaker transitions to an open state.
    /// A value of null or less than or equal to zero may cause default thresholds to be used, depending on the implementation.
    public int? FailureThreshold { get; set; }

    /// <summary>
    /// Gets or sets the time, in seconds, that the circuit breaker will remain in a disconnected state
    /// after reaching the defined failure threshold.
    /// </summary>
    /// <remarks>
    /// This property is used to configure the duration for which operations will be prevented
    /// following consecutive failures. If not explicitly set, a default value may be used
    /// by the implementing cache provider. Appropriate configuration of this value helps
    /// manage stability and recovery behavior during transient errors or outages.
    /// </remarks>
    public int? DisconnectSeconds { get; set; }

    /// Validates the current cache provider settings and returns a list of validation errors, if any.
    /// The method checks for the following validation rules:
    /// - The `Name` property must be provided.
    /// - If the `Type` is not `ProviderType.Memory`, the `ConnectionString` property must be provided.
    /// If the validation passes, an empty string array is returned. If any validation errors occur, they are included in the returned array.
    /// <returns>
    /// A string array containing validation error messages, or an empty array if no errors are found.
    /// </returns>
    public string[] Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("[CacheManager][Validator] Cache name is required");

        if (Type == ProviderType.Memory)
            return errors.ToArray();

        if (string.IsNullOrWhiteSpace(ConnectionString))
            errors.Add("[CacheManager][Validator] Cache connection string is required");

        return errors.ToArray();
    }
}
