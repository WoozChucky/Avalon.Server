using System.Reflection;
using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldMaintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class WorldControllerShould
{
    private readonly IWorldService _service = Substitute.For<IWorldService>();
    private readonly IWorldMaintenanceRepository _maintenance = Substitute.For<IWorldMaintenanceRepository>();
    private readonly IWorldMaintenanceControl _control = Substitute.For<IWorldMaintenanceControl>();
    private readonly IWorldReadiness _readiness = Substitute.For<IWorldReadiness>();

    private WorldController MakeSut(ClaimsPrincipal user) =>
        new(_service, _maintenance, _control, _readiness)
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
        _service.ListAsync(Arg.Any<Avalon.Common.Accounts.AccountAccessLevel>(), 1, 50,
                Arg.Any<CancellationToken>(), null, SortDirection.Ascending)
            .Returns(new PagedResult<WorldDto>(1, 50, 0, new List<WorldDto>()));

        WorldController sut = MakeSut(User(7, AvalonRoles.Player));
        PagedResult<WorldDto> result = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_Returns404_WhenMissing()
    {
        _service.GetAsync((ushort)1, Arg.Any<Avalon.Common.Accounts.AccountAccessLevel>(), Arg.Any<CancellationToken>()).Returns((WorldDto?)null);

        WorldController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_Returns200_WhenFound()
    {
        _service.GetAsync((ushort)1, Arg.Any<Avalon.Common.Accounts.AccountAccessLevel>(), Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 1, Name = "n" });

        WorldController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreated()
    {
        var request = new CreateWorldRequest { Name = "x", Host = "h", Port = 1, MinVersion = "0", Version = "0" };
        _service.CreateAsync(request, Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 42, Name = "x" });

        WorldController sut = MakeSut(User(99, AvalonRoles.Admin));
        IActionResult result = await sut.Create(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Update_Returns404_WhenMissing()
    {
        _service.UpdateAsync((ushort)1, Arg.Any<UpdateWorldRequest>(), Arg.Any<CancellationToken>())
            .Returns((WorldDto?)null);

        WorldController sut = MakeSut(User(99, AvalonRoles.Admin));
        IActionResult result = await sut.Update(1, new UpdateWorldRequest(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_Returns200_WhenUpdated()
    {
        _service.UpdateAsync((ushort)1, Arg.Any<UpdateWorldRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WorldDto { Id = 1, Name = "updated" });

        WorldController sut = MakeSut(User(99, AvalonRoles.Admin));
        IActionResult result = await sut.Update(1, new UpdateWorldRequest { Name = "updated" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Maintenance_actions_return_persisted_state_and_validate_grace()
    {
        DateTime now = DateTime.UtcNow;
        var enabled = new WorldMaintenanceState(true, 7, now.AddMinutes(5));
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>()).Returns(enabled);
        _control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(enabled);
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        WorldController sut = MakeSut(User(7, AvalonRoles.Admin));

        OkObjectResult read = Assert.IsType<OkObjectResult>(await sut.GetMaintenance(1, CancellationToken.None));
        Assert.True(Assert.IsType<WorldMaintenanceDto>(read.Value).Ready);
        OkObjectResult post = Assert.IsType<OkObjectResult>(await sut.EnableMaintenance(1,
            new WorldMaintenanceRequest(), CancellationToken.None));
        Assert.Equal(now.AddMinutes(5), Assert.IsType<WorldMaintenanceDto>(post.Value).DeadlineUtc);
        Assert.IsType<BadRequestResult>(await sut.EnableMaintenance(1,
            new WorldMaintenanceRequest { GraceMinutes = 61 }, CancellationToken.None));
        Assert.IsType<BadRequestResult>(await sut.EnableMaintenance(1,
            new WorldMaintenanceRequest { GraceMinutes = 0 }, CancellationToken.None));
        await _control.Received(1).SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5),
            "account:7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disable_maintenance_uses_the_shared_control()
    {
        var disabled = new WorldMaintenanceState(false, 8, null);
        _control.SetAsync(new WorldId(1), false, TimeSpan.FromMinutes(5), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(disabled);
        WorldController sut = MakeSut(User(7, AvalonRoles.Admin));

        OkObjectResult result = Assert.IsType<OkObjectResult>(await sut.DisableMaintenance(1, CancellationToken.None));

        Assert.False(Assert.IsType<WorldMaintenanceDto>(result.Value).Enabled);
        await _control.Received(1).SetAsync(new WorldId(1), false, TimeSpan.FromMinutes(5),
            "account:7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Maintenance_missing_world_is_404()
    {
        WorldController sut = MakeSut(User(7, AvalonRoles.Admin));
        Assert.IsType<NotFoundResult>(await sut.GetMaintenance(999, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await sut.EnableMaintenance(999, new WorldMaintenanceRequest(),
            CancellationToken.None));
        Assert.IsType<NotFoundResult>(await sut.DisableMaintenance(999, CancellationToken.None));
    }

    [Theory]
    [InlineData(nameof(WorldController.GetMaintenance))]
    [InlineData(nameof(WorldController.EnableMaintenance))]
    [InlineData(nameof(WorldController.DisableMaintenance))]
    public void Maintenance_actions_require_admin_policy(string action)
    {
        MethodInfo method = typeof(WorldController).GetMethod(action)!;
        AuthorizeAttribute attribute = Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),
            false).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal(AvalonRoles.Admin, attribute.Policy);
    }
}
