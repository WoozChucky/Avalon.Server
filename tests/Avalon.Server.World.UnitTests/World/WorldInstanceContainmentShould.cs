using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abstractions;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #639: through <c>World.Update</c>, an instance whose update throws no longer ends the world update. It used
/// to throw out of it, so every instance after it went unticked, and the tick loop's flushes after the world
/// update were skipped. Here a town and a dungeon are live, and whichever the registry ticks first throws.
/// </summary>
public class WorldInstanceContainmentShould
{
    private static readonly MapTemplateId TownId = new(1);
    private static readonly MapTemplateId DungeonId = new(2);

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

        var mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = TownId, MapType = MapType.Town },
            new MapTemplate { Id = DungeonId, MapType = MapType.Normal },
        ]);
        var factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town, dungeon);
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new SilentReloader(), intervalSeconds: 60, mapManager, factory);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        await world.InstanceRegistry.GetOrCreateNormalInstanceAsync(639_102, DungeonId);
        Assert.Equal(2, world.InstanceRegistry.ActiveInstances.Count);

        Exception? thrown = Record.Exception(() => world.Update(TimeSpan.FromSeconds(1d / 60d)));

        Assert.Null(thrown);
        broken.Received(1).UpdateMap();
        healthy.Received(1).UpdateMap();
    }
}
