using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authorization;
using Avalon.Api.Identity.Authorization;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authorization;

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

    [Theory]
    [InlineData(7, AvalonRoles.Player, true)]
    [InlineData(99, AvalonRoles.Admin, true)]
    [InlineData(99, AvalonRoles.GameMaster, false)]
    [InlineData(99, AvalonRoles.Player, false)]
    public async Task Let_the_owner_and_admins_read(long caller, string role, bool allowed) =>
        Assert.Equal(allowed, await Run(User(caller, role), MakePat(7)));
}
