using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

public class JwtUtilsShould
{
    private static readonly AuthenticationConfig Config = new()
    {
        IssuerSigningKey = new string('k', 64),
        Issuer = "test",
        Audience = "test",
        ValidateIssuer = true,
        ValidateIssuerKey = true,
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
        var sut = new JwtUtils(Config);
        var token = sut.GenerateJwtToken(MakeAccount(AccountAccessLevel.Player));

        var groupSids = ReadGroupSids(token);

        Assert.Contains("Player", groupSids);
    }

    [Fact]
    public void EmitAllMatchingGroupSidClaims_WhenAccountHasMultipleFlags()
    {
        var sut = new JwtUtils(Config);
        var token = sut.GenerateJwtToken(MakeAccount(
            AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin));

        var groupSids = ReadGroupSids(token);

        Assert.Contains("Player",     groupSids);
        Assert.Contains("GameMaster", groupSids);
        Assert.Contains("Admin",      groupSids);
    }
}
