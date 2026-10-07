using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public class GameApplicationAccessPolicyShould
{
    private static StoreAuthenticationConfiguration Configuration() => new()
    {
        SteamAppId = 2499460,
        SteamPublisherKey = "private-test-secret",
        SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] },
    };

    [Fact]
    public void Omitted_selector_uses_main() => Assert.Equal(2499460u, Configuration().ResolveSteamApplication(null)!.AppId);

    [Fact]
    public void Explicit_zero_is_not_main() => Assert.Null(Configuration().ResolveSteamApplication(0));

    [Fact]
    public void Disabled_playtest_is_rejected()
    {
        StoreAuthenticationConfiguration config = Configuration();
        config.SteamPlaytest.Enabled = false;
        config.Validate(true);
        Assert.Null(config.ResolveSteamApplication(2514590));
    }

    [Fact]
    public void Duplicate_applications_are_invalid()
    {
        StoreAuthenticationConfiguration config = Configuration();
        config.SteamPlaytest.AppId = config.SteamAppId;
        Assert.Throws<InvalidOperationException>(() => config.Validate(true));
    }

    [Fact]
    public void Enabled_playtest_requires_worlds()
    {
        StoreAuthenticationConfiguration config = Configuration();
        config.SteamPlaytest.AllowedWorldIds = [];
        Assert.Throws<InvalidOperationException>(() => config.Validate(true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Restricted_application_never_allows_an_unlisted_world(ushort world, bool allowed)
    {
        StoreAuthenticationConfiguration config = Configuration();
        var policy = new GameApplicationAccessPolicy(Options.Create(config));
        Assert.Equal(allowed, policy.AllowsWorld(2514590, world));
        Assert.False(policy.AllowsApplication(0));
        Assert.False(policy.AllowsApplication(480));
        Assert.False(policy.AllowsWorld(2499460, 0));
        Assert.True(policy.AllowsWorld(2499460, 1));
        config.SteamPlaytest.Enabled = false;
        Assert.False(policy.AllowsWorld(2514590, 3));
    }

    [Fact]
    public void Invalid_world_restrictions_fail_validation()
    {
        StoreAuthenticationConfiguration config = Configuration();
        foreach (ushort[] worlds in new ushort[][] { [0], [3, 3] })
        {
            config.SteamPlaytest.AllowedWorldIds = worlds;
            Assert.Throws<InvalidOperationException>(() => config.Validate(true));
        }
        config.SteamPlaytest.AppId = 0;
        config.SteamPlaytest.AllowedWorldIds = [3];
        Assert.Throws<InvalidOperationException>(() => config.Validate(true));
    }
}
