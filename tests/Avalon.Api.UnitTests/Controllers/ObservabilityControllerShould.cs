using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.UnitTests.Controllers;

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

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetOnline(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetPlayerPresence_Returns404_WhenNotPresent()
    {
        _service
            .GetPlayerPresenceAsync(42, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns((PlayerPresenceDto?)null);

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetPlayerPresence(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPlayerPresence_Returns200_WhenPresent()
    {
        _service
            .GetPlayerPresenceAsync(42, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new PlayerPresenceDto());

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetPlayerPresence(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetInstancePresence_Returns404_WhenInstanceMissing()
    {
        var instanceId = Guid.NewGuid();
        _service
            .GetInstancePresenceAsync(instanceId, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns((InstancePresenceDto?)null);

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetInstancePresence(instanceId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetInstancePresence_Returns200_WhenInstanceFound()
    {
        var instanceId = Guid.NewGuid();
        _service
            .GetInstancePresenceAsync(instanceId, Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new InstancePresenceDto { InstanceId = instanceId });

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetInstancePresence(instanceId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Pass_the_callers_access_level_to_the_service()
    {
        var instanceId = Guid.NewGuid();
        ClaimsPrincipal user = User(7, AvalonRoles.GameMaster);
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.GroupSid, nameof(AccountAccessLevel.GameMaster)));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.GroupSid, nameof(AccountAccessLevel.PTR)));
        const AccountAccessLevel expected = AccountAccessLevel.GameMaster | AccountAccessLevel.PTR;
        var sut = MakeSut(user);

        await sut.GetOnline(new PresencePaginateFilters(), CancellationToken.None);
        await sut.GetPlayerPresence(42, CancellationToken.None);
        await sut.GetInstancePresence(instanceId, CancellationToken.None);

        await _service.Received(1).GetOnlineAsync(Arg.Any<PresencePaginateFilters>(), expected, Arg.Any<CancellationToken>());
        await _service.Received(1).GetPlayerPresenceAsync(42, expected, Arg.Any<CancellationToken>());
        await _service.Received(1).GetInstancePresenceAsync(instanceId, expected, Arg.Any<CancellationToken>());
    }
}
