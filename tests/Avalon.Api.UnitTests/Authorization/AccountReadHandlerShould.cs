using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.UnitTests.Authorization;

public class AccountReadHandlerShould
{
    private readonly AccountReadHandler _sut = new();

    private static Account MakeAccount(long id) => new()
    {
        Id = new AccountId(id),
        Username = "u",
        Email = "u@t",
        Salt = new byte[] { 1 },
        Verifier = new byte[] { 2 },
        JoinDate = DateTime.UtcNow,
    };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private async Task<bool> Run(ClaimsPrincipal user, Account resource)
    {
        var req = new ReadRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource);
        await _sut.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task Succeed_WhenSelf() =>
        Assert.True(await Run(User(7, AvalonRoles.Player), MakeAccount(7)));

    [Fact]
    public async Task Succeed_WhenGameMaster() =>
        Assert.True(await Run(User(99, AvalonRoles.GameMaster), MakeAccount(7)));

    [Fact]
    public async Task Fail_WhenPlayerNotSelf() =>
        Assert.False(await Run(User(99, AvalonRoles.Player), MakeAccount(7)));
}
