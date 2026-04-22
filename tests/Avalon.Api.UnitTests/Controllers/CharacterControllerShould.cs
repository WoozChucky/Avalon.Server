using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class CharacterControllerShould
{
    private readonly ICharacterService _service = Substitute.For<ICharacterService>();
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();
    private readonly IAuthContext _authContext = Substitute.For<IAuthContext>();

    private CharacterController MakeSut(ClaimsPrincipal user) =>
        new(_authContext, _service, _authz)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private static Character MakeChar(long accountId) => new()
    {
        Id = new CharacterId(42),
        AccountId = new AccountId(accountId),
        Name = "c",
        CreationDate = DateTime.UtcNow,
    };

    [Fact]
    public async Task GetById_Returns200_WhenAuthzSucceeds()
    {
        var user = User(7, AvalonRoles.Player);
        var ch = MakeChar(7);
        _service.GetCharacterByIdAsync(new CharacterId(42), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetById_Returns404_WhenEntityMissing()
    {
        var user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        var user = User(7, AvalonRoles.Player);
        var ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
