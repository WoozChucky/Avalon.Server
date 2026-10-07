using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Services;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.UnitTests.Services;

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
        using AuthDbContext context = _database.CreateDbContext();
        context.Worlds.RemoveRange(context.Worlds.ToList());
        context.Worlds.AddRange(
            World(101, AccountAccessLevel.Player), World(102, AccountAccessLevel.Player),
            World(103, AccountAccessLevel.Player), World(104, AccountAccessLevel.Admin));
        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private static WorldEntity World(ushort id, AccountAccessLevel required) => new()
    {
        Id = new WorldId(id),
        Name = $"World{id}",
        Host = "h",
        Port = 21000 + id,
        MinVersion = "0.0.1",
        Version = "0.0.1",
        AccessLevelRequired = required,
        UpdatedAt = DateTime.UtcNow,
    };

    private WorldService Sut() => new(new WorldRepository(_database), _databases,
        Substitute.For<IWorldReadiness>());

    [Fact]
    public async Task Metadata_update_preserves_a_concurrent_maintenance_transition()
    {
        var repository = new WorldRepository(_database);
        WorldEntity stale = (await repository.FindByIdAsync(new WorldId(101)))!;
        stale.Name = "Renamed";
        stale.UpdatedAt = DateTime.UtcNow;
        using (AuthDbContext context = _database.CreateDbContext())
        {
            WorldEntity persisted = context.Worlds.Single(w => w.Id == new WorldId(101));
            persisted.MaintenanceEnabled = true;
            persisted.MaintenanceRevision = 1;
            persisted.MaintenanceDeadlineUtc = DateTime.UtcNow.AddMinutes(5);
            context.SaveChanges();
        }

        await repository.UpdateMetadataAsync(stale, CancellationToken.None);

        using AuthDbContext check = _database.CreateDbContext();
        WorldEntity actual = check.Worlds.Single(w => w.Id == new WorldId(101));
        Assert.Equal("Renamed", actual.Name);
        Assert.True(actual.MaintenanceEnabled);
        Assert.Equal(1, actual.MaintenanceRevision);
    }

    [Fact]
    public async Task Flag_each_listed_world_as_configured_and_available_or_not()
    {
        PagedResult<WorldDto> page = await Sut().ListAsync(AccountAccessLevel.Player, 1, 50);

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
    public async Task Show_a_ready_scheduled_world_as_online_before_its_deadline()
    {
        using (AuthDbContext context = _database.CreateDbContext())
        {
            WorldEntity world = context.Worlds.Single(w => w.Id == new WorldId(101));
            world.MaintenanceEnabled = true;
            world.MaintenanceRevision = 1;
            world.MaintenanceDeadlineUtc = DateTime.UtcNow.AddMinutes(10);
            context.SaveChanges();
        }
        IWorldReadiness readiness = Substitute.For<IWorldReadiness>();
        readiness.IsReadyAsync(101, Arg.Any<CancellationToken>()).Returns(true);
        var service = new WorldService(new WorldRepository(_database), _databases, readiness);

        WorldDto? worldDto = await service.GetAsync(101, AccountAccessLevel.Player);

        Assert.Equal(Avalon.Api.Contract.WorldStatus.Online, worldDto!.Status);
    }

    [Fact]
    public async Task Flag_a_configured_admin_world_for_an_admin()
    {
        WorldDto? world = await Sut().GetAsync(104, AccountAccessLevel.Admin);

        Assert.True(world!.Configured);
        Assert.True(world.Available);
    }
}
