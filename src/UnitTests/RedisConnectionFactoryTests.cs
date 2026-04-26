using CacheManager.Redis;
using CacheManager.Settings;
using CacheManager.Types;
using Microsoft.Extensions.Logging.Abstractions;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class RedisConnectionFactoryTests
{
    [Test]
    public void GetConnection_WhenSettingsIsNull_ThrowsArgumentNullException()
    {
        using var factory = CreateFactory();

        Assert.That(() => factory.GetConnection(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void GetConnection_WhenProviderNameIsBlank_ThrowsArgumentException()
    {
        using var factory = CreateFactory();
        var settings = CreateSettings(" ");

        Assert.That(() => factory.GetConnection(settings), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetConnection_WhenConnectionStringIsBlank_ThrowsArgumentException()
    {
        using var factory = CreateFactory();
        var settings = CreateSettings("Remote");
        settings.ConnectionString = " ";

        Assert.That(() => factory.GetConnection(settings), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetConnection_AfterDispose_ThrowsObjectDisposedException()
    {
        using var factory = CreateFactory();
        factory.Dispose();

        Assert.That(() => factory.GetConnection(CreateSettings("Remote")), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task GetConnection_AfterDisposeAsync_ThrowsObjectDisposedException()
    {
        var factory = CreateFactory();
        await factory.DisposeAsync();

        Assert.That(() => factory.GetConnection(CreateSettings("Remote")), Throws.TypeOf<ObjectDisposedException>());
    }

    private static RedisConnectionFactory CreateFactory() =>
        new(NullLogger<RedisConnectionFactory>.Instance);

    private static CacheProviderSettings CreateSettings(string name) =>
        new()
        {
            Name = name,
            Type = ProviderType.Redis,
            ConnectionString = "localhost:6379"
        };
}
