using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyInstanceRoutingShould : IDisposable
{
    private static readonly MapTemplateId s_dungeonId = new(2);
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);
    private readonly IChunkLayoutInstanceFactory _factory = Substitute.For<IChunkLayoutInstanceFactory>();
    private readonly List<TaskCompletionSource<MapInstance>> _builds = [];
    private readonly List<(MapTemplate Template, PartyId? Party)> _requested = [];
    private readonly List<MapInstance> _instances = [];
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanceRegistry _registry;

    public PartyInstanceRoutingShould()
    {
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = s_dungeonId, MapType = MapType.Normal }]);
        _factory.BuildAsync(default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            _requested.Add((call.ArgAt<MapTemplate>(0), call.ArgAt<PartyId?>(3)));
            var build = new TaskCompletionSource<MapInstance>(); // inline, so a finished build is queued at once
            _builds.Add(build);
            return build.Task;
        });
        _registry = new InstanceRegistry(NullLoggerFactory.Instance, mapManager, _factory);
    }

    [Fact]
    public async Task Build_one_instance_for_members_entering_at_once()
    {
        var party = new PartyId(9);
        Task<IMapInstance> first = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        Task<IMapInstance> second = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);

        Assert.Single(_builds);
        CompleteBuilds();
        IMapInstance[] got = await Task.WhenAll(first, second).WaitAsync(s_bound);

        Assert.Same(got[0], got[1]);
        Assert.Equal(party, ((MapInstance)got[0]).OwnerPartyId);
        Assert.Null(got[0].OwnerCharacterId);
        Assert.True(_registry.IsPartyInstance(party, got[0].InstanceId));
        Assert.False(_registry.IsPartyInstance(new PartyId(10), got[0].InstanceId));
    }

    /// <summary>
    /// #639: a finished party build is neither registered nor indexed until the tick publishes it, so a member asking
    /// meanwhile joins the same build, and once published the party is routed to it.
    /// </summary>
    [Fact]
    public async Task Index_the_party_instance_only_when_the_tick_publishes_it()
    {
        var party = new PartyId(9);
        Task<IMapInstance> first = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        CompleteBuilds(publish: false);

        Assert.False(first.IsCompleted);
        Assert.Empty(_registry.ActiveInstances);
        Assert.Same(first, _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId));

        _registry.PublishFinished();
        IMapInstance built = await first.WaitAsync(s_bound);

        Assert.True(_registry.IsPartyInstance(party, built.InstanceId));
        Assert.Same(built, await _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId).WaitAsync(s_bound));
        Assert.Single(_builds);
    }

    /// <summary>
    /// A disband while the party's build is in flight: the build must not index its instance once it finishes, and the
    /// instance, which nobody ever enters, must still expire rather than stay live for good.
    /// </summary>
    [Fact]
    public async Task Leave_a_build_finished_after_the_party_was_forgotten_unindexed_and_let_it_expire()
    {
        var party = new PartyId(9);
        Task<IMapInstance> first = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);

        _registry.ForgetParty(party); // the party disbands mid-build
        CompleteBuilds();
        IMapInstance orphan = await first.WaitAsync(s_bound);

        // Not indexed: asking again builds anew instead of handing back the orphan.
        Task<IMapInstance> again = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        Assert.Equal(2, _builds.Count);
        CompleteBuilds();
        Assert.NotSame(orphan, await again.WaitAsync(s_bound));

        _clock.Now += TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1);
        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
        Assert.Same(orphan, _registry.GetInstanceById(orphan.InstanceId));

        _clock.Now += TimeSpan.FromSeconds(1);
        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
        Assert.Null(_registry.GetInstanceById(orphan.InstanceId));
    }

    /// <summary>A newer build of the same party and map replaced the entry; freeing the older, expired one must keep it.</summary>
    [Fact]
    public async Task Keep_the_newer_party_instance_indexed_when_an_older_one_of_the_map_is_freed()
    {
        var party = new PartyId(9);
        Task<IMapInstance> first = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        CompleteBuilds();
        IMapInstance older = await first.WaitAsync(s_bound);

        _clock.Now += TimeSpan.FromMinutes(15); // older is expired but not yet freed
        Task<IMapInstance> second = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        Assert.Equal(2, _builds.Count);
        CompleteBuilds();
        IMapInstance newer = await second.WaitAsync(s_bound);

        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));

        Assert.Null(_registry.GetInstanceById(older.InstanceId));
        Task<IMapInstance> after = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        Assert.Equal(2, _builds.Count); // no third build: the entry still names the newer instance
        Assert.Same(newer, await after.WaitAsync(s_bound));
    }

    /// <summary>An instance stamped empty at creation must not look expired while someone is in it, however long.</summary>
    [Fact]
    public async Task Keep_occupied_party_and_solo_instances_past_the_expiry()
    {
        var party = new PartyId(9);
        Task<IMapInstance> partyBuild = _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId);
        Task<IMapInstance> soloBuild = _registry.GetOrCreateNormalInstanceAsync(1, s_dungeonId);
        CompleteBuilds();
        var partyInstance = (MapInstance)await partyBuild.WaitAsync(s_bound);
        var soloInstance = (MapInstance)await soloBuild.WaitAsync(s_bound);
        MapInstanceClients.Join(partyInstance, 1);
        MapInstanceClients.Join(soloInstance, 2);

        _clock.Now += TimeSpan.FromMinutes(20);
        _registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));

        Assert.Same(partyInstance, await _registry.GetOrCreatePartyInstanceAsync(party, s_dungeonId).WaitAsync(s_bound));
        Assert.Same(soloInstance, await _registry.GetOrCreateNormalInstanceAsync(1, s_dungeonId).WaitAsync(s_bound));
        Assert.Equal(2, _builds.Count);
    }

    public void Dispose()
    {
        foreach (MapInstance instance in _instances)
            instance.Dispose();
    }

    private void CompleteBuilds(bool publish = true)
    {
        for (int i = 0; i < _builds.Count; i++)
        {
            if (_builds[i].Task.IsCompleted)
                continue;

            MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: _clock,
                ownerPartyId: _requested[i].Party, templateId: _requested[i].Template.Id);
            _instances.Add(instance);
            _builds[i].SetResult(instance);
        }

        if (publish)
            _registry.PublishFinished(); // what the tick does first (#639)
    }
}

