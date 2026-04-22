using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.UnitTests.Authorization;

public class PatReadHandlerShould
{
    private readonly PatReadHandler _sut = new();

    private static PersonalAccessToken MakePat(long ownerAccountId) => new()
    {
        Id = new PersonalAccessTokenId(1),
        AccountId = new AccountId(ownerAccountId),
        TokenHash = new byte[] { 0x00 },
        TokenPrefix = "avp_abcd",
        Name = "t",
        Roles = AccountAccessLevel.Player,
        CreatedAt = DateTime.UtcNow,
    };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private async Task<bool> Run(ClaimsPrincipal user, PersonalAccessToken resource)
    {
        var req = new ReadRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource);
        await _sut.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact] public async Task Succeed_WhenCallerIsOwner() =>
        Assert.True(await Run(User(7, AvalonRoles.Player), MakePat(7)));

    [Fact] public async Task Succeed_WhenCallerIsAdmin() =>
        Assert.True(await Run(User(99, AvalonRoles.Admin), MakePat(7)));

    [Fact] public async Task Fail_WhenCallerIsGameMasterAndNotOwner() =>
        Assert.False(await Run(User(99, AvalonRoles.GameMaster), MakePat(7)));

    [Fact] public async Task Fail_WhenCallerIsPlayerAndNotOwner() =>
        Assert.False(await Run(User(99, AvalonRoles.Player), MakePat(7)));
}
