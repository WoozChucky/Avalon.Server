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

public class WorldControllerShould
{
    private readonly IWorldService _service = Substitute.For<IWorldService>();

    private WorldController MakeSut(ClaimsPrincipal user) =>
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
        _service.ListAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<WorldDto>(1, 50, 0, new List<WorldDto>()));

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_Returns404_WhenMissing()
    {
        _service.GetAsync((ushort)1, Arg.Any<CancellationToken>()).Returns((WorldDto?)null);

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_Returns200_WhenFound()
    {
        _service.GetAsync((ushort)1, Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 1, Name = "n" });

        var sut = MakeSut(User(7, AvalonRoles.Player));
        var result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreated()
    {
        var request = new CreateWorldRequest { Name = "x", Host = "h", Port = 1, MinVersion = "0", Version = "0" };
        _service.CreateAsync(request, Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 42, Name = "x" });

        var sut = MakeSut(User(99, AvalonRoles.Admin));
        var result = await sut.Create(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Update_Returns404_WhenMissing()
    {
        _service.UpdateAsync((ushort)1, Arg.Any<UpdateWorldRequest>(), Arg.Any<CancellationToken>())
            .Returns((WorldDto?)null);

        var sut = MakeSut(User(99, AvalonRoles.Admin));
        var result = await sut.Update(1, new UpdateWorldRequest(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_Returns200_WhenUpdated()
    {
        _service.UpdateAsync((ushort)1, Arg.Any<UpdateWorldRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 1, Name = "updated" });

        var sut = MakeSut(User(99, AvalonRoles.Admin));
        var result = await sut.Update(1, new UpdateWorldRequest { Name = "updated" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
