using System.Collections.Concurrent;
using CacheManager.Settings;

namespace CacheManager;

/// <inheritdoc/>
public class CacheProvidersCollection : ICacheProvidersCollection
{
    /// <summary>
    /// Maintains a collection of registered cache providers and their corresponding settings.
    /// </summary>
    /// <remarks>
    /// This private dictionary serves as a repository for managing cache provider configurations,
    /// mapping provider names to their respective <see cref="CacheProviderSettings"/> objects.
    /// It supports operations such as adding, retrieving, and removing providers.
    /// </remarks>
    private readonly ConcurrentDictionary<string, CacheProviderSettings> _registeredProviders = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool TryAddProvider(string name, CacheProviderSettings settings) =>
        _registeredProviders.TryAdd(name, settings);

    /// <inheritdoc/>
    public bool TryGetProvider(string name, out CacheProviderSettings? settings) =>
        _registeredProviders.TryGetValue(name, out settings);

    /// <inheritdoc/>
    public bool TryRemoveProvider(string name) => _registeredProviders.TryRemove(name, out _);
}
