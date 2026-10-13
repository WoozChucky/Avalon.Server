using Avalon.Combat;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
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
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Respawn;

/// <summary>
/// The chunk layout a client is sent on arrival is what it builds the map from and bakes its navmesh from, so a
/// packet that differs by a byte is a map that differs. Every arrival sends it: a portal entry, a return to town, an
/// item's teleport and a character select, into a town, a character's own forest or a party's.
/// </summary>
public class MapArrivalShould
{
    public enum Arrival
    {
        PortalIntoTheTown,
        PortalIntoAForest,
        PortalIntoAPartysForest,
        ReturnToTown,
        Teleport,
        Select,
    }

    private readonly ScenarioReferenceData _reference = ScenarioReferenceData.Shared;
    private readonly PartyTestWorld _parties = new();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly MapTemplate _townTemplate;
    private readonly MapTemplate _forestTemplate;
    private readonly MapInstance _town;
    private readonly MapInstance _forest;
    private readonly MapInstance _partyForest;

    public MapArrivalShould()
    {
        _townTemplate = _reference.TownTemplate;
        _forestTemplate = _reference.ForestTemplate;

        // The town as the World server builds it, and two forests off its own generator: real layouts, with their
        // rotations, set pieces and portals.
        _town = Instance(_reference.TownLayout, _townTemplate.Id, MapType.Town);
        _forest = Instance(Forest(seed: 1), _forestTemplate.Id, MapType.Normal);
        _partyForest = Instance(Forest(seed: 2), _forestTemplate.Id, MapType.Normal, new PartyId(9));

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        foreach (MapInstance instance in new[] { _town, _forest, _partyForest })
            registry.GetInstanceById(instance.InstanceId).Returns(instance);
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromResult<IMapInstance>(_town));
        registry.GetOrCreateNormalInstanceAsync(Arg.Any<uint>(), Arg.Any<MapTemplateId>())
            .Returns(Task.FromResult<IMapInstance>(_forest));
        IPartyInstanceRegistry partyInstances = Substitute.For<IPartyInstanceRegistry>();
        partyInstances.GetOrCreatePartyInstanceAsync(Arg.Any<PartyId>(), Arg.Any<MapTemplateId>())
            .Returns(Task.FromResult<IMapInstance>(_partyForest));

