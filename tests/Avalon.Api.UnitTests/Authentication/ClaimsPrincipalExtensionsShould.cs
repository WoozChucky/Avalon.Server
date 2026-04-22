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
    public void HasRoleAtLeast_FollowsHierarchy(string callerRole, string minRole, bool expected)
    {
        var user = Principal((ClaimTypes.Role, callerRole));
        Assert.Equal(expected, user.HasRoleAtLeast(minRole));
    }

    [Fact]
    public void HasRoleAtLeast_FalseForUnknownMinRole()
    {
        var user = Principal((ClaimTypes.Role, "Admin"));
        Assert.False(user.HasRoleAtLeast("Banana"));
    }
}
