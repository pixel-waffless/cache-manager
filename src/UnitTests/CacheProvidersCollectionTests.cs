using CacheManager.Settings;
using CacheManager.Types;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class CacheProvidersCollectionTests
{
    [Test]
    public void TryAddProvider_WhenNameAlreadyExistsWithDifferentCase_ReturnsFalse()
    {
        var collection = new CacheProvidersCollection();
        var settings = CreateSettings("Default");

        var firstAdded = collection.TryAddProvider("Default", settings);
        var secondAdded = collection.TryAddProvider("default", CreateSettings("default"));

        Assert.That(firstAdded, Is.True);
        Assert.That(secondAdded, Is.False);
    }

    [Test]
    public void TryGetProvider_UsesCaseInsensitiveLookup()
    {
        var collection = new CacheProvidersCollection();
        var settings = CreateSettings("Default");
        collection.TryAddProvider("Default", settings);

        var found = collection.TryGetProvider("DEFAULT", out var resolvedSettings);

        Assert.That(found, Is.True);
        Assert.That(resolvedSettings, Is.SameAs(settings));
    }

    [Test]
    public void TryRemoveProvider_UsesCaseInsensitiveLookup()
    {
        var collection = new CacheProvidersCollection();
        collection.TryAddProvider("Default", CreateSettings("Default"));

        var removed = collection.TryRemoveProvider("default");
        var foundAfterRemove = collection.TryGetProvider("Default", out _);

        Assert.That(removed, Is.True);
        Assert.That(foundAfterRemove, Is.False);
    }

    private static CacheProviderSettings CreateSettings(string name) =>
        new()
        {
            Name = name,
            Type = ProviderType.Memory
        };
}
