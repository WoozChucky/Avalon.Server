using System.Security.Claims;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Api.UnitTests.Services;
using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Controllers;

/// <summary>
/// #452: the REST world list and lookup must show a caller only the worlds they may enter, by the
/// same rule the TCP world list applies (<see cref="AccessLevels.ForWorld"/>). Runs the real
/// controller, service and repository over SQLite, so paging is counted by the database.
/// </summary>
public sealed class WorldVisibilityShould : IDisposable
{
    private const ushort PlayerWorld = 101;
    private const ushort AdminWorld = 102;
    private const ushort PtrWorld = 103;

    private readonly SqliteAuthDatabase _database = new();

    public WorldVisibilityShould()
    {
        using var context = _database.CreateDbContext();
        // Replace the seeded worlds so the test states every world it reasons about.
        context.Worlds.RemoveRange(context.Worlds.ToList());
        context.Worlds.AddRange(
            World(PlayerWorld, "Players", AccountAccessLevel.Player),
            World(AdminWorld, "Staff", AccountAccessLevel.Admin),
            World(PtrWorld, "TestRealm", AccountAccessLevel.PTR));
        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private static WorldEntity World(ushort id, string name, AccountAccessLevel required) => new()
    {
        Id = new WorldId(id),
        Name = name,
        Host = $"{name}.example",
        Port = 21000 + id,
        MinVersion = "0.0.1",
        Version = "0.0.1",
        AccessLevelRequired = required,
        UpdatedAt = DateTime.UtcNow,
    };

    private WorldController MakeSut(AccountAccessLevel level) =>
        new(new WorldService(new WorldRepository(_database)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(level) }
            }
        };

    // The claims JwtUtils and AvalonAuthenticationHandler emit: one GroupSid per set flag.
    private static ClaimsPrincipal Principal(AccountAccessLevel level)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7") };
        claims.AddRange(Enum.GetValues<AccountAccessLevel>()
            .Where(flag => level.HasFlag(flag))
            .Select(flag => new Claim(ClaimTypes.GroupSid, flag.ToString())));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.GroupSid));
    }

    [Fact]
    public async Task List_only_the_worlds_a_player_may_enter()
    {
        var result = await MakeSut(AccountAccessLevel.Player).List(1, 50, CancellationToken.None);

        Assert.Equal(new[] { PlayerWorld }, result.Items.Select(w => w.Id));
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Count_only_visible_worlds_when_paging()
    {
        var result = await MakeSut(AccountAccessLevel.Player).List(1, 1, CancellationToken.None);

        Assert.Equal(new[] { PlayerWorld }, result.Items.Select(w => w.Id));
        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player, new[] { PlayerWorld })]
    [InlineData(AccountAccessLevel.PTR, new[] { PlayerWorld, PtrWorld })]
    [InlineData(AccountAccessLevel.Tournament, new[] { PlayerWorld })]
    [InlineData(AccountAccessLevel.GameMaster, new[] { PlayerWorld, PtrWorld })]
    [InlineData(AccountAccessLevel.Admin, new[] { PlayerWorld, AdminWorld, PtrWorld })]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.Admin, new[] { PlayerWorld, AdminWorld, PtrWorld })]
    public async Task List_what_the_tcp_world_list_would_show(AccountAccessLevel level, ushort[] expected)
    {
        var result = await MakeSut(level).List(1, 50, CancellationToken.None);

        Assert.Equal(expected.Order(), result.Items.Select(w => w.Id).Order());
        Assert.Equal(expected.Length, result.TotalCount);
    }

    [Fact]
    public async Task List_nothing_for_a_caller_with_no_access_level()
    {
        var result = await MakeSut(0).List(1, 50, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData(AdminWorld)]
    [InlineData(PtrWorld)]
    public async Task Answer_404_for_a_world_a_player_may_not_enter(ushort id)
    {
        var result = await MakeSut(AccountAccessLevel.Player).Get(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Return_a_world_a_player_may_enter()
    {
        var result = await MakeSut(AccountAccessLevel.Player).Get(PlayerWorld, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(PlayerWorld, Assert.IsType<WorldDto>(ok.Value).Id);
    }

    [Fact]
    public async Task Return_a_staff_world_to_staff()
    {
        var result = await MakeSut(AccountAccessLevel.Admin).Get(AdminWorld, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
