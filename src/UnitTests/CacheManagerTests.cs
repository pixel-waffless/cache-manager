using CacheManager.Memory;
using CacheManager.Redis;
using CacheManager.Settings;
using CacheManager.Types;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class CacheManagerTests
{
    [Test]
    public void GetCacheProvider_WhenProviderIsMissing_ReturnsSameFallbackWithoutRegisteringIt()
    {
        var providers = new CacheProvidersCollection();
        var manager = CreateCacheManager(providers);

        var first = manager.GetCacheProvider("Missing");
        var second = manager.GetCacheProvider("OtherMissing");

        Assert.That(second, Is.SameAs(first));
        Assert.That(providers.TryGetProvider("Fallback", out _), Is.False);
        Assert.That(providers.TryGetProvider("Default", out _), Is.False);
    }

    [Test]
    public void GetCacheProvider_WhenMissingProviderIsAddedLater_DoesNotReuseFallbackForThatName()
    {
        var providers = new CacheProvidersCollection();
        var manager = CreateCacheManager(providers);

        var fallback = manager.GetCacheProvider("Orders");

        providers.TryAddProvider("Orders", new CacheProviderSettings
        {
            Name = "Orders",
            Namespace = "orders",
            Type = ProviderType.Memory,
            ExpirationMinutes = 60
        });

        var provider = manager.GetCacheProvider("Orders");

        Assert.That(provider, Is.Not.SameAs(fallback));
        provider.Set("1", "created-after-fallback");
        Assert.That(provider.Get<string>("1"), Is.EqualTo("created-after-fallback"));
    }

    [Test]
    public void GetCacheProvider_UsesCaseInsensitiveProviderCache()
    {
        var providers = new CacheProvidersCollection();
        providers.TryAddProvider("Default", new CacheProviderSettings
        {
            Name = "Default",
            Namespace = "default",
            Type = ProviderType.Memory,
            ExpirationMinutes = 60
        });

        var manager = CreateCacheManager(providers);

        var lower = manager.GetCacheProvider("default");
        var upper = manager.GetCacheProvider("DEFAULT");

        Assert.That(upper, Is.SameAs(lower));
    }

    [Test]
    public void GetCacheProvider_WhenProviderInitializationFails_DoesNotCacheFallbackUnderRequestedName()
    {
        var providers = new CacheProvidersCollection();
        providers.TryAddProvider("Remote", new CacheProviderSettings
        {
            Name = "Remote",
            Namespace = "remote",
            Type = ProviderType.Redis,
            ConnectionString = "localhost:6379"
        });

        var manager = CreateCacheManager(providers);
        var fallback = manager.GetCacheProvider("Remote");

        providers.TryRemoveProvider("Remote");
        providers.TryAddProvider("Remote", new CacheProviderSettings
        {
            Name = "Remote",
            Namespace = "remote",
            Type = ProviderType.Memory,
            ExpirationMinutes = 60
        });

        var provider = manager.GetCacheProvider("Remote");

        Assert.That(provider, Is.Not.SameAs(fallback));
        provider.Set("1", "created-after-failed-init");
        Assert.That(provider.Get<string>("1"), Is.EqualTo("created-after-failed-init"));
    }

    [Test]
    public void GetCacheProvider_WhenNameIsBlank_ThrowsArgumentException()
    {
        var manager = CreateCacheManager(new CacheProvidersCollection());

        Assert.That(() => manager.GetCacheProvider(" "), Throws.ArgumentException);
    }

    [Test]
    public void InMemoryCacheProvider_AppliesConfiguredNamespaceToKeys()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var orders = CreateInMemoryProvider(memoryCache, "OrdersProvider", "orders");
        var customers = CreateInMemoryProvider(memoryCache, "CustomersProvider", "customers");

        orders.Set("1", "order");
        customers.Set("1", "customer");

        Assert.That(orders.Get<string>("1"), Is.EqualTo("order"));
        Assert.That(customers.Get<string>("1"), Is.EqualTo("customer"));
        Assert.That(memoryCache.TryGetValue("orders:1", out string? rawOrder), Is.True);
        Assert.That(rawOrder, Is.EqualTo("order"));
    }

    [Test]
    public void InMemoryCacheProvider_ClearOnlyRemovesKeysFromConfiguredNamespace()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var orders = CreateInMemoryProvider(memoryCache, "OrdersProvider", "orders");
        var customers = CreateInMemoryProvider(memoryCache, "CustomersProvider", "customers");

        orders.Set("1", "order");
        customers.Set("1", "customer");

        orders.Clear();

        Assert.That(orders.Get<string>("1"), Is.Null);
        Assert.That(customers.Get<string>("1"), Is.EqualTo("customer"));
    }

    private static CacheManager CreateCacheManager(ICacheProvidersCollection providers)
    {
        var services = new TestServiceProvider(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<InMemoryCacheProvider>.Instance);

        return new CacheManager(
            NullLogger<CacheManager>.Instance,
            services,
            providers);
    }

    private static InMemoryCacheProvider CreateInMemoryProvider(IMemoryCache memoryCache, string name, string namespaceName)
    {
        return new InMemoryCacheProvider(
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

    private sealed class TestServiceProvider(IMemoryCache memoryCache, ILogger<InMemoryCacheProvider> inMemoryLogger) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IMemoryCache))
                return memoryCache;

            if (serviceType == typeof(ILogger<InMemoryCacheProvider>))
                return inMemoryLogger;

            if (serviceType == typeof(ILogger<RedisCacheProvider>))
                return NullLogger<RedisCacheProvider>.Instance;

            return null;
        }
    }
}
