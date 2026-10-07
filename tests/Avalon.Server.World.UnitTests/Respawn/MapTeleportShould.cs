using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Respawn;

/// <summary>
/// An item's teleport (item use): the instance resolved as a portal resolves it, then World.TransferPlayer and
/// the transition and layout the client already handles, on the tick.
/// </summary>
public class MapTeleportShould
{
    private static readonly MapTemplateId Town = new(1);
    private static readonly MapTemplateId Forest = new(2);

    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IInstanceRegistry _registry = Substitute.For<IInstanceRegistry>();
    private readonly IMapInstance _town = Substitute.For<IMapInstance>();
    private readonly IMapInstance _forest = Substitute.For<IMapInstance>();
    private readonly IMapInstance _source = Substitute.For<IMapInstance>();
    private readonly CharacterEntity _character = TestCharacters.New(7);
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly List<NetworkPacket> _sent = [];
    private readonly List<Action> _continuations = [];
    private readonly ICharacterSaver _saver = Substitute.For<ICharacterSaver>();
    private readonly MapTeleport _teleport;

    public MapTeleportShould()
    {
        _town.InstanceId.Returns(Guid.NewGuid());
        _forest.InstanceId.Returns(Guid.NewGuid());
        _character.InstanceId = Guid.NewGuid();
        _registry.GetInstanceById(_character.InstanceId).Returns(_source);
        _registry.GetOrCreateTownInstanceAsync(Town, Arg.Any<ushort>()).Returns(Task.FromResult(_town));
        _registry.GetOrCreateNormalInstanceAsync(7u, Forest).Returns(Task.FromResult(_forest));
        _world.InstanceRegistry.Returns(_registry);
        _world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = Town, MapType = MapType.Town, Name = "town", Description = "" },
            new()
            {
                Id = Forest, MapType = MapType.Normal, Name = "forest", Description = "", MinLevel = 1, MaxLevel = 10,
                DefaultSpawnX = 3, DefaultSpawnY = 0, DefaultSpawnZ = 4,
            },
        });
        TestTown.Record(_connection, _character, _sent);
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => _continuations.Add(ci.Arg<Action>()));
        _saver.Save(_connection, _character).Returns(Task.FromResult(true));
        _teleport = new MapTeleport(NullLogger<MapTeleport>.Instance, _world, Substitute.For<IChunkLibrary>(), _saver);
    }

    /// <summary>
    /// Gives the town that navigator. It is built before the town's stub, because NSubstitute cannot configure one
    /// substitute inside another's Returns.
    /// </summary>
    private void TownGround(NavmeshGroundKind kind, Vector3 ground)
    {
        IMapNavigator navigator = Ground(kind, ground);
        _town.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);
    }

    /// <summary>A navigator whose ground query answers <paramref name="kind" /> at <paramref name="ground" />.</summary>
    private static IMapNavigator Ground(NavmeshGroundKind kind, Vector3 ground)
    {
        IMapNavigator navigator = Substitute.For<IMapNavigator, IGroundNavigator>();
        ((IGroundNavigator)navigator).FindGround(Arg.Any<Vector3>(), out Arg.Any<Vector3>()).Returns(ci =>
        {
            ci[1] = ground;
            return kind;
        });
        return navigator;
    }

    private void RunContinuations()
    {
        foreach (Action continuation in _continuations.ToList())
            continuation();
    }

    [Fact]
    public void Move_the_character_to_a_town_at_the_position_given_and_tell_the_client()
    {
        Assert.True(_teleport.Start(_connection, Town, new Vector3(5, 0, 6)));
        Assert.True(_connection.RespawnInFlight);

        RunContinuations();

        _source.CombatService.Received(1).DropPlayerFromEncounter(_character);
        _world.Received(1).TransferPlayer(_connection, _town);
        Assert.Equal(new Vector3(5, 0, 6), _character.Position);
        SMapTransitionPacket transition = Assert.Single(TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION));
        Assert.Equal((MapTransitionResult.Success, (ushort)1), (transition.Result, transition.MapId));
        Assert.False(_connection.RespawnInFlight);
    }

    /// <summary>The position is snapped to the nearest walkable point the navmesh has in its 4 x 8 x 4 m box.</summary>
    [Fact]
    public void Snap_the_position_to_the_nearest_walkable_point()
    {
        TownGround(NavmeshGroundKind.Nearest, new Vector3(5.5f, 1f, 6f));

        Assert.True(_teleport.Start(_connection, Town, new Vector3(5, 9, 6)));
        RunContinuations();

        Assert.Equal(new Vector3(5.5f, 1f, 6f), _character.Position);
    }

    /// <summary>No mesh within the box refuses the arrival, and nothing moves or is saved.</summary>
    [Fact]
    public void Refuse_the_arrival_when_no_walkable_point_is_near_the_position()
    {
        TownGround(NavmeshGroundKind.None, default);

        Assert.True(_teleport.Start(_connection, Town, new Vector3(500, 0, 500)));
        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.Equal(MapTransitionResult.NoWalkableGround,
            Assert.Single(TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION)).Result);
        _saver.DidNotReceiveWithAnyArgs().Save(default(IWorldConnection)!, default!);
        Assert.False(_connection.RespawnInFlight);
    }

    /// <summary>A map with no navmesh keeps the position, as procedural spawns do.</summary>
    [Fact]
    public void Keep_the_position_on_a_map_with_no_navmesh()
    {
        TownGround(NavmeshGroundKind.NoNavMesh, new Vector3(9, 9, 9));

        Assert.True(_teleport.Start(_connection, Town, new Vector3(5, 3, 6)));
        RunContinuations();

        Assert.Equal(new Vector3(5, 3, 6), _character.Position);
    }

    /// <summary>Saved on arrival like a portal entry: map, instance and position on the row, then the save.</summary>
    [Fact]
    public void Save_the_character_on_arrival_like_a_portal_entry()
    {
        TownGround(NavmeshGroundKind.Under, new Vector3(5, 0, 6));

        Assert.True(_teleport.Start(_connection, Town, new Vector3(5, 0, 6)));
        RunContinuations();

        _saver.Received(1).Save(_connection, _character);
        Assert.Equal(((ushort)1, _town.InstanceId.ToString(), 5f, 0f, 6f),
            (_character.Data!.Map, _character.Data.InstanceId, _character.Data.X, _character.Data.Y, _character.Data.Z));
    }

    [Fact]
    public void Use_the_characters_own_instance_of_a_normal_map_and_its_spawn_when_no_position_is_given()
    {
        Assert.True(_teleport.Start(_connection, Forest, position: null));
        RunContinuations();

        _world.Received(1).TransferPlayer(_connection, _forest);
        Assert.Equal(new Vector3(3, 0, 4), _character.Position);
    }

    [Fact]
    public void Refuse_an_unknown_map_a_level_outside_its_band_and_a_move_under_way()
    {
        Assert.False(_teleport.Start(_connection, new MapTemplateId(99), null));

        _character.Level = 11;
        Assert.False(_teleport.Start(_connection, Forest, null));
        _character.Level = 1;

        _connection.RespawnInFlight = true;
        Assert.False(_teleport.Start(_connection, Town, null));

        Assert.Empty(_continuations);
        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
    }

    [Fact]
    public void Do_nothing_when_the_character_left_the_connection_before_the_instance_was_ready()
    {
        Assert.True(_teleport.Start(_connection, Town, null));
        _connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);

        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
    }

    [Fact]
    public void Log_and_clear_the_move_when_the_instance_cannot_be_built()
    {
        _registry.GetOrCreateTownInstanceAsync(Town, Arg.Any<ushort>())
            .Returns(Task.FromException<IMapInstance>(new InvalidOperationException("database down")));

        Assert.True(_teleport.Start(_connection, Town, null));
        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.False(_connection.RespawnInFlight);
    }

    /// <summary>A character that died while the instance built stays where it died; the item that started it stays used.</summary>
    [Fact]
    public void Not_move_a_character_that_died_before_the_instance_was_ready()
    {
        Assert.True(_teleport.Start(_connection, Town, new Vector3(5, 0, 6)));
        _character.IsDead = true;

        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        _saver.DidNotReceiveWithAnyArgs().Save(default(IWorldConnection)!, default!);
        Assert.Empty(TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION));
        Assert.False(_connection.RespawnInFlight);
    }

    /// <summary>
    /// A party of the test's character (7) and one other member, a MapTeleport that knows it, and the party's instance
    /// of the forest. The configuration is the defaults (MaxPartySize 6).
    /// </summary>
    private (PartyTestWorld Parties, MapTeleport Teleport, IPartyInstanceRegistry Instances) InParty()
    {
        var parties = new PartyTestWorld();
        PartyClient member = parties.Online(7);
        PartyClient other = parties.Online(8);
        parties.Form(member, other);

        // The party world's character and connection stand in for the test's own.
        _connection.Character.Returns(member.Character);
        _registry.GetInstanceById(member.Character.InstanceId).Returns(_source);
        _world.Configuration.Returns(new GameConfiguration());
        IPartyInstanceRegistry instances = Substitute.For<IPartyInstanceRegistry>();
        instances.GetOrCreatePartyInstanceAsync(Arg.Any<PartyId>(), Forest).Returns(Task.FromResult(_forest));
        _world.PartyInstances.Returns(instances);

        var teleport = new MapTeleport(NullLogger<MapTeleport>.Instance, _world, Substitute.For<IChunkLibrary>(), _saver,
            parties.Parties);
        return (parties, teleport, instances);
    }

    [Fact]
    public void Use_the_partys_instance_for_a_member()
    {
        (PartyTestWorld parties, MapTeleport teleport, IPartyInstanceRegistry instances) = InParty();

        Assert.True(teleport.Start(_connection, Forest, position: null));
        RunContinuations();

        instances.Received(1).GetOrCreatePartyInstanceAsync(parties.Parties.PartyOf(7)!.Id, Forest);
        _registry.DidNotReceiveWithAnyArgs().GetOrCreateNormalInstanceAsync(default, default!);
        _world.Received(1).TransferPlayer(_connection, _forest);
        Assert.False(_connection.RespawnInFlight);
    }

    [Fact]
    public void Refuse_a_full_party_instance()
    {
        (_, MapTeleport teleport, _) = InParty();
        _forest.PlayerCount.Returns(6);

        Assert.True(teleport.Start(_connection, Forest, position: null));
        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.Equal(MapTransitionResult.InstanceFull,
            Assert.Single(TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION)).Result);
        Assert.False(_connection.RespawnInFlight);
    }

    [Fact]
    public void Refuse_a_character_that_left_the_party_while_the_instance_built()
    {
        (PartyTestWorld parties, MapTeleport teleport, _) = InParty();

        Assert.True(teleport.Start(_connection, Forest, position: null));
        Assert.Equal(PartyResult.Ok, parties.Parties.Leave(7));
        RunContinuations();

        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.Equal(MapTransitionResult.MapNotFound,
            Assert.Single(TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION)).Result);
        Assert.False(_connection.RespawnInFlight);
    }
}
