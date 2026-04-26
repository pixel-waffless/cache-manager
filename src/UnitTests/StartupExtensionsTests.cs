using CacheManager.Extensions;
using CacheManager.Redis;
using CacheManager.Settings;
using CacheManager.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class StartupExtensionsTests
{
    [Test]
    public void AddCacheManager_WhenServicesIsNull_ThrowsArgumentNullException()
    {
        Assert.That(
            () => StartupExtensions.AddCacheManager(null!, new ConfigurationBuilder().Build()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void AddCacheManager_WhenConfigurationIsNull_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.That(
            () => services.AddCacheManager(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void AddCacheManager_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCacheManager(
            new ConfigurationBuilder().Build(),
            settings: settings => settings.Providers.Add(new CacheProviderSettings
            {
                Name = "Default",
                Type = ProviderType.Memory,
                ExpirationMinutes = 60
            }));

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ICacheManager>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<ICacheProvidersCollection>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<IRedisConnectionFactory>(), Is.Not.Null);
    }

    [Test]
    public void AddCacheManager_SkipsInvalidProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCacheManager(
            new ConfigurationBuilder().Build(),
            settings: settings =>
            {
                settings.Providers.Add(new CacheProviderSettings
                {
                    Name = "Local",
                    Type = ProviderType.Memory
                });
                settings.Providers.Add(new CacheProviderSettings
                {
                    Name = "Remote",
                    Type = ProviderType.Redis,
                    ConnectionString = " "
                });
            });

        using var provider = services.BuildServiceProvider();
        var collection = provider.GetRequiredService<ICacheProvidersCollection>();

        Assert.That(collection.TryGetProvider("Local", out _), Is.True);
        Assert.That(collection.TryGetProvider("Remote", out _), Is.False);
    }

    [Test]
    public void AddCacheManager_DoesNotRegisterDuplicateProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCacheManager(
            new ConfigurationBuilder().Build(),
            settings: settings =>
            {
                settings.Providers.Add(new CacheProviderSettings
                {
                    Name = "Default",
                    Type = ProviderType.Memory,
                    Namespace = "first"
                });
                settings.Providers.Add(new CacheProviderSettings
                {
                    Name = "default",
                    Type = ProviderType.Memory,
                    Namespace = "second"
                });
            });

        using var provider = services.BuildServiceProvider();
        var collection = provider.GetRequiredService<ICacheProvidersCollection>();

        Assert.That(collection.TryGetProvider("DEFAULT", out var settings), Is.True);
        Assert.That(settings!.Namespace, Is.EqualTo("first"));
    }
}
