using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authorization;
using Avalon.Api.Worlds.Authorization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Authorization;

public class CharacterReadHandlerShould
{
    private readonly CharacterReadHandler _sut = new();

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
        var req = new ReadRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource);
        await _sut.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Theory]
    [InlineData(7, AvalonRoles.Player, true)]
    [InlineData(99, AvalonRoles.GameMaster, true)]
    [InlineData(99, AvalonRoles.Admin, true)]
    [InlineData(99, AvalonRoles.Player, false)]
    public async Task Let_the_owner_and_staff_read(long caller, string role, bool allowed) =>
        Assert.Equal(allowed, await Run(User(caller, role), MakeCharacter(7)));
}
