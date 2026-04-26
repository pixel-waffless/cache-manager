using CacheManager.Memory;
using CacheManager.Settings;
using CacheManager.Types;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class InMemoryCacheProviderTests
{
    [Test]
    public void Get_WhenKeyDoesNotExist_ReturnsDefault()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");

        Assert.That(provider.Get<string>("missing"), Is.Null);
        Assert.That(provider.Get<int>("missing"), Is.Zero);
    }

    [Test]
    public void SetAndGet_SupportsPrimitiveValues()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");

        provider.Set("answer", 42);

        Assert.That(provider.Get<int>("answer"), Is.EqualTo(42));
    }

    [Test]
    public void Set_WhenValueIsNull_DoesNotStoreEntry()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");

        provider.Set<string?>("null-value", null);

        Assert.That(provider.Get<string>("null-value"), Is.Null);
        Assert.That(memoryCache.TryGetValue("local:null-value", out _), Is.False);
    }

    [Test]
    public void InvalidKeys_AreIgnored()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");

        provider.Set(" ", "value");
        provider.Remove(" ");

        Assert.That(provider.Get<string>(" "), Is.Null);
        Assert.That(memoryCache.TryGetValue("local: ", out _), Is.False);
    }

    [Test]
    public void Remove_RemovesOnlyNamespacedEntry()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var local = CreateProvider(memoryCache, "Local", "local");
        var other = CreateProvider(memoryCache, "Other", "other");

        local.Set("shared-key", "local-value");
        other.Set("shared-key", "other-value");

        local.Remove("shared-key");

        Assert.That(local.Get<string>("shared-key"), Is.Null);
        Assert.That(other.Get<string>("shared-key"), Is.EqualTo("other-value"));
    }

    [Test]
    public async Task AsyncOperations_PerformSameCacheFlow()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");

        await provider.SetAsync("key", "value");
        var stored = await provider.GetAsync<string>("key");
        await provider.RemoveAsync("key");
        var removed = await provider.GetAsync<string>("key");

        Assert.That(stored, Is.EqualTo("value"));
        Assert.That(removed, Is.Null);
    }

    [Test]
    public async Task ClearAsync_RemovesOnlyConfiguredNamespace()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var local = CreateProvider(memoryCache, "Local", "local");
        var other = CreateProvider(memoryCache, "Other", "other");

        await local.SetAsync("key", "local-value");
        await other.SetAsync("key", "other-value");

        await local.ClearAsync();

        Assert.That(await local.GetAsync<string>("key"), Is.Null);
        Assert.That(await other.GetAsync<string>("key"), Is.EqualTo("other-value"));
    }

    [Test]
    public void AsyncOperations_WhenCanceled_ReturnCanceledTasks()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var token = cancellationTokenSource.Token;

        Assert.That(async () => await provider.GetAsync<string>("key", token), Throws.TypeOf<TaskCanceledException>());
        Assert.That(async () => await provider.SetAsync("key", "value", token), Throws.TypeOf<TaskCanceledException>());
        Assert.That(async () => await provider.RemoveAsync("key", token), Throws.TypeOf<TaskCanceledException>());
        Assert.That(async () => await provider.ClearAsync(token), Throws.TypeOf<TaskCanceledException>());
    }

    [Test]
    public void Namespace_WhenNotConfigured_UsesProviderName()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", null);

        provider.Set("key", "value");

        Assert.That(memoryCache.TryGetValue("Local:key", out string? value), Is.True);
        Assert.That(value, Is.EqualTo("value"));
    }

    [Test]
    public void Namespace_WhenConfiguredWithTrailingColon_IsNormalized()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(memoryCache, "Local", "local:");

        provider.Set("key", "value");

        Assert.That(memoryCache.TryGetValue("local:key", out string? value), Is.True);
        Assert.That(value, Is.EqualTo("value"));
    }

    private static InMemoryCacheProvider CreateProvider(IMemoryCache memoryCache, string name, string? namespaceName) =>
        new(
            NullLogger<InMemoryCacheProvider>.Instance,
            memoryCache,
            new CacheProviderSettings
            {
                Name = name,
                Namespace = namespaceName,
                Type = ProviderType.Memory,
                ExpirationMinutes = 60
            });
}
