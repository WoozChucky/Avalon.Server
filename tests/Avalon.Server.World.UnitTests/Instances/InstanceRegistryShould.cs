using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

public class InstanceRegistryShould : IDisposable
{
    private static readonly MapTemplateId TownId = new(1);
    private static readonly MapTemplateId DungeonId = new(2);
    private static readonly MapTemplateId CaveId = new(3);
    private const uint CharacterId = 42;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly IChunkLayoutInstanceFactory _factory = Substitute.For<IChunkLayoutInstanceFactory>();
    private readonly List<TaskCompletionSource<MapInstance>> _builds = [];
    private readonly List<(MapTemplate Template, uint? Owner)> _requested = [];
    private readonly List<MapInstance> _instances = [];
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanceRegistry _registry;

    public InstanceRegistryShould()
    {
        var mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = TownId, MapType = MapType.Town },
            new MapTemplate { Id = DungeonId, MapType = MapType.Normal },
            new MapTemplate { Id = CaveId, MapType = MapType.Normal },
        ]);

        // Each build gets its own completion source, so the test decides when each one finishes.
        // One shared task for every call would hand both callers the same instance and pass with
        // the bug present — the assertions below count builds and registered towns instead. Completed
        // inline, so a build finished by the test is queued for publication before CompleteBuilds returns.
        _factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            _requested.Add((call.ArgAt<MapTemplate>(0), call.ArgAt<uint?>(1)));
            var build = new TaskCompletionSource<MapInstance>();
            _builds.Add(build);
            return build.Task;
        });

        _registry = new InstanceRegistry(NullLoggerFactory.Instance, mapManager, _factory);
    }

    /// <summary>
    /// Issue #442. The first two players to log in after a restart both find no town, and the
    /// second one's check runs while the first one's build is still baking a navmesh — so it
    /// starts a build of its own, and the two players end up in two copies of a town that is
    /// meant to be one shared place.
    /// </summary>
    [Fact]
    public async Task Start_One_Town_Build_For_Callers_Arriving_While_It_Is_In_Flight()
    {
        Task<IMapInstance> first = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        Task<IMapInstance> second = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);

        Assert.Single(_builds);

        CompleteBuildsAndPublish();
        IMapInstance[] results = await Task.WhenAll(first, second).WaitAsync(Bound);

        Assert.Same(results[0], results[1]);
        Assert.Single(_registry.ActiveInstances, i => i.TemplateId == TownId && i.MapType == MapType.Town);
    }

    /// <summary>
    /// #639: a build that has finished is not in the registry, and its requesters are not answered, until the tick
    /// publishes it. Before, the build's own continuation registered the instance and wrote the character index from
    /// the thread pool while the tick read and pruned the same index.
    /// </summary>
    [Fact]
    public async Task Leave_A_Finished_Build_Out_Of_The_Registry_Until_It_Is_Published()
    {
        Task<IMapInstance> town = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        Task<IMapInstance> dungeon = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);

        CompleteBuilds();

        Assert.Empty(_registry.ActiveInstances);
        Assert.False(town.IsCompleted);
        Assert.False(dungeon.IsCompleted);
        // Still in flight as far as requesters can tell: a second request joins the build rather than starting one.
        Assert.Same(dungeon, _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId));
        Assert.Equal(2, _builds.Count);

        IReadOnlyList<MapInstance> published = _registry.PublishFinished();

        Assert.Equal(2, published.Count);
        Assert.True(town.IsCompletedSuccessfully);
        Assert.True(dungeon.IsCompletedSuccessfully);
        Assert.Equal(2, _registry.ActiveInstances.Count);
        Assert.Same(await dungeon.WaitAsync(Bound),
            await _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId).WaitAsync(Bound));
        Assert.Equal(2, _builds.Count);
    }

    /// <summary>A tick with nothing finished publishes nothing, and allocates no list for it.</summary>
    [Fact]
    public void Publish_Nothing_While_No_Build_Has_Finished()
    {
        _ = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);

        Assert.Same(Array.Empty<MapInstance>(), _registry.PublishFinished());
        Assert.Empty(_registry.ActiveInstances);
    }

    /// <summary>
    /// Sharing a build must not mean sharing its failure forever. If a failed build stayed cached,
    /// every later arrival would get the same exception and the town would be unenterable until a
    /// restart.
    /// </summary>
    [Fact]
    public async Task Let_The_Next_Caller_Start_A_Fresh_Build_After_One_Fails()
    {
        Task<IMapInstance> first = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        Task<IMapInstance> second = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);

        _builds[0].SetException(new InvalidOperationException("navmesh bake failed"));
        Assert.False(first.IsCompleted); // the failure, too, reaches requesters only through the tick
        _registry.PublishFinished();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Bound));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(Bound));

        Task<IMapInstance> retry = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);

        Assert.Equal(2, _builds.Count);

        CompleteBuildsAndPublish();
        IMapInstance town = await retry.WaitAsync(Bound);

        Assert.Single(_registry.ActiveInstances, i => i.TemplateId == TownId && i.MapType == MapType.Town);
        Assert.Same(town, _registry.ActiveInstances.Single());
    }

    /// <summary>
    /// Once the build has finished and registered its town, arrivals find the town itself, not a
    /// leftover in-flight entry.
    /// </summary>
    [Fact]
    public async Task Reuse_The_Registered_Town_Once_Its_Build_Has_Finished()
    {
        Task<IMapInstance> first = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        CompleteBuildsAndPublish();
        IMapInstance town = await first.WaitAsync(Bound);

        IMapInstance later = await _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100).WaitAsync(Bound);

        Assert.Same(town, later);
        Assert.Single(_builds);
    }

    /// <summary>
    /// The same gap on the per-character path: a portal request sent twice, before the first
    /// build has finished, bakes two navmeshes and leaves the first instance orphaned until it
    /// expires.
    /// </summary>
    [Fact]
    public async Task Start_One_Normal_Build_For_A_Character_Entering_Twice_While_It_Is_In_Flight()
    {
        Task<IMapInstance> first = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Task<IMapInstance> second = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);

        Assert.Single(_builds);

        CompleteBuildsAndPublish();
        IMapInstance[] results = await Task.WhenAll(first, second).WaitAsync(Bound);

        Assert.Same(results[0], results[1]);
        Assert.Single(_registry.ActiveInstances);
    }

    /// <summary>
    /// A failed per-character build must not stay cached either, or that character could never
    /// enter the map again until a restart.
    /// </summary>
    [Fact]
    public async Task Let_A_Character_Start_A_Fresh_Normal_Build_After_One_Fails()
    {
        Task<IMapInstance> first = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);

        _builds[0].SetException(new InvalidOperationException("navmesh bake failed"));
        _registry.PublishFinished();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Bound));

        Task<IMapInstance> retry = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);

        Assert.Equal(2, _builds.Count);

        CompleteBuildsAndPublish();
        IMapInstance instance = await retry.WaitAsync(Bound);

        Assert.Same(instance, _registry.ActiveInstances.Single());
    }

    /// <summary>
    /// A publish step that throws (here a build that handed back no instance) loses nothing: its requesters get the
    /// failure, the build behind it is still published, and the next request starts afresh.
    /// </summary>
    [Fact]
    public async Task Answer_the_requesters_of_a_build_whose_publish_throws_and_publish_the_rest()
    {
        Task<IMapInstance> broken = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Task<IMapInstance> town = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        _builds[0].SetResult(null!);
        CompleteBuilds();

        IReadOnlyList<MapInstance> published = _registry.PublishFinished();

        await Assert.ThrowsAnyAsync<Exception>(() => broken.WaitAsync(Bound));
        Assert.Same(Assert.Single(published), await town.WaitAsync(Bound));
        _ = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Assert.Equal(3, _builds.Count);
    }

    /// <summary>A build whose map is unknown fails the same way, through the tick, and is not cached.</summary>
    [Fact]
    public async Task Fail_A_Build_Of_An_Unknown_Map_Through_The_Tick()
    {
        Task<IMapInstance> unknown = _registry.GetOrCreateNormalInstanceAsync(CharacterId, new MapTemplateId(99));

        Assert.False(unknown.IsCompleted);
        _registry.PublishFinished();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.WaitAsync(Bound));
        Assert.NotSame(unknown, _registry.GetOrCreateNormalInstanceAsync(CharacterId, new MapTemplateId(99)));
        Assert.Empty(_builds);
    }

    /// <summary>
    /// Sharing is per character: two different characters entering the same map at once each get
    /// their own instance, as they always have.
    /// </summary>
    [Fact]
    public async Task Build_Separate_Normal_Instances_For_Different_Characters()
    {
        Task<IMapInstance> mine = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Task<IMapInstance> theirs = _registry.GetOrCreateNormalInstanceAsync(CharacterId + 1, DungeonId);

        Assert.Equal(2, _builds.Count);

        CompleteBuildsAndPublish();
        IMapInstance[] results = await Task.WhenAll(mine, theirs).WaitAsync(Bound);

        Assert.NotSame(results[0], results[1]);
    }

    /// <summary>
    /// Re-entry within the window still returns the character's instance once its build has
    /// finished, rather than a stale pending entry or a fresh build.
    /// </summary>
    [Fact]
    public async Task Return_A_Characters_Finished_Normal_Instance_On_Re_Entry()
    {
        Task<IMapInstance> first = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        CompleteBuildsAndPublish();
        IMapInstance instance = await first.WaitAsync(Bound);

        IMapInstance again = await _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId).WaitAsync(Bound);

        Assert.Same(instance, again);
        Assert.Single(_builds);
    }

    /// <summary>
    /// #639, the race the analysis found: one of a character's instances expires while a build of another of its maps
    /// finishes. Both now change the character's index on the tick, one after the other: the expiry frees only the
    /// expired map's entry, and the publish adds the new one.
    /// </summary>
    [Fact]
    public async Task Expire_One_Map_And_Publish_Another_For_The_Same_Character_In_Tick_Order()
    {
        Task<IMapInstance> dungeonBuild = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        CompleteBuildsAndPublish();
        IMapInstance dungeon = await dungeonBuild.WaitAsync(Bound);

        _clock.Now += TimeSpan.FromMinutes(15);
        Task<IMapInstance> caveBuild = _registry.GetOrCreateNormalInstanceAsync(CharacterId, CaveId);
        CompleteBuilds(); // the cave's build ends off the tick, queued until the next publish

        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
        Assert.Null(_registry.GetInstanceById(dungeon.InstanceId));

        _registry.PublishFinished();
        IMapInstance cave = await caveBuild.WaitAsync(Bound);

        Assert.Same(cave, await _registry.GetOrCreateNormalInstanceAsync(CharacterId, CaveId).WaitAsync(Bound));
        Assert.Equal(2, _builds.Count);
        _ = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Assert.Equal(3, _builds.Count); // the expired dungeon's entry is gone, so a fresh one is built
    }

    /// <summary>
    /// A newer build of the same character and map replaced the entry; freeing the older, expired one must keep it,
    /// as for a party's instances.
    /// </summary>
    [Fact]
    public async Task Keep_The_Newer_Normal_Instance_Indexed_When_An_Older_One_Of_The_Map_Is_Freed()
    {
        Task<IMapInstance> first = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        CompleteBuildsAndPublish();
        IMapInstance older = await first.WaitAsync(Bound);

        _clock.Now += TimeSpan.FromMinutes(15); // older is expired but not yet freed
        Task<IMapInstance> second = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);
        Assert.Equal(2, _builds.Count);
        CompleteBuildsAndPublish();
        IMapInstance newer = await second.WaitAsync(Bound);

        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));

        Assert.Null(_registry.GetInstanceById(older.InstanceId));
        Assert.Same(newer, await _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId).WaitAsync(Bound));
        Assert.Equal(2, _builds.Count);
    }

    private void CompleteBuildsAndPublish()
    {
        CompleteBuilds();
        _registry.PublishFinished();
    }

    private void CompleteBuilds()
    {
        for (int i = 0; i < _builds.Count; i++)
        {
            TaskCompletionSource<MapInstance> build = _builds[i];
            if (build.Task.IsCompleted)
            {
                continue;
            }

            MapInstance instance = BuildInstance(_requested[i].Template, _requested[i].Owner, _clock);
            _instances.Add(instance);
            build.SetResult(instance);
        }
    }

    /// <summary>Follows <c>MapInstanceDisposalShould.BuildInstance</c>, for the requested template and owner.</summary>
    private static MapInstance BuildInstance(MapTemplate template, uint? owner, TimeProvider clock)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(TimeProvider)).Returns(clock);

        var world = Substitute.For<Avalon.World.IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(
            Seed: 0,
            Chunks: new[] { entryChunk },
            EntryChunk: entryChunk,
            BossChunk: null,
            Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);

        return new MapInstance(
            NullLoggerFactory.Instance,
            serviceProvider,
            world,
            template.Id,
            ownerCharacterId: owner,
            layout,
            Substitute.For<IMapNavigator>(),
            seed: 0,
            template.MapType);
    }

    // Disposed as the registry disposes the instances it drops.
    public void Dispose()
    {
        foreach (MapInstance instance in _instances)
        {
            instance.Dispose();
        }
    }
}
