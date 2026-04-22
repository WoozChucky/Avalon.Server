using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.UnitTests.Authorization;

public class CharacterWriteHandlerShould
{
    private readonly CharacterWriteHandler _sut = new();

    private static Character MakeCharacter(long accountId) => new()
    {
        Id = new CharacterId(1),
        AccountId = new AccountId(accountId),
        Name = "c",
        CreationDate = DateTime.UtcNow,
    };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private async Task<bool> Run(ClaimsPrincipal user, Character resource)
    {
        var req = new WriteRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource);
        await _sut.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact] public async Task Succeed_WhenCallerIsOwner() =>
        Assert.True(await Run(User(7, AvalonRoles.Player), MakeCharacter(7)));

    [Fact] public async Task Succeed_WhenCallerIsAdmin() =>
        Assert.True(await Run(User(99, AvalonRoles.Admin), MakeCharacter(7)));

    [Fact] public async Task Fail_WhenCallerIsGameMasterAndNotOwner() =>
        Assert.False(await Run(User(99, AvalonRoles.GameMaster), MakeCharacter(7)));

    [Fact] public async Task Fail_WhenCallerIsPlayerAndNotOwner() =>
        Assert.False(await Run(User(99, AvalonRoles.Player), MakeCharacter(7)));
}
