using System.Net;
using CacheManager.Redis;
using CacheManager.Settings;
using CacheManager.Types;
using Moq;
using Moq.AutoMock;
using StackExchange.Redis;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class RedisCacheProviderTests
{
    [Test]
    public void Constructor_WhenConnectionIsNotConnected_ThrowsInvalidOperationException()
    {
        var fixture = CreateFixture();
        fixture.ConnectionMultiplexer
            .SetupGet(connection => connection.IsConnected)
            .Returns(false);

        Assert.That(
            () => fixture.CreateProvider(),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Constructor_WhenConnectionStringIsBlank_ThrowsArgumentException()
    {
        var fixture = CreateFixture(settings: CreateSettings(connectionString: " "));

        Assert.That(
            () => fixture.CreateProvider(),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Get_WhenKeyExists_ReturnsDeserializedValueFromNamespacedKey()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.StringGet(
                It.Is<RedisKey>(key => key == "orders:1"),
                It.IsAny<CommandFlags>()))
            .Returns("{\"Name\":\"Book\"}");
        var provider = fixture.CreateProvider();

        var value = provider.Get<TestPayload>("1");

        Assert.That(value, Is.Not.Null);
        Assert.That(value!.Name, Is.EqualTo("Book"));
    }

    [Test]
    public void Get_WhenKeyDoesNotExist_ReturnsDefault()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(RedisValue.Null);
        var provider = fixture.CreateProvider();

        var value = provider.Get<TestPayload>("missing");

        Assert.That(value, Is.Null);
    }

    [Test]
    public void Get_WhenStoredValueIsInvalidJson_ReturnsDefault()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns("{not-json");
        var provider = fixture.CreateProvider();

        var value = provider.Get<TestPayload>("1");

        Assert.That(value, Is.Null);
    }

    [Test]
    public void Get_WhenKeyIsBlank_DoesNotCallRedis()
    {
        var fixture = CreateFixture();
        var provider = fixture.CreateProvider();

        var value = provider.Get<TestPayload>(" ");

        Assert.That(value, Is.Null);
        fixture.Database.Verify(
            database => database.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Test]
    public void Set_SerializesValueAndStoresItUnderNamespacedKey()
    {
        var fixture = CreateFixture();
        var provider = fixture.CreateProvider();

        provider.Set("1", new TestPayload("Book"));

        var invocation = AssertSingleInvocation(fixture.Database, "StringSet");
        Assert.That(invocation.Arguments[0]!.ToString(), Is.EqualTo("orders:1"));
        Assert.That(invocation.Arguments[1]!.ToString(), Is.EqualTo("{\"Name\":\"Book\"}"));
        Assert.That(invocation.Arguments[2]!.ToString(), Is.EqualTo("EX 900"));
    }

    [Test]
    public void Set_WhenValueIsNull_DoesNotCallRedis()
    {
        var fixture = CreateFixture();
        var provider = fixture.CreateProvider();

        provider.Set<TestPayload?>("1", null);

        fixture.Database.Verify(database => database.StringSet(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Test]
    public void Remove_DeletesNamespacedKey()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.KeyDelete(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .Returns(true);
        var provider = fixture.CreateProvider();

        provider.Remove("1");

        fixture.Database.Verify(database => database.KeyDelete(
                It.Is<RedisKey>(key => key == "orders:1"),
                CommandFlags.None),
            Times.Once);
    }

    [Test]
    public void Clear_DeletesOnlyKeysReturnedByConfiguredNamespacePattern()
    {
        var fixture = CreateFixture();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetEndPoints(false))
            .Returns([endpoint]);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetServer(endpoint, null))
            .Returns(server.Object);
        fixture.Database
            .SetupGet(database => database.Database)
            .Returns(4);
        server
            .Setup(redisServer => redisServer.Keys(
                4,
                It.Is<RedisValue>(pattern => pattern == "orders:*"),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns([(RedisKey)"orders:1", (RedisKey)"orders:2"]);
        fixture.Database
            .Setup(database => database.KeyDelete(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(true);
        var provider = fixture.CreateProvider();

        provider.Clear();

        fixture.Database.Verify(database => database.KeyDelete((RedisKey)"orders:1", CommandFlags.None), Times.Once);
        fixture.Database.Verify(database => database.KeyDelete((RedisKey)"orders:2", CommandFlags.None), Times.Once);
        fixture.Database.Verify(database => database.KeyDelete(It.Is<RedisKey>(key => key == "customers:1"), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Test]
    public void Clear_EscapesRedisPatternCharactersInNamespace()
    {
        var settings = CreateSettings(namespaceName: "tenant[1]*?");
        var fixture = CreateFixture(settings);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetEndPoints(false))
            .Returns([endpoint]);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetServer(endpoint, null))
            .Returns(server.Object);
        server
            .Setup(redisServer => redisServer.Keys(
                It.IsAny<int>(),
                It.Is<RedisValue>(pattern => pattern == @"tenant\[1\]\*\?:*"),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns([]);
        var provider = fixture.CreateProvider();

        provider.Clear();

        server.Verify(redisServer => redisServer.Keys(
                It.IsAny<int>(),
                It.Is<RedisValue>(pattern => pattern == @"tenant\[1\]\*\?:*"),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Test]
    public async Task GetAsync_WhenKeyExists_ReturnsDeserializedValueFromNamespacedKey()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.StringGetAsync(
                It.Is<RedisKey>(key => key == "orders:1"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync("{\"Name\":\"Book\"}");
        var provider = fixture.CreateProvider();

        var value = await provider.GetAsync<TestPayload>("1");

        Assert.That(value, Is.Not.Null);
        Assert.That(value!.Name, Is.EqualTo("Book"));
    }

    [Test]
    public async Task SetAsync_SerializesValueAndStoresItUnderNamespacedKey()
    {
        var fixture = CreateFixture();
        var provider = fixture.CreateProvider();

        await provider.SetAsync("1", new TestPayload("Book"));

        var invocation = AssertSingleInvocation(fixture.Database, "StringSetAsync");
        Assert.That(invocation.Arguments[0]!.ToString(), Is.EqualTo("orders:1"));
        Assert.That(invocation.Arguments[1]!.ToString(), Is.EqualTo("{\"Name\":\"Book\"}"));
        Assert.That(invocation.Arguments[2]!.ToString(), Is.EqualTo("EX 900"));
    }

    [Test]
    public async Task RemoveAsync_DeletesNamespacedKey()
    {
        var fixture = CreateFixture();
        fixture.Database
            .Setup(database => database.KeyDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var provider = fixture.CreateProvider();

        await provider.RemoveAsync("1");

        fixture.Database.Verify(database => database.KeyDeleteAsync(
                It.Is<RedisKey>(key => key == "orders:1"),
                CommandFlags.None),
            Times.Once);
    }

    [Test]
    public void GetAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var fixture = CreateFixture();
        var provider = fixture.CreateProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.That(
            async () => await provider.GetAsync<TestPayload>("1", cancellationTokenSource.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ClearAsync_DeletesOnlyKeysReturnedByConfiguredNamespacePattern()
    {
        var fixture = CreateFixture();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetEndPoints(false))
            .Returns([endpoint]);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetServer(endpoint, null))
            .Returns(server.Object);
        fixture.Database
            .SetupGet(database => database.Database)
            .Returns(4);
        server
            .Setup(redisServer => redisServer.Keys(
                4,
                It.Is<RedisValue>(pattern => pattern == "orders:*"),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns([(RedisKey)"orders:1", (RedisKey)"orders:2"]);
        fixture.Database
            .Setup(database => database.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var provider = fixture.CreateProvider();

        await provider.ClearAsync();

        fixture.Database.Verify(database => database.KeyDeleteAsync((RedisKey)"orders:1", CommandFlags.None), Times.Once);
        fixture.Database.Verify(database => database.KeyDeleteAsync((RedisKey)"orders:2", CommandFlags.None), Times.Once);
    }

    private static RedisProviderFixture CreateFixture(CacheProviderSettings? settings = null)
    {
        var fixture = new RedisProviderFixture(settings ?? CreateSettings());
        fixture.ConnectionMultiplexer
            .SetupGet(connection => connection.IsConnected)
            .Returns(true);
        fixture.ConnectionMultiplexer
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(fixture.Database.Object);
        fixture.Factory
            .Setup(factory => factory.GetConnection(fixture.Settings))
            .Returns(fixture.ConnectionMultiplexer.Object);

        return fixture;
    }

    private static CacheProviderSettings CreateSettings(
        string namespaceName = "orders",
        string connectionString = "localhost:6379") =>
        new()
        {
            Name = "OrdersRedis",
            Namespace = namespaceName,
            Type = ProviderType.Redis,
            ConnectionString = connectionString,
            ExpirationMinutes = 15,
            FailureThreshold = 3,
            DisconnectSeconds = 30
        };

    private static IInvocation AssertSingleInvocation(Mock<IDatabase> database, string methodName)
    {
        var invocations = database.Invocations
            .Where(invocation => invocation.Method.Name == methodName)
            .ToArray();

        Assert.That(invocations, Has.Length.EqualTo(1));
        return invocations[0];
    }

    private sealed record TestPayload(string Name);

    private sealed class RedisProviderFixture
    {
        private readonly AutoMocker _mocker = new();

        public RedisProviderFixture(CacheProviderSettings settings)
        {
            Settings = settings;
            _mocker.Use(settings);
        }

        public CacheProviderSettings Settings { get; }

        public Mock<IRedisConnectionFactory> Factory => _mocker.GetMock<IRedisConnectionFactory>();

        public Mock<IConnectionMultiplexer> ConnectionMultiplexer => _mocker.GetMock<IConnectionMultiplexer>();

        public Mock<IDatabase> Database => _mocker.GetMock<IDatabase>();

        public RedisCacheProvider CreateProvider() => _mocker.CreateInstance<RedisCacheProvider>();
    }
}
