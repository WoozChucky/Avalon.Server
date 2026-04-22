using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Controllers;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class MapTemplateControllerShould
{
    private readonly IMapTemplateRepository _repository = Substitute.For<IMapTemplateRepository>();

    private MapTemplateController MakeSut(ClaimsPrincipal user) =>
        new(_repository)
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
        _repository
            .PaginateAsync(Arg.Any<EntityPaginateFilter<MapTemplate>>(), false, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<MapTemplate>(1, 50, 0, new List<MapTemplate>()));

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_Returns404_WhenMissing()
    {
        _repository
            .FindByIdAsync(Arg.Any<MapTemplateId>(), false, Arg.Any<CancellationToken>())
            .Returns((MapTemplate?)null);

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_Returns200_WhenFound()
    {
        _repository
            .FindByIdAsync(Arg.Any<MapTemplateId>(), false, Arg.Any<CancellationToken>())
            .Returns(new MapTemplate { Id = new MapTemplateId(1), Name = "Stormwind", Description = "", Directory = "" });

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