public class PartyMapEntryShould
{
    private const ushort SourceMap = 1;
    private const ushort Dungeon = 2;

    [Fact]
    public void Route_a_member_to_the_party_instance()
    {
        (EnterMapHandler handler, IWorld world, PartyTestWorld parties, PartyClient a, _) = Arrange(maxPlayers: null);

        handler.Execute(a.Connection, new CEnterMapPacket { TargetMapId = Dungeon });

        world.PartyInstances.Received(1).GetOrCreatePartyInstanceAsync(parties.Parties.PartyOf(a.Id)!.Id, new MapTemplateId(Dungeon));
        world.InstanceRegistry.DidNotReceiveWithAnyArgs().GetOrCreateNormalInstanceAsync(default, default!);
    }

    [Fact]
    public void Route_a_character_with_no_party_to_its_solo_instance()
    {
        (EnterMapHandler handler, IWorld world, PartyTestWorld parties, PartyClient a, _) = Arrange(maxPlayers: null);
        parties.Parties.Leave(a.Id); // disbands

        handler.Execute(a.Connection, new CEnterMapPacket { TargetMapId = Dungeon });

        world.InstanceRegistry.Received(1).GetOrCreateNormalInstanceAsync(a.Id, new MapTemplateId(Dungeon));
        world.PartyInstances.DidNotReceiveWithAnyArgs().GetOrCreatePartyInstanceAsync(default!, default!);
    }

    /// <summary>The capacity is min(MaxPartySize 6, MaxPlayers 2): a member gets in while fewer than 2 are inside.</summary>
    [Theory]
    [InlineData(1, MapTransitionResult.Success)]
    [InlineData(2, MapTransitionResult.InstanceFull)]
    public void Let_a_member_in_only_while_the_party_instance_has_room(int players, MapTransitionResult expected)
    {
        (EnterMapHandler handler, IWorld world, _, PartyClient a, Captured captured) = Arrange(maxPlayers: 2);
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.PlayerCount.Returns(players);

        handler.Execute(a.Connection, new CEnterMapPacket { TargetMapId = Dungeon });
        captured.Callback!(instance);

        world.Received(expected == MapTransitionResult.Success ? 1 : 0).TransferPlayer(a.Connection, instance);
        Assert.Equal(expected, captured.LastTransition(a).Result);
    }