        _world.InstanceRegistry.Returns(registry);
        _world.PartyInstances.Returns(partyInstances);
        _world.Configuration.Returns(_parties.Config);
        _world.Data.Returns(_reference.Data);
        // As the map manager holds them: with their lookup by id (#883).
        _world.MapTemplates.Returns<IReadOnlyList<MapTemplate>>(MapTemplateRows.Keyed([_townTemplate, _forestTemplate]));
    }

    /// <summary>
    /// Each arrival is sent its instance's layout exactly as the layout and the chunk library's names encode, behind
    /// the packet that announces the map. Twice, by two characters: an instance keeps the packet's chunk and portal
    /// entries from its first arrival (#883), and the second must be sent the same bytes from them.
    /// </summary>
    [Theory]
    [InlineData(Arrival.PortalIntoTheTown)]
    [InlineData(Arrival.PortalIntoAForest)]
    [InlineData(Arrival.PortalIntoAPartysForest)]
    [InlineData(Arrival.ReturnToTown)]
    [InlineData(Arrival.Teleport)]
    [InlineData(Arrival.Select)]
    public void Send_every_arrival_the_layout_of_its_instance_byte_for_byte(Arrival arrival)
    {
        MapInstance into = arrival switch
        {
            Arrival.PortalIntoAForest or Arrival.Teleport => _forest,
            Arrival.PortalIntoAPartysForest => _partyForest,
            _ => _town,
        };
        byte[] expected = Encoded(into);

        PartyClient first = Online(1);
        PartyClient second = Online(2);
        if (arrival == Arrival.PortalIntoAPartysForest)
            _parties.Form(first, second);

        foreach (PartyClient client in new[] { first, second })
        {
            List<OutboundPacket> sent = Arrive(arrival, client);

            int layout = sent.FindIndex(p => p.Header.Type == NetworkPacketType.SMSG_CHUNK_LAYOUT);
            Assert.True(layout > 0, $"{arrival} sent no chunk layout behind another packet");
            Assert.Equal(expected, sent[layout].PayloadMemory.ToArray());
            Assert.Equal(
                arrival == Arrival.Select ? NetworkPacketType.SMSG_CHARACTER_SELECTED : NetworkPacketType.SMSG_MAP_TRANSITION,
                sent[layout - 1].Header.Type);
            Assert.Single(sent, p => p.Header.Type == NetworkPacketType.SMSG_CHUNK_LAYOUT);
        }
    }

    /// <summary>
    /// The layout's packet, written here from the layout itself: the seed, the instance, the map, the cell size, an
    /// entry per chunk with the name the chunk library gives it, the entry spawn and an entry per portal.
    /// </summary>
    private byte[] Encoded(MapInstance instance)
    {
        ChunkLayout layout = instance.Layout;
        Assert.True(layout.Chunks.Count > 1 && layout.Portals.Count > 0, "a layout with several chunks and a portal");

        var message = new SChunkLayoutPacket
        {
            Seed = layout.Seed,
            InstanceId = instance.InstanceId,
            MapId = instance.TemplateId.Value,
            CellSize = layout.CellSize,
            Chunks = layout.Chunks.Select(c => new PlacedChunkDto
            {
                ChunkTemplateId = c.TemplateId.Value,
                ChunkName = _reference.Chunks.GetById(c.TemplateId).Name,
                GridX = c.GridX,
                GridZ = c.GridZ,
                Rotation = c.Rotation,
            }).ToList(),
            EntrySpawn = Vector3Dto.From(layout.EntrySpawnWorldPos),
            Portals = layout.Portals.Select(p => new PortalPlacementDto
            {
                Role = (byte)p.Role,
                WorldPos = Vector3Dto.From(p.WorldPos),
                Radius = p.Radius,
                TargetMapId = p.TargetMapId,
            }).ToList(),
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return stream.ToArray();
    }

    /// <summary>Runs <paramref name="arrival" /> for <paramref name="client" /> to its end and answers what the client was sent.</summary>
    private List<OutboundPacket> Arrive(Arrival arrival, PartyClient client)
    {
        ICharacterSaver saver = Substitute.For<ICharacterSaver>();
        switch (arrival)
        {
            case Arrival.PortalIntoTheTown:
                EnterPortal(client, from: _forest, to: _townTemplate, saver);
                break;

            case Arrival.PortalIntoAForest or Arrival.PortalIntoAPartysForest:
                EnterPortal(client, from: _town, to: _forestTemplate, saver);
                break;

            case Arrival.ReturnToTown:
                IRespawnTargetResolver resolver = Substitute.For<IRespawnTargetResolver>();
                resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>()).Returns(_townTemplate.Id);
                new TownReturn(NullLogger.Instance, _world, resolver, _reference.Chunks)
                    .Start(client.Connection, revive: false, dropEncounter: false);
                break;

            case Arrival.Teleport:
                Assert.True(new MapTeleport(NullLogger<MapTeleport>.Instance, _world, _reference.Chunks, saver, _parties.Parties)
                    .Start(client.Connection, _forestTemplate.Id, position: null));
                break;

            case Arrival.Select:
                return Select(client.Id);
        }

        return client.Sent;
    }

    private void EnterPortal(PartyClient client, MapInstance from, MapTemplate to, ICharacterSaver saver)
    {
        client.Character.InstanceId = from.InstanceId;
        client.Character.Position = from.Portals.Single(p => p.TargetMapId == to.Id.Value).Position;

        new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, _reference.Chunks, _world, _parties.Parties)
            .Execute(client.Connection, new CEnterMapPacket { TargetMapId = to.Id.Value });
    }

    /// <summary>A select of character <paramref name="id" />, saved in the town, as far as the packets that announce its map.</summary>
    private List<OutboundPacket> Select(uint id)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(42L),
            Name = $"Tester{id}",
            Class = CharacterClass.Warrior,
            Level = 1,
            Map = _townTemplate.Id.Value,
            CreationDate = DateTime.UtcNow,
        };
        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindForGameplayAsync(Arg.Any<GameplayWriteAuthority>(), row.Id, Arg.Any<CancellationToken>()).Returns(row);

        var sent = new List<OutboundPacket>();
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => sent.Add(call.Arg<OutboundPacket>()));
        RunInline<(Character? Character, bool SaveStillRunning)>(connection);
        RunInline<IMapInstance>(connection);

        var handler = new CharacterSelectHandler(
            NullLogger<CharacterSelectHandler>.Instance,
            NullLoggerFactory.Instance,
            characters,
            Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(),
            Substitute.For<ICharacterAbilityRepository>(),
            _reference.Chunks,
            _world,
            Substitute.For<IRespawnTargetResolver>(),
            Options.Create(new RegenConfiguration()),
            Substitute.For<IAccountRepository>(),
            Substitute.For<ICharacterSaver>(),
            Substitute.For<IWorldServer>(),
            databaseWork: InlineDatabaseWork.Instance);
        handler.Execute(connection, new CCharacterSelectedPacket { CharacterId = id });
        return sent;
    }

    /// <summary>A character in the world behind a connection that records what it is sent and runs a continuation where it is queued.</summary>
    private PartyClient Online(uint id)
    {
        PartyClient client = _parties.Online(id, level: Math.Max((ushort)1, _forestTemplate.MinLevel ?? 1));
        client.Connection.InGame.Returns(true);
        RunInline<IMapInstance>(client.Connection);
        client.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(call => call.Arg<Action>()());
        return client;
    }

    private static void RunInline<T>(IWorldConnection connection) =>
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<T>>(), Arg.Any<Action<T>>()))
            .Do(call => call.Arg<Action<T>>()(call.Arg<Task<T>>().Result));

    private ChunkLayout Forest(int seed)
    {
        ProceduralMapConfig config = _reference.ForestConfig;
        return new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(config,
            _reference.Chunks.GetByPool(config.ChunkPoolId), seed, _reference.Chunks.GetGroupsByPool(config.ChunkPoolId));
    }

    /// <summary>A real instance of <paramref name="layout" />, with the portals the layout places.</summary>
    private static MapInstance Instance(ChunkLayout layout, MapTemplateId template, MapType mapType, PartyId? party = null)
    {
        IServiceProvider services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        services.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var instance = new MapInstance(NullLoggerFactory.Instance, services, world, template, ownerCharacterId: null, layout,
            Substitute.For<IMapNavigator>(), layout.Seed, mapType, ownerPartyId: party);
        new PortalPlacementService().Place(instance, layout, null);
        return instance;
    }
}
