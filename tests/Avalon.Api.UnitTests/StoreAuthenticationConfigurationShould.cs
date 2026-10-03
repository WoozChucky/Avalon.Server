using Avalon.Configuration;
using Xunit;

namespace Avalon.Api.UnitTests;

public class StoreAuthenticationConfigurationShould
{
    [Theory]
    [InlineData(480, "secret", "production", "avalon-auth-prod")]
    [InlineData(2499460, "", "production", "avalon-auth-prod")]
    [InlineData(2499460, "secret", "development", "avalon-auth-dev")]
    [InlineData(2499460, "secret", "production", "avalon-auth-dev")]
    public void Refuse_untrusted_production_configuration(uint appId, string key, string environment, string identity)
    {
        var config = new StoreAuthenticationConfiguration
        {
            SteamAppId = appId, SteamPublisherKey = key, Environment = environment, SteamIdentityPrefix = identity,
        };
        Assert.Throws<InvalidOperationException>(() => config.Validate(production: true));
    }

    [Fact]
    public void Use_a_fixed_product_and_disable_unimplemented_direct_grants()
    {
        var config = new StoreAuthenticationConfiguration { SteamPublisherKey = "private-test-secret" };
        config.Validate(production: true);
        Assert.Equal(2499460u, config.SteamAppId);
        Assert.Equal("avalon.base", StoreAuthenticationConfiguration.Product);
        Assert.False(config.DirectGrantsEnabled);
        config.DirectGrantsEnabled = true;
        Assert.Throws<InvalidOperationException>(() => config.Validate(production: true));
    }
}
