using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #611: a character's replication state starts over when it changes instance. The client keeps the
/// objects it was told about until it is told they are gone, so the move tells it to drop every one it
/// knew in the instance left, itself included, before the map transition; the first tick in the new
/// instance then adds, in full, the character itself and everything it can see there, a character that
/// crossed just before it included. Every move between instances (entering a map, respawning at a
/// town) goes through World.TransferPlayer and so through MapInstance.AddCharacter, where the reset is.
/// Character ids (611_0xx) and creature ids (611_9xx) are unique to this class.
/// </summary>
public class InstanceTransferReplicationShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private const ushort TownMap = 1;
    private const ushort ForestMap = 2;

    [Fact]
    public async Task Tell_the_client_to_drop_the_old_instance_then_add_the_new_one_in_full_on_a_transfer()
    {
        Maps maps = await Maps.CreateAsync();
        MapInstanceClient traveller = Join(maps.Town, 611_001);
        MapInstanceClient friend = Join(maps.Town, 611_002);
        friend.Character.Position = new Vector3(5f, 0f, 0f);
        Creature townBoar = AddCreature(maps.Town, 611_901, new Vector3(10f, 0f, 0f));
        Creature forestBoar = AddCreature(maps.Forest, 611_902, new Vector3(10f, 0f, 0f));
        Ticks(maps.Town, 2);
        Assert.Contains(traveller.Added(), s => s.Guid == friend.Character.Guid.RawValue);
        traveller.Sent.Clear();

        // The friend crosses first, and the traveller follows before the town ticks again, so the
        // traveller's view still holds the friend when it arrives.
        maps.World.TransferPlayer(friend.Connection, maps.Forest);
        maps.World.TransferPlayer(traveller.Connection, maps.Forest);
        List<ulong> removedAtTransfer = traveller.Removed();
        Ticks(maps.Forest, 1);

        Assert.Equal(
            new[] { traveller.Character.Guid.RawValue, friend.Character.Guid.RawValue, townBoar.Guid.RawValue }.Order(),
            removedAtTransfer.Order());
        Assert.Equal(removedAtTransfer, traveller.Removed());   // and nothing more on the first tick

        ObjectState self = Assert.Single(traveller.Added(), s => s.Guid == traveller.Character.Guid.RawValue);
        AssertFullCharacterState(self);
        Assert.Null(self.Position);   // a client is never told its own position
        ObjectState crossed = Assert.Single(traveller.Added(), s => s.Guid == friend.Character.Guid.RawValue);
        AssertFullCharacterState(crossed);
        Assert.NotNull(crossed.Position);
        Assert.Single(traveller.Added(), s => s.Guid == forestBoar.Guid.RawValue);

        Ticks(maps.Forest, 30);
        Assert.Single(traveller.Added(), s => s.Guid == traveller.Character.Guid.RawValue);
        Assert.Equal(removedAtTransfer, traveller.Removed());
    }

    [Fact]
    public async Task Send_no_removes_on_a_transfer_when_the_client_knew_of_nothing_yet()
    {
        Maps maps = await Maps.CreateAsync();
        MapInstanceClient traveller = Join(maps.Town, 611_011);

        maps.World.TransferPlayer(traveller.Connection, maps.Forest);
        Ticks(maps.Forest, 1);

        Assert.Empty(traveller.Removed());
        Assert.Single(traveller.Added(), s => s.Guid == traveller.Character.Guid.RawValue);
    }

    /// <summary>A respawn at the town the character died in moves it into the instance it is already in.</summary>
    [Fact]
    public async Task Start_over_on_a_transfer_into_the_same_instance()
    {
        Maps maps = await Maps.CreateAsync();
        MapInstanceClient traveller = Join(maps.Town, 611_021);
        Creature townBoar = AddCreature(maps.Town, 611_921, new Vector3(10f, 0f, 0f));
        Ticks(maps.Town, 2);
        traveller.Sent.Clear();

        maps.World.TransferPlayer(traveller.Connection, maps.Town);
        Ticks(maps.Town, 1);

        Assert.Equal(new[] { traveller.Character.Guid.RawValue, townBoar.Guid.RawValue }.Order(),
            traveller.Removed().Order());
        Assert.Single(traveller.Added(), s => s.Guid == traveller.Character.Guid.RawValue);
        Assert.Single(traveller.Added(), s => s.Guid == townBoar.Guid.RawValue);
    }

    [Fact]
    public async Task Start_over_when_the_character_enters_a_map_through_a_portal()
    {
        Maps maps = await Maps.CreateAsync();
        MapInstanceClient traveller = Join(maps.Town, 611_031);
        traveller.Connection.InGame.Returns(true);
        RunContinuationsInline(traveller.Connection);
        Creature townBoar = AddCreature(maps.Town, 611_931, new Vector3(10f, 0f, 0f));
        Creature forestBoar = AddCreature(maps.Forest, 611_932, new Vector3(10f, 0f, 0f));
        Ticks(maps.Town, 2);
        traveller.Sent.Clear();
        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Substitute.For<ICharacterSaver>(),
            maps.Chunks, maps.World);

        handler.Execute(traveller.Connection, new CEnterMapPacket { TargetMapId = ForestMap });
        Ticks(maps.Forest, 1);

        Assert.Same(maps.Forest, maps.World.InstanceRegistry.GetInstanceById(traveller.Character.InstanceId));
        AssertStartedOver(traveller, [traveller.Character.Guid.RawValue, townBoar.Guid.RawValue], forestBoar);
    }

    [Fact]
    public async Task Start_over_when_the_character_respawns_at_a_town()
    {
        Maps maps = await Maps.CreateAsync();
        MapInstanceClient traveller = Join(maps.Forest, 611_041);
        RunContinuationsInline(traveller.Connection);
        Creature forestBoar = AddCreature(maps.Forest, 611_941, new Vector3(10f, 0f, 0f));
        Creature townBoar = AddCreature(maps.Town, 611_942, new Vector3(10f, 0f, 0f));
        Ticks(maps.Forest, 2);
        traveller.Character.IsDead = true;
        traveller.Sent.Clear();
        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(TownMap));
        var handler = new RespawnAtTownHandler(NullLogger<RespawnAtTownHandler>.Instance, maps.World, resolver,
            maps.Chunks);

        handler.Execute(traveller.Connection, new CRespawnAtTownPacket());
        Ticks(maps.Town, 1);

        Assert.Same(maps.Town, maps.World.InstanceRegistry.GetInstanceById(traveller.Character.InstanceId));
        AssertStartedOver(traveller, [traveller.Character.Guid.RawValue, forestBoar.Guid.RawValue], townBoar);
        Assert.False(traveller.Character.IsDead);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// The client was told to drop exactly <paramref name="dropped" />, before the map transition, and
    /// then given itself and <paramref name="seen" /> in full, once each.
    /// </summary>
    private static void AssertStartedOver(MapInstanceClient client, ulong[] dropped, Creature seen)
    {
        Assert.Equal(dropped.Order(), client.Removed().Order());

        List<NetworkPacketType> order = client.Sent.Select(p => p.Header.Type).ToList();
        int remove = order.IndexOf(NetworkPacketType.SMSG_WORLD_STATE_REMOVE);
        Assert.Equal(remove, order.LastIndexOf(NetworkPacketType.SMSG_WORLD_STATE_REMOVE));
        Assert.True(remove < order.IndexOf(NetworkPacketType.SMSG_MAP_TRANSITION));
        Assert.True(order.IndexOf(NetworkPacketType.SMSG_MAP_TRANSITION) < order.IndexOf(NetworkPacketType.SMSG_WORLD_STATE_ADD));

        AssertFullCharacterState(Assert.Single(client.Added(), s => s.Guid == client.Character.Guid.RawValue));
        Assert.Single(client.Added(), s => s.Guid == seen.Guid.RawValue);
    }

    /// <summary>A character's add carries the fields only a full state has, not only this tick's changes.</summary>
    private static void AssertFullCharacterState(ObjectState state)
    {
        Assert.NotNull(state.Name);
        Assert.NotNull(state.Level);
        Assert.NotNull(state.Health);
        Assert.NotNull(state.CurrentHealth);
        Assert.NotNull(state.MoveState);
    }

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(Tick);
        }
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
        };
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>The handlers' continuations run as soon as they are queued; every task here is already complete.</summary>
    private static void RunContinuationsInline(IWorldConnection connection)
    {
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<IMapInstance>>()))
            .Do(call => call.Arg<Action<IMapInstance>>()(call.Arg<Task<IMapInstance>>().GetAwaiter().GetResult()));
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<MapTemplateId>>(), Arg.Any<Action<MapTemplateId>>()))
            .Do(call => call.Arg<Action<MapTemplateId>>()(call.Arg<Task<MapTemplateId>>().GetAwaiter().GetResult()));
    }

    /// <summary>
    /// A real World whose registry builds exactly two real instances: the town (map 1), with a portal to
    /// the forest at the origin, and the forest (map 2).
    /// </summary>
    private sealed record Maps(Avalon.World.World World, MapInstance Town, MapInstance Forest, IChunkLibrary Chunks)
    {
        public static async Task<Maps> CreateAsync()
        {
            MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
            town.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, ForestMap, 1));
            MapInstance forest = TestMapInstances.Build(NewWorld());

            var templates = new List<MapTemplate>
            {
                new() { Id = new MapTemplateId(TownMap), MapType = MapType.Town, Name = "town", Description = "" },
                new() { Id = new MapTemplateId(ForestMap), MapType = MapType.Normal, Name = "forest", Description = "" },
            };
            var mapManager = Substitute.For<IAvalonMapManager>();
            mapManager.Templates.Returns(templates);

            var factory = Substitute.For<IChunkLayoutInstanceFactory>();
            factory.BuildAsync(Arg.Is<MapTemplate>(t => t.Id == new MapTemplateId(TownMap)), Arg.Any<uint?>(),
                Arg.Any<CancellationToken>()).Returns(town);
            factory.BuildAsync(Arg.Is<MapTemplate>(t => t.Id == new MapTemplateId(ForestMap)), Arg.Any<uint?>(),
                Arg.Any<CancellationToken>()).Returns(forest);

            var serviceProvider = Substitute.For<IServiceProvider>();
            serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory)).Returns(factory);

            var worldRepository = Substitute.For<IWorldRepository>();
            worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(new Avalon.Domain.Auth.World
                {
                    Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0",
                });

            var chunks = Substitute.For<IChunkLibrary>();
            chunks.GetById(Arg.Any<ChunkTemplateId>()).Returns(new ChunkTemplate { Name = "chunk" });

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
                chunks,
                r.Dialogue, r.Loot);
            await world.LoadAsync(CancellationToken.None);

            // Registered through the registry's own creation paths, so TransferPlayer finds them.
            Assert.Same(town, await world.InstanceRegistry.GetOrCreateTownInstanceAsync(new MapTemplateId(TownMap), 30));
            Assert.Same(forest, await world.InstanceRegistry.GetOrCreateNormalInstanceAsync(0, new MapTemplateId(ForestMap)));
            return new Maps(world, town, forest, chunks);
        }
    }
}
