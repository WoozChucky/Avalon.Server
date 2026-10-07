using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authorization;
using Avalon.Api.Identity.Authorization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authorization;

public class AccountWriteHandlerShould
{
    private readonly AccountWriteHandler _sut = new();

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
        var req = new WriteRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource);
        await _sut.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Theory]
    [InlineData(7, AvalonRoles.Player, true)]
    [InlineData(99, AvalonRoles.Admin, true)]
    [InlineData(99, AvalonRoles.GameMaster, false)]
    [InlineData(99, AvalonRoles.Player, false)]
    public async Task Let_the_owner_and_admins_write(long caller, string role, bool allowed) =>
        Assert.Equal(allowed, await Run(User(caller, role), MakeAccount(7)));
}