    /// <summary>
    /// #707: the forest as seeded (map 2, read from the model's seed data) must take a party's second member, not
    /// only the one who built the instance. It was seeded with MaxPlayers 1, so the capacity came to min(6, 1).
    /// </summary>
    [Fact]
    public void Let_two_members_into_the_seeded_forest()
    {
        MapTemplate forest;
        using (var database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
        {
            forest = context.MapTemplates.AsNoTracking().ToList().Single(map => map.Id.Value == Dungeon);
        }

        (EnterMapHandler handler, IWorld world, _, PartyClient a, PartyClient b, Captured capturedA, Captured capturedB) =
            Arrange(forest);
        int players = 0;
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.PlayerCount.Returns(_ => players);
        world.When(w => w.TransferPlayer(Arg.Any<IWorldConnection>(), instance)).Do(_ => players++);

        handler.Execute(a.Connection, new CEnterMapPacket { TargetMapId = Dungeon });
        handler.Execute(b.Connection, new CEnterMapPacket { TargetMapId = Dungeon });
        capturedA.Callback!(instance); // A built it and goes in first
        capturedB.Callback!(instance);

        world.Received(1).TransferPlayer(a.Connection, instance);
        world.Received(1).TransferPlayer(b.Connection, instance);
        Assert.Equal(MapTransitionResult.Success, capturedA.LastTransition(a).Result);
        Assert.Equal(MapTransitionResult.Success, capturedB.LastTransition(b).Result);
    }

    [Fact]
    public void Refuse_entry_when_the_party_changed_while_the_instance_was_building()
    {
        (EnterMapHandler handler, IWorld world, PartyTestWorld parties, PartyClient a, Captured captured) = Arrange(maxPlayers: null);
        IMapInstance built = Substitute.For<IMapInstance>();

        handler.Execute(a.Connection, new CEnterMapPacket { TargetMapId = Dungeon });
        parties.Parties.Leave(a.Id); // the party is gone before the build finishes
        captured.Callback!(built);

        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.Equal(MapTransitionResult.MapNotFound, captured.LastTransition(a).Result);
    }

    /// <summary>
    /// A and B in a party, A standing on a portal to the dungeon in a real source instance, the handler over a
    /// substitute world whose registries record what they are asked for.
    /// </summary>
    private static (EnterMapHandler, IWorld, PartyTestWorld, PartyClient, Captured) Arrange(ushort? maxPlayers)
    {
        (EnterMapHandler handler, IWorld world, PartyTestWorld parties, PartyClient a, _, Captured captured, _) =
            Arrange(new MapTemplate
            {
                Id = new MapTemplateId(Dungeon),
                MapType = MapType.Normal,
                MaxPlayers = maxPlayers,
                Name = "dungeon",
                Description = "",
            });
        return (handler, world, parties, a, captured);
    }

    /// <summary>As above, entering <paramref name="dungeon" />, with B on the same portal and its continuation captured too.</summary>
    private static (EnterMapHandler, IWorld, PartyTestWorld, PartyClient, PartyClient, Captured, Captured) Arrange(
        MapTemplate dungeon)
    {
        var parties = new PartyTestWorld();
        MapInstance source = SourceWithPortalTo(Dungeon);

        PartyClient a = parties.Online(1, "Kaela", instance: source.InstanceId);
        PartyClient b = parties.Online(2, "Borin", instance: source.InstanceId);
        parties.Form(a, b);
        a.Character.Position = Vector3.zero;

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.PartyInstances.Returns(Substitute.For<IPartyInstanceRegistry>());
        world.Configuration.Returns(parties.Config);
        world.MapTemplates.Returns(new List<MapTemplate> { dungeon });

        b.Character.Position = Vector3.zero;
        Captured captured = Capture(a);
        Captured capturedB = Capture(b);

        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Substitute.For<ICharacterSaver>(),
            Substitute.For<IChunkLibrary>(), world, parties.Parties);
        return (handler, world, parties, a, b, captured, capturedB);
    }

