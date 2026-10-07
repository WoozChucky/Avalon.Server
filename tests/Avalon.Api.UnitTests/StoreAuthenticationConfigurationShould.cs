using Avalon.Api.Testing;
using Avalon.Configuration;
using Xunit;

namespace Avalon.Api.UnitTests;

public class StoreAuthenticationConfigurationShould
{
    [Fact]
    public void Require_an_explicit_deployment_app_id_without_a_builtin_default()
    {
        var config = new StoreAuthenticationConfiguration { SteamPublisherKey = "private-test-secret" };
        Assert.Equal(0u, config.SteamAppId);
        Assert.Throws<InvalidOperationException>(() => config.Validate(production: true));
    }

    [Theory]
    [InlineData(480)]
    [InlineData(123456)]
    public void Use_the_explicitly_configured_positive_app_id(uint appId)
    {
        var config = new StoreAuthenticationConfiguration { SteamAppId = appId, SteamPublisherKey = "private-test-secret" };
        config.Validate(production: true);
        Assert.Equal(appId, config.SteamAppId);
    }

    [Theory]
    [InlineData(0, "secret", "production", "avalon-auth-prod")]
    [InlineData(StoreAuthenticationTestData.SteamAppId, "", "production", "avalon-auth-prod")]
    [InlineData(StoreAuthenticationTestData.SteamAppId, "secret", "development", "avalon-auth-dev")]
    [InlineData(StoreAuthenticationTestData.SteamAppId, "secret", "production", "avalon-auth-dev")]
    public void Refuse_untrusted_production_configuration(uint appId, string key, string environment, string identity)
    {
        var config = new StoreAuthenticationConfiguration
        {
            SteamAppId = appId,
            SteamPublisherKey = key,
            Environment = environment,
            SteamIdentityPrefix = identity,
        };
        Assert.Throws<InvalidOperationException>(() => config.Validate(production: true));
    }

    [Fact]
    public void Use_a_fixed_product_and_disable_unimplemented_direct_grants()
    {
        var config = new StoreAuthenticationConfiguration { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "private-test-secret" };
        config.Validate(production: true);
        Assert.Equal(StoreAuthenticationTestData.SteamAppId, config.SteamAppId);
        Assert.Equal("avalon.base", StoreAuthenticationConfiguration.Product);
        Assert.False(config.DirectGrantsEnabled);
        config.DirectGrantsEnabled = true;
        Assert.Throws<InvalidOperationException>(() => config.Validate(production: true));
    }
}
