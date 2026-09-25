using System.Security.Claims;
using Avalon.Api.Authentication;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

public class ClaimsPrincipalExtensionsShould
{
    private static ClaimsPrincipal Principal(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            authenticationType: "test",
            nameType: ClaimTypes.NameIdentifier,
            roleType: ClaimTypes.Role));

    [Fact]
    public void ReturnAccountId_WhenNameIdentifierPresent()
    {
        var user = Principal((ClaimTypes.NameIdentifier, "42"));
        Assert.Equal(42L, user.AccountId().Value);
    }

    [Fact]
    public void Throw_WhenNameIdentifierMissing()
    {
        var user = Principal();
        Assert.Throws<InvalidOperationException>(() => user.AccountId());
    }

    [Theory]
    [InlineData("Player",     "Player",     true)]
    [InlineData("GameMaster", "Player",     true)]
    [InlineData("Admin",      "GameMaster", true)]
    [InlineData("Console",    "Admin",      true)]
    [InlineData("Player",     "GameMaster", false)]
    [InlineData("GameMaster", "Admin",      false)]
    [InlineData("Admin",      "Console",    false)]
    // #447: Tournament and PTR are players with the Player permission set, and nothing more.
    [InlineData("Tournament", "Player",     true)]
    [InlineData("PTR",        "Player",     true)]
    [InlineData("Tournament", "GameMaster", false)]
    [InlineData("PTR",        "GameMaster", false)]
    [InlineData("PTR",        "Admin",      false)]
    public void HasRoleAtLeast_FollowsHierarchy(string callerRole, string minRole, bool expected)
    {
        var user = Principal((ClaimTypes.Role, callerRole));
        Assert.Equal(expected, user.HasRoleAtLeast(minRole));
    }

    [Fact]
    public void AccessLevel_FoldsEveryGroupSidFlag()
    {
        var user = Principal(
            (ClaimTypes.GroupSid, "Player"),
            (ClaimTypes.GroupSid, "PTR"),
            (ClaimTypes.GroupSid, "Admin"));

        Assert.Equal(
            Avalon.Common.Accounts.AccountAccessLevel.Player
            | Avalon.Common.Accounts.AccountAccessLevel.PTR
            | Avalon.Common.Accounts.AccountAccessLevel.Admin,
            user.AccessLevel());
    }

    [Fact]
    public void AccessLevel_IgnoresOtherClaimsAndUnknownValues()
    {
        var user = Principal(
            (ClaimTypes.Role, "Admin"),
            (ClaimTypes.GroupSid, "Banana"),
            (ClaimTypes.GroupSid, "4"),
            (ClaimTypes.GroupSid, "Player"));

        Assert.Equal(Avalon.Common.Accounts.AccountAccessLevel.Player, user.AccessLevel());
    }

    [Fact]
    public void AccessLevel_IsNoneWithoutGroupSidClaims()
        => Assert.Equal((Avalon.Common.Accounts.AccountAccessLevel)0, Principal().AccessLevel());

    [Fact]
    public void HasRoleAtLeast_FalseForUnknownMinRole()
    {
        var user = Principal((ClaimTypes.Role, "Admin"));
        Assert.False(user.HasRoleAtLeast("Banana"));
    }
}
