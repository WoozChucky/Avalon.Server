using Avalon.Api.Worlds.Services;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using NSubstitute;
using Xunit;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.UnitTests.Services;

/// <summary>The one read of a world's status and occupancy, shared by the admin list and the public endpoint.</summary>
public sealed class WorldLiveStateShould
{
    private static readonly DateTime s_nowUtc = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static WorldEntity World(int? limit = null, DateTime? maintenanceDeadline = null) => new()
    {
        Id = new WorldId(101),
        Name = "World101",
        Host = "h",
        Port = 21101,
        MinVersion = "0.0.1",
        Version = "0.0.1",
        PlayerLimit = limit,
        MaintenanceEnabled = maintenanceDeadline is not null,
        MaintenanceRevision = maintenanceDeadline is not null ? 1 : 0,
        MaintenanceDeadlineUtc = maintenanceDeadline,
        UpdatedAt = s_nowUtc,
    };

    private static Task<WorldLiveState> Read(WorldEntity world, bool ready, int taken = 0, int queue = 0)
    {
        IWorldReadiness readiness = Substitute.For<IWorldReadiness>();
        readiness.IsReadyAsync(101, Arg.Any<CancellationToken>()).Returns(ready);
        IWorldOccupancy occupancy = Substitute.For<IWorldOccupancy>();
        occupancy.ReadAsync(101, Arg.Any<CancellationToken>()).Returns(new WorldOccupancy(taken, queue));
        return WorldLiveState.ReadAsync(readiness, occupancy, world, s_nowUtc, CancellationToken.None);
    }

    [Fact]
    public async Task Be_online_when_ready_and_below_the_limit()
    {
        WorldLiveState state = await Read(World(limit: 10), ready: true, taken: 9);

        Assert.Equal(WorldStatus.Online, state.Status);
        Assert.Equal(9, state.Occupancy.PlayerCount);
    }

    [Fact]
    public async Task Be_full_when_places_taken_reach_the_limit()
    {
        WorldLiveState state = await Read(World(limit: 10), ready: true, taken: 10, queue: 4);

        Assert.Equal(WorldStatus.Full, state.Status);
        Assert.Equal(4, state.Occupancy.QueueLength);
    }

    [Fact]
    public async Task Be_maintenance_once_the_cutoff_passes()
    {
        WorldLiveState state = await Read(World(maintenanceDeadline: s_nowUtc.AddMinutes(-1)), ready: true);

        Assert.Equal(WorldStatus.Maintenance, state.Status);
    }

    [Fact]
    public async Task Stay_online_before_the_cutoff()
    {
        WorldLiveState state = await Read(World(maintenanceDeadline: s_nowUtc.AddMinutes(12)), ready: true);

        Assert.Equal(WorldStatus.Online, state.Status);
    }

    [Fact]
    public async Task Be_offline_when_not_ready()
    {
        WorldLiveState state = await Read(World(), ready: false);

        Assert.Equal(WorldStatus.Offline, state.Status);
        Assert.False(state.Ready);
    }
}
