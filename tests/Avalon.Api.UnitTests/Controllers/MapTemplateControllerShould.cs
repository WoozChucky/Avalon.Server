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

public class MapTemplateControllerShould
{
    private readonly IMapService _service = Substitute.For<IMapService>();

    private MapTemplateController MakeSut(ClaimsPrincipal user) =>
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
    public async Task List_ReturnsPage()
    {
        _service
            .ListAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<MapTemplateDto>(1, 50, 0, new List<MapTemplateDto>()));

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_Returns404_WhenMissing()
    {
        _service
            .GetAsync(1, Arg.Any<CancellationToken>())
            .Returns((MapTemplateDto?)null);

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_Returns200_WhenFound()
    {
        _service
            .GetAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MapTemplateDto { Id = 1, Name = "Stormwind" });

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
