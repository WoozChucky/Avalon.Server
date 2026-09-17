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
            .GetOnlineAsync(Arg.Any<PresencePaginateFilters>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<OnlinePlayerDto>(1, 20, 0, new List<OnlinePlayerDto>()));

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetOnline(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetPlayerPresence_Returns404_WhenNotPresent()
    {
        _service
            .GetPlayerPresenceAsync(42, Arg.Any<CancellationToken>())
            .Returns((PlayerPresenceDto?)null);

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetPlayerPresence(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPlayerPresence_Returns200_WhenPresent()
    {
        _service
            .GetPlayerPresenceAsync(42, Arg.Any<CancellationToken>())
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
            .GetInstancePresenceAsync(instanceId, Arg.Any<CancellationToken>())
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
            .GetInstancePresenceAsync(instanceId, Arg.Any<CancellationToken>())
            .Returns(new InstancePresenceDto { InstanceId = instanceId });

        var sut = MakeSut(User(7, AvalonRoles.GameMaster));
        var result = await sut.GetInstancePresence(instanceId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
