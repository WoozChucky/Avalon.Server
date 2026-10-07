using Avalon.Common.Accounts;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class AvalonWorldAdmissionShould
{
    private readonly GameApplicationAccessPolicy _policy = new(Options.Create(new StoreAuthenticationConfiguration
    {
        SteamAppId = 2499460,
        SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] },
        AdditionalApplications = new() { ["test.base"] = new() { Provider = "test-store", ProviderProductId = "opaque" } },
    }));
    [Theory]
    [InlineData("avalon.base")]
    [InlineData("steam.main")]
    [InlineData("test.base")]
    public void Common_unrestricted_admission_preserves_account_roles(string application)
    {
        Assert.True(_policy.AllowsWorld(application, 1));
        Assert.True(_policy.AllowsWorldAccess(application, 1, AccountAccessLevel.Player, AccountAccessLevel.Player));
        Assert.False(_policy.AllowsWorldAccess(application, 3, AccountAccessLevel.PTR, AccountAccessLevel.Player));
        Assert.False(_policy.RequiresLicenseForWorldListing(application));
    }
    [Fact]
    public void Playtest_source_remains_PTR_only_regardless_of_other_account_licenses()
    {
        Assert.True(_policy.AllowsWorldAccess("steam.playtest", 3, AccountAccessLevel.PTR, AccountAccessLevel.Player));
        Assert.False(_policy.AllowsWorld("steam.playtest", 1));
        Assert.False(_policy.AllowsWorld("unknown", 3));
        Assert.True(_policy.RequiresLicenseForWorldListing("steam.playtest"));
    }
    [Fact]
    public void Native_authority_window_does_not_require_a_store_identity_timestamp()
    {
        DateTime now = DateTime.UtcNow;
        var context = new GameContextRecord
        {
            AuthorizationValidUntil = now.AddMinutes(5),
            AbsoluteExpiresAt = now.AddHours(1),
            ProtocolVersion = "1",
            Environment = "production",
            State = "authorized",
            CredentialDigest = "",
            RefreshDigest = ""
        };
        Assert.Equal(now.AddMinutes(5), GameContextAuthorizationWindow.Deadline(context));
        Assert.Equal(now.AddMinutes(1), GameContextAuthorizationWindow.Deadline(context with { IdentityValidUntil = now.AddMinutes(1) }));
        Assert.Null(GameContextAuthorizationWindow.Deadline(context with { AuthorizationValidUntil = null }));
    }
}
