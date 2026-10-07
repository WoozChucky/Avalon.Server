using System.Security.Claims;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Controllers;
using Avalon.Api.Worlds.Services;
using Avalon.Database;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.Worlds.UnitTests.Controllers;

public class ObservabilityControllerShould
{
    private readonly IObservabilityService _service = Substitute.For<IObservabilityService>();

    private ObservabilityController MakeSut(ClaimsPrincipal user) =>
        new(_service)
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

    [Fact]
    public async Task GetOnline_ReturnsPage()
    {
        _service
            .GetOnlineAsync(Arg.Any<PresencePaginateFilters>(), Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<OnlinePlayerDto>(1, 20, 0, new List<OnlinePlayerDto>()));

        ObservabilityController sut = MakeSut(User(7, AvalonRoles.GameMaster));
        PagedResult<OnlinePlayerDto> result = await sut.GetOnline(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetInstancePresence_Returns404_WhenInstanceMissing()
    {
        var instanceId = Guid.NewGuid();
        _service
            .GetInstancePresenceAsync(instanceId, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns((InstancePresenceDto?)null);

        ObservabilityController sut = MakeSut(User(7, AvalonRoles.GameMaster));
        IActionResult result = await sut.GetInstancePresence(instanceId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetInstancePresence_Returns200_WhenInstanceFound()
    {
        var instanceId = Guid.NewGuid();
        _service
            .GetInstancePresenceAsync(instanceId, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new InstancePresenceDto { InstanceId = instanceId });

        ObservabilityController sut = MakeSut(User(7, AvalonRoles.GameMaster));
        IActionResult result = await sut.GetInstancePresence(instanceId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Pass_the_callers_access_level_to_the_service()
    {
        var instanceId = Guid.NewGuid();
        ClaimsPrincipal user = User(7, AvalonRoles.GameMaster);
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.GroupSid, nameof(AccountAccessLevel.GameMaster)));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.GroupSid, nameof(AccountAccessLevel.PTR)));
        const AccountAccessLevel Expected = AccountAccessLevel.GameMaster | AccountAccessLevel.PTR;
        ObservabilityController sut = MakeSut(user);

        await sut.GetOnline(new PresencePaginateFilters(), CancellationToken.None);
        await sut.GetInstancePresence(instanceId, CancellationToken.None);

        await _service.Received(1).GetOnlineAsync(Arg.Any<PresencePaginateFilters>(), Expected, Arg.Any<CancellationToken>());
        await _service.Received(1).GetInstancePresenceAsync(instanceId, Expected, Arg.Any<CancellationToken>());
    }
}

/// <summary>Per-character presence names its world (#556): the world the route selected, never another.</summary>
public class WorldObservabilityControllerShould
{
    private readonly IObservabilityService _service = Substitute.For<IObservabilityService>();
    private readonly CurrentWorld _world = new();

    private WorldObservabilityController MakeSut(AccountAccessLevel level)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "test");
        identity.AddClaim(new Claim(ClaimTypes.GroupSid, level.ToString()));
        _world.Select(new WorldId(2), "Boreal");
        return new WorldObservabilityController(_service, _world)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    [Fact]
    public async Task Answer_404_when_the_character_is_not_online_in_the_world()
    {
        _service.GetPlayerPresenceAsync(Arg.Any<WorldId>(), 42, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns((PlayerPresenceDto?)null);

        Assert.IsType<NotFoundResult>(await MakeSut(AccountAccessLevel.GameMaster).GetPlayerPresence(42, CancellationToken.None));
    }

    [Fact]
    public async Task Read_the_selected_worlds_presence_with_the_callers_access_level()
    {
        _service.GetPlayerPresenceAsync(Arg.Any<WorldId>(), 42, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new PlayerPresenceDto());

        IActionResult result = await MakeSut(AccountAccessLevel.GameMaster).GetPlayerPresence(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await _service.Received(1).GetPlayerPresenceAsync(
            Arg.Is<WorldId>(w => w.Value == 2), 42, AccountAccessLevel.GameMaster, Arg.Any<CancellationToken>());
    }
}
