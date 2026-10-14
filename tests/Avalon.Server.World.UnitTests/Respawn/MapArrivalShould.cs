using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.Mathematics;
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
        // The seed describes each map by its name and enters each at x = z, which would hide a transposed field: here
        // each has a description of its own, and an entry spawn off the diagonal.
        _townTemplate = Described(_reference.TownTemplate, "Where every character enters the world");
        _forestTemplate = Described(_reference.ForestTemplate, "Beyond the town's portal");

        // The town as the World server builds it, and two forests off its own generator: real layouts, with their
        // rotations, set pieces and portals.
        _town = Instance(_reference.TownLayout with { EntrySpawnWorldPos = new Vector3(14f, 0.5f, 17f) }, _townTemplate.Id,
            MapType.Town);
        _forest = Instance(Forest(seed: 1) with { EntrySpawnWorldPos = new Vector3(11f, 0.25f, 19f) }, _forestTemplate.Id,
            MapType.Normal);
        _partyForest = Instance(Forest(seed: 2) with { EntrySpawnWorldPos = new Vector3(9f, 0.75f, 21f) }, _forestTemplate.Id,
            MapType.Normal, new PartyId(9));

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
    /// the packet that announces the map. Twice, by two characters: an instance is built with the packet's chunk and
    /// portal entries (#883), and every arrival must be sent the same bytes from them. A portal entry, a return to
    /// town and a teleport announce the map with a transition, sent from the same place as the layout: it names the
    /// instance, the map and its template's name and description, and puts the character at the layout's entry spawn.
    /// </summary>
    [Theory]
    [InlineData(Arrival.PortalIntoTheTown)]
    [InlineData(Arrival.PortalIntoAForest)]
    [InlineData(Arrival.PortalIntoAPartysForest)]
    [InlineData(Arrival.ReturnToTown)]
    [InlineData(Arrival.Teleport)]
    [InlineData(Arrival.Select)]
    public void Announce_the_map_then_send_its_layout_byte_for_byte_to_every_arrival(Arrival arrival)
    {
        MapInstance into = arrival switch
        {
            Arrival.PortalIntoAForest or Arrival.Teleport => _forest,
            Arrival.PortalIntoAPartysForest => _partyForest,
            _ => _town,
        };
        MapTemplate template = ReferenceEquals(into, _town) ? _townTemplate : _forestTemplate;
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
            Assert.Single(sent, p => p.Header.Type == NetworkPacketType.SMSG_CHUNK_LAYOUT);

            OutboundPacket announcement = sent[layout - 1];
            if (arrival == Arrival.Select)
            {
                Assert.Equal(NetworkPacketType.SMSG_CHARACTER_SELECTED, announcement.Header.Type);
                continue;
            }

            Assert.Equal(NetworkPacketType.SMSG_MAP_TRANSITION, announcement.Header.Type);
            SMapTransitionPacket transition = Assert.Single(TestTown.Read<SMapTransitionPacket>([announcement],
                NetworkPacketType.SMSG_MAP_TRANSITION));
            Assert.Equal(MapTransitionResult.Success, transition.Result);
            Assert.Equal(into.InstanceId, transition.InstanceId);
            Assert.Equal(template.Id.Value, transition.MapId);
            Assert.Equal(into.Layout.EntrySpawnWorldPos, new Vector3(transition.SpawnX, transition.SpawnY, transition.SpawnZ));
            Assert.Equal(template.Name, transition.MapName);
            Assert.Equal(template.Description, transition.MapDescription);
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
                new TownReturn(NullLogger.Instance, _world, resolver)
                    .Start(client.Connection, revive: false, dropEncounter: false);
                break;

            case Arrival.Teleport:
                Assert.True(new MapTeleport(NullLogger<MapTeleport>.Instance, _world, saver, _parties.Parties)
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

        new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, _world, _parties.Parties)
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
        // A portal entry's arrival, queued to run whatever becomes of its instance request.
        ((ISettledContinuations)client.Connection)
            .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
            .Do(call => call.Arg<Action<Task<IMapInstance>>>()(call.Arg<Task<IMapInstance>>()));
        client.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(call => call.Arg<Action>()());
        return client;
    }

    private static void RunInline<T>(IWorldConnection connection) =>
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<T>>(), Arg.Any<Action<T>>()))
            .Do(call => call.Arg<Action<T>>()(call.Arg<Task<T>>().Result));

    /// <summary>The seeded template as the arrival paths read it, under <paramref name="description" />.</summary>
    private static MapTemplate Described(MapTemplate seeded, string description) => new()
    {
        Id = seeded.Id,
        Name = seeded.Name,
        Description = description,
        MapType = seeded.MapType,
        MinLevel = seeded.MinLevel,
        MaxLevel = seeded.MaxLevel,
        MaxPlayers = seeded.MaxPlayers,
    };

    private ChunkLayout Forest(int seed)
    {
        ProceduralMapConfig config = _reference.ForestConfig;
        return new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(config,
            _reference.Chunks.GetByPool(config.ChunkPoolId), seed, _reference.Chunks.GetGroupsByPool(config.ChunkPoolId));
    }

    /// <summary>
    /// A real instance of <paramref name="layout" />, with the portals the layout places, built over the catalog's
    /// chunk library as the server builds one: an instance makes its layout's packet entries as it is constructed.
    /// </summary>
    private MapInstance Instance(ChunkLayout layout, MapTemplateId template, MapType mapType, PartyId? party = null)
    {
        IServiceProvider services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        services.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        services.GetService(typeof(IChunkLibrary)).Returns(_reference.Chunks);

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var instance = new MapInstance(NullLoggerFactory.Instance, services, world, template, ownerCharacterId: null, layout,
            Substitute.For<IMapNavigator>(), layout.Seed, mapType, ownerPartyId: party);
        new PortalPlacementService().Place(instance, layout, null);
        return instance;
    }
}
