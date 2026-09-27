using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Services;

/// <summary>GET /world (#523): each world says whether this api serves it and whether it is available.</summary>
public sealed class WorldListFlagsShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();
    private readonly WorldDatabases _databases = new(
    [
        new ConfiguredWorld(new WorldId(101), "Host=w101", "Host=c101"),
        new ConfiguredWorld(new WorldId(102), "Host=w102", "Host=c102"),
        new ConfiguredWorld(new WorldId(104), "Host=w104", "Host=c104"),
    ]);

    public WorldListFlagsShould()
    {
        _databases.MarkUnavailable(new WorldId(102));
        using var context = _database.CreateDbContext();
        context.Worlds.RemoveRange(context.Worlds.ToList());
        context.Worlds.AddRange(
            World(101, AccountAccessLevel.Player), World(102, AccountAccessLevel.Player),
            World(103, AccountAccessLevel.Player), World(104, AccountAccessLevel.Admin));
        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private static WorldEntity World(ushort id, AccountAccessLevel required) => new()
    {
        Id = new WorldId(id), Name = $"World{id}", Host = "h", Port = 21000 + id,
        MinVersion = "0.0.1", Version = "0.0.1", AccessLevelRequired = required, UpdatedAt = DateTime.UtcNow,
    };

    private WorldService Sut() => new(new WorldRepository(_database), _databases);

    [Fact]
    public async Task Flag_each_listed_world_as_configured_and_available_or_not()
    {
        var page = await Sut().ListAsync(AccountAccessLevel.Player, 1, 50);

        Assert.Equal(new ushort[] { 101, 102, 103 }, page.Items.Select(w => w.Id).Order());
        WorldDto Get(ushort id) => page.Items.Single(w => w.Id == id);
        Assert.True(Get(101).Configured); Assert.True(Get(101).Available);
        Assert.True(Get(102).Configured); Assert.False(Get(102).Available);
        Assert.False(Get(103).Configured); Assert.False(Get(103).Available);
    }

    [Fact]
    public async Task Flag_a_single_world_lookup()
    {
        WorldDto? world = await Sut().GetAsync(102, AccountAccessLevel.Player);

        Assert.True(world!.Configured);
        Assert.False(world.Available);
    }

    [Fact]
    public async Task Keep_hiding_a_configured_world_the_caller_may_not_enter()
    {
        Assert.Null(await Sut().GetAsync(104, AccountAccessLevel.Player));
        Assert.DoesNotContain((await Sut().ListAsync(AccountAccessLevel.Player, 1, 50)).Items, w => w.Id == 104);
    }

    [Theory]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Tournament)]
    public async Task Hide_a_configured_admin_world_from_ptr_and_tournament_callers(AccountAccessLevel caller)
    {
        // PTR (32) and Tournament (16) are numerically above Admin (4): the world rule is a mask test.
        var page = await Sut().ListAsync(caller, 1, 50);

        Assert.Equal(new ushort[] { 101, 102, 103 }, page.Items.Select(w => w.Id).Order());
        Assert.Equal(3, page.TotalCount);
        Assert.Null(await Sut().GetAsync(104, caller));
    }

    [Fact]
    public async Task Answer_a_restricted_world_exactly_as_one_that_does_not_exist()
    {
        Assert.Null(await Sut().GetAsync(104, AccountAccessLevel.Tournament));
        Assert.Null(await Sut().GetAsync(999, AccountAccessLevel.Tournament));
    }

    [Fact]
    public async Task Flag_a_configured_admin_world_for_an_admin()
    {
        WorldDto? world = await Sut().GetAsync(104, AccountAccessLevel.Admin);

        Assert.True(world!.Configured);
        Assert.True(world.Available);
    }
}
