using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
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
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class InstanceRegistryShould : IDisposable
{
    private static readonly MapTemplateId TownId = new(1);
    private static readonly MapTemplateId DungeonId = new(2);
    private const uint CharacterId = 42;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly IChunkLayoutInstanceFactory _factory = Substitute.For<IChunkLayoutInstanceFactory>();
    private readonly List<TaskCompletionSource<MapInstance>> _builds = [];
    private readonly List<MapTemplate> _requested = [];
    private readonly List<MapInstance> _instances = [];
    private readonly InstanceRegistry _registry;

    public InstanceRegistryShould()
    {
        var mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = TownId, MapType = MapType.Town },
            new MapTemplate { Id = DungeonId, MapType = MapType.Normal },
        ]);

        // Each build gets its own completion source, so the test decides when each one finishes.
        // One shared task for every call would hand both callers the same instance and pass with
        // the bug present — the assertions below count builds and registered towns instead.
        _factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            _requested.Add(call.ArgAt<MapTemplate>(0));
            var build = new TaskCompletionSource<MapInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        CompleteBuilds();
        IMapInstance[] results = await Task.WhenAll(first, second).WaitAsync(Bound);

        Assert.Same(results[0], results[1]);
        Assert.Single(_registry.ActiveInstances, i => i.TemplateId == TownId && i.MapType == MapType.Town);
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Bound));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(Bound));

        Task<IMapInstance> retry = _registry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);

        Assert.Equal(2, _builds.Count);

        CompleteBuilds();
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
        CompleteBuilds();
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

        CompleteBuilds();
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Bound));

        Task<IMapInstance> retry = _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId);

        Assert.Equal(2, _builds.Count);

        CompleteBuilds();
        IMapInstance instance = await retry.WaitAsync(Bound);

        Assert.Same(instance, _registry.ActiveInstances.Single());
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

        CompleteBuilds();
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
        CompleteBuilds();
        IMapInstance instance = await first.WaitAsync(Bound);

        IMapInstance again = await _registry.GetOrCreateNormalInstanceAsync(CharacterId, DungeonId).WaitAsync(Bound);

        Assert.Same(instance, again);
        Assert.Single(_builds);
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

            MapInstance instance = BuildInstance(_requested[i]);
            _instances.Add(instance);
            build.SetResult(instance);
        }
    }

    /// <summary>Follows <c>MapInstanceDisposalShould.BuildInstance</c>, for the requested template.</summary>
    private static MapInstance BuildInstance(MapTemplate template)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

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
            ownerCharacterId: null,
            layout,
            Substitute.For<IMapNavigator>(),
            seed: 0,
            template.MapType);
    }

    // A MapInstance subscribes to static entity events in its constructor; an undisposed one
    // keeps reacting to every other test's entities for the rest of the run.
    public void Dispose()
    {
        foreach (MapInstance instance in _instances)
        {
            instance.Dispose();
        }
    }
}