    private static Captured Capture(PartyClient client)
    {
        var captured = new Captured();
        client.Connection.InGame.Returns(true);
        client.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<IMapInstance>>()))
            .Do(call => captured.Callback = call.Arg<Action<IMapInstance>>());
        return captured;
    }

    /// <summary>A real instance with a layout and a portal to <paramref name="targetMap" /> at the origin, so Execute reaches the continuation.</summary>
    private static MapInstance SourceWithPortalTo(ushort targetMap)
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var source = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(SourceMap),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        source.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, targetMap, 1));
        return source;
    }

    private sealed class Captured
    {
        public Action<IMapInstance>? Callback { get; set; }

        public SMapTransitionPacket LastTransition(PartyClient client) =>
            client.Read<SMapTransitionPacket>(NetworkPacketType.SMSG_MAP_TRANSITION).Last();
    }
}

/// <summary>The party side of a real, loaded World: the registry it builds is the one the party service forgets into.</summary>
public class PartyWorldWiringShould
{
    private static readonly MapTemplateId s_dungeonId = new(2);
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Forget_the_party_instances_in_the_world_registry_when_the_party_disbands()
    {
        var parties = new PartyTestWorld();
        var built = new List<MapInstance>();
        Avalon.World.World world = await RealWorldAsync(parties.Parties, built);
        PartyClient a = parties.Online(1, "Kaela");
        PartyClient b = parties.Online(2, "Borin");
        parties.Form(a, b);
        PartyId party = parties.Parties.PartyOf(a.Id)!.Id;

        IMapInstance first = await world.PartyInstances.GetOrCreatePartyInstanceAsync(party, s_dungeonId).Published(world).WaitAsync(s_bound);
        Assert.Same(first, await world.PartyInstances.GetOrCreatePartyInstanceAsync(party, s_dungeonId).Published(world).WaitAsync(s_bound));

        parties.Parties.Leave(b.Id); // two members: the party disbands

        IMapInstance after = await world.PartyInstances.GetOrCreatePartyInstanceAsync(party, s_dungeonId).Published(world).WaitAsync(s_bound);
        Assert.NotSame(first, after);
        Assert.Equal(2, built.Count);
        Assert.Empty(parties.Instances.Forgotten); // the world's registry, not the fake, is what the service forgets into

        foreach (MapInstance instance in built)
            instance.Dispose();
    }

    [Fact]
    public async Task Refresh_every_member_roster_when_a_member_changes_instance()
    {
        var parties = new PartyTestWorld();
        Avalon.World.World world = await RealWorldAsync(parties.Parties, []);
        PartyClient a = parties.Online(1, "Kaela");
        PartyClient b = parties.Online(2, "Borin");
        parties.Form(a, b);
        Assert.Empty(b.Rosters());

        IMapInstance elsewhere = Substitute.For<IMapInstance>();
        elsewhere.InstanceId.Returns(new Guid("20260930-0000-0000-0000-000000000006"));
        world.TransferPlayer(a.Connection, elsewhere);

        SPartyRosterPacket seenByB = b.Rosters().Last();
        Assert.False(seenByB.Members.Single(m => m.CharacterId == a.Id).SameInstance);
        Assert.True(seenByB.Members.Single(m => m.CharacterId == b.Id).SameInstance);
        SPartyRosterPacket seenByA = a.Rosters().Last();
        Assert.True(seenByA.Members.Single(m => m.CharacterId == a.Id).SameInstance);
        Assert.False(seenByA.Members.Single(m => m.CharacterId == b.Id).SameInstance);
    }

    /// <summary>A real World, loaded, with the party service, whose factory builds a party instance at once.</summary>
    private static async Task<Avalon.World.World> RealWorldAsync(PartyService parties, List<MapInstance> built)
    {
        IWorldRepository worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test",
                Host = "127.0.0.1",
                Port = 0,
                MinVersion = "0.0.1",
                Version = "1.0.0",
            });

        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), ownerPartyId: call.ArgAt<PartyId?>(3));
            built.Add(instance);
            return Task.FromResult(instance);
        });

        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory)).Returns(factory);

        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = s_dungeonId, MapType = MapType.Normal }]);

        TestStaticDataRepositories r = TestStaticData.Repositories();
        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
            serviceProvider,
            worldRepository,
            mapManager,
            Substitute.For<IServiceScopeFactory>(),
            r.CreateInfos, r.ClassStats, r.Items, r.Abilities, r.Levels, r.Creatures, r.BaseStats, r.Rarities,
            r.Texts,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            r.Dialogue, r.Loot, Avalon.Server.World.UnitTests.Chat.ChatLimits.Off(),
            parties: parties);

        await world.LoadAsync(CancellationToken.None);
        return world;
    }
}
