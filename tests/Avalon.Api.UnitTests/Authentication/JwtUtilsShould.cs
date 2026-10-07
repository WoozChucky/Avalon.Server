using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

public class JwtUtilsShould
{
    private static readonly AuthenticationConfig s_config = new()
    {
        IssuerSigningKey = new string('k', 64),
        Issuer = "test",
        Audience = "test",
        ValidateIssuer = true,
        ValidateAudience = true,
        ClockSkewInMinutes = 1,
    };

    private static Account MakeAccount(AccountAccessLevel level) => new()
    {
        Id = new AccountId(7),
        Username = "u",
        Email = "u@t",
        Salt = new byte[] { 1 },
        Verifier = new byte[] { 2 },
        JoinDate = DateTime.UtcNow,
        AccessLevel = level,
    };

    // The short JWT claim name that ClaimTypes.GroupSid is mapped to by JwtSecurityTokenHandler's
    // outbound claim type map. JwtSecurityToken.Claims exposes raw JWT payload claim types
    // (pre-inbound-mapping), so we match against the short form actually present on the wire.
    private const string GroupSidJwtClaim = "groupsid";

    private static string[] ReadGroupSids(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type == GroupSidJwtClaim || c.Type == ClaimTypes.GroupSid)
            .Select(c => c.Value)
            .ToArray();

    [Fact]
    public void EmitPlayerGroupSidClaim_WhenAccountHasPlayerFlagOnly()
    {
        var sut = new JwtUtils(s_config, JwtSigningKey.Create(s_config));
        string token = sut.GenerateJwtToken(MakeAccount(AccountAccessLevel.Player));

        string[] groupSids = ReadGroupSids(token);

        Assert.Contains("Player", groupSids);
    }

    [Fact]
    public void EmitAllMatchingGroupSidClaims_WhenAccountHasMultipleFlags()
    {
        var sut = new JwtUtils(s_config, JwtSigningKey.Create(s_config));
        string token = sut.GenerateJwtToken(MakeAccount(
            AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin));

        string[] groupSids = ReadGroupSids(token);

        Assert.Contains("Player", groupSids);
        Assert.Contains("GameMaster", groupSids);
        Assert.Contains("Admin", groupSids);
    }

    [Fact]
    public void EmitLauncherFamilyOnlyForLauncherToken()
    {
        var sut = new JwtUtils(s_config, JwtSigningKey.Create(s_config));
        Account account = MakeAccount(AccountAccessLevel.Player);
        var familyId = Guid.Parse("12345678-1234-1234-1234-123456789abc");

        JwtSecurityToken website = new JwtSecurityTokenHandler().ReadJwtToken(sut.GenerateJwtToken(account));
        JwtSecurityToken launcher = new JwtSecurityTokenHandler().ReadJwtToken(sut.GenerateLauncherJwtToken(account, familyId));

        Assert.DoesNotContain(website.Claims, claim => claim.Type == JwtUtils.LauncherFamilyClaim);
        Assert.Equal(familyId.ToString(), launcher.Claims.Single(claim => claim.Type == JwtUtils.LauncherFamilyClaim).Value);
    }
}
