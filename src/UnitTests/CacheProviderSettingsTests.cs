using CacheManager.Settings;
using CacheManager.Types;

namespace CacheManager.UnitTests;

[TestFixture]
public sealed class CacheProviderSettingsTests
{
    [Test]
    public void Validate_WhenMemoryProviderHasNoConnectionString_ReturnsNoErrors()
    {
        var settings = new CacheProviderSettings
        {
            Name = "Local",
            Type = ProviderType.Memory
        };

        var errors = settings.Validate();

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Validate_WhenProviderNameIsBlank_ReturnsNameError()
    {
        var settings = new CacheProviderSettings
        {
            Name = " ",
            Type = ProviderType.Memory
        };

        var errors = settings.Validate();

        Assert.That(errors, Does.Contain("[CacheManager][Validator] Cache name is required"));
    }

    [Test]
    public void Validate_WhenRedisConnectionStringIsBlank_ReturnsConnectionStringError()
    {
        var settings = new CacheProviderSettings
        {
            Name = "Remote",
            Type = ProviderType.Redis,
            ConnectionString = " "
        };

        var errors = settings.Validate();

        Assert.That(errors, Does.Contain("[CacheManager][Validator] Cache connection string is required"));
    }

    [Test]
    public void CacheSettings_InitializesProvidersCollection()
    {
        var settings = new CacheSettings();

        Assert.That(settings.Providers, Is.Not.Null);
        Assert.That(settings.Providers, Is.Empty);
    }
}
