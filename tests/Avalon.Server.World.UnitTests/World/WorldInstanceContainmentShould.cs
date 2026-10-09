using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Threading;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #639: through <c>World.Update</c>, an instance whose update throws no longer ends the world update. It used
/// to throw out of it, so every instance after it went unticked, and the tick loop's flushes after the world
/// update were skipped. Here a town and a dungeon are live, and whichever the registry ticks first throws.
/// Also which instances it ticks: the registry's tick snapshot (#851).
/// </summary>
public class WorldInstanceContainmentShould
{
    private static readonly MapTemplateId s_townId = new(1);
    private static readonly MapTemplateId s_dungeonId = new(2);
    private static readonly MapTemplateId s_caveId = new(3);
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private sealed class SilentReloader : IScriptHotReloader
    {
        public void Update(out List<Type> scriptTypes) => scriptTypes = [];

        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tick_the_other_instances_and_return_when_one_throws(bool townThrows)
    {
        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        using MapInstance dungeon = TestMapInstances.Build(NewWorld());
        MapInstanceClient inTown = Join(town, 639_101);
        MapInstanceClient inDungeon = Join(dungeon, 639_102);
        IWorldConnection broken = townThrows ? inTown.Connection : inDungeon.Connection;
        IWorldConnection healthy = townThrows ? inDungeon.Connection : inTown.Connection;
        broken.When(c => c.UpdateMap()).Do(_ => throw new InvalidOperationException("broken instance"));

        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = s_townId, MapType = MapType.Town },
            new MapTemplate { Id = s_dungeonId, MapType = MapType.Normal },
        ]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town, dungeon);
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new SilentReloader(), intervalSeconds: 60, mapManager, factory);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(s_townId, maxPlayers: 100).Published(world);
        await world.InstanceRegistry.GetOrCreateNormalInstanceAsync(639_102, s_dungeonId).Published(world);
        Assert.Equal(2, world.InstanceRegistry.ActiveInstances.Count);

        Exception? thrown = Record.Exception(() => world.Update(TimeSpan.FromSeconds(1d / 60d)));

        Assert.Null(thrown);
        broken.Received(1).UpdateMap();
        healthy.Received(1).UpdateMap();
    }

    /// <summary>
    /// #851: the instances are ticked from the registry's tick snapshot, an array rebuilt only when the set changes, not
    /// from a fresh copy of the list each tick. One published is ticked from the next tick; one removed while the
    /// instances tick, from inside a later one's update, cuts that tick short for no one (a walk of the live list would
    /// shift the rest down a place and skip the next instance), and is not ticked again.
    /// </summary>
    [Fact]
    public async Task Tick_each_published_instance_and_stop_ticking_one_removed_mid_tick()
    {
        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        using MapInstance dungeon = TestMapInstances.Build(NewWorld());
        using MapInstance cave = TestMapInstances.Build(NewWorld());
        IWorldConnection inTown = Join(town, 851_101).Connection;
        IWorldConnection inDungeon = Join(dungeon, 851_102).Connection;
        IWorldConnection inCave = Join(cave, 851_103).Connection;

        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = s_townId, MapType = MapType.Town },
            new MapTemplate { Id = s_dungeonId, MapType = MapType.Normal },
            new MapTemplate { Id = s_caveId, MapType = MapType.Normal },
        ]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town, dungeon, cave);
        var tickThread = new TickThreadGuard();
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new SilentReloader(), intervalSeconds: 60, mapManager, factory, tickThread: tickThread);
        tickThread.Bind(); // this thread is the tick from here on, so every registry step below is checked

        _ = world.InstanceRegistry.GetOrCreateTownInstanceAsync(s_townId, maxPlayers: 100);
        _ = world.InstanceRegistry.GetOrCreateNormalInstanceAsync(851_102, s_dungeonId);
        world.Update(s_tick);

        inTown.Received(1).UpdateMap();
        inDungeon.Received(1).UpdateMap();

        // The cave is published at the top of the next tick, after the dungeon in the walk (town, dungeon, cave); the
        // dungeon removes the town, ahead of it, from inside its own update.
        _ = world.InstanceRegistry.GetOrCreateNormalInstanceAsync(851_103, s_caveId);
        inDungeon.When(c => c.UpdateMap()).Do(_ => world.InstanceRegistry.RemoveInstance(town.InstanceId));
        world.Update(s_tick);

        inDungeon.Received(2).UpdateMap();
        inCave.Received(1).UpdateMap();
        Assert.Null(world.InstanceRegistry.GetInstanceById(town.InstanceId));

        inTown.ClearReceivedCalls();
        world.Update(s_tick);

        inTown.DidNotReceive().UpdateMap();
        inDungeon.Received(3).UpdateMap();
        inCave.Received(2).UpdateMap();
    }
}
