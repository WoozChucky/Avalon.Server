using Avalon.Server.World.UnitTests.GameAuth;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// Where a character leave (#663) sits in a connection's queue. It is taken by the session pass
/// alone, so in-map packets queued before it run first, and in-map packets queued behind it, meant
/// for the character that has left, are dropped rather than left to hold back the next character
/// list (see ProcessQueueWedgeShould for the wedge they would otherwise cause).
/// </summary>
public class CharacterLeaveQueueShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;
    private readonly TestConnection _connection;
    private readonly List<NetworkPacketType> _dispatched = [];

    public CharacterLeaveQueueShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _clientSide = new TcpClient();
        _clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();

        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [NetworkPacketType.CMSG_CHARACTER_LIST] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched),
            [NetworkPacketType.CMSG_PLAYER_INPUT] = new Recorder(NetworkPacketType.CMSG_PLAYER_INPUT, _dispatched),
            [NetworkPacketType.CMSG_CAST_ABILITY] = new Recorder(NetworkPacketType.CMSG_CAST_ABILITY, _dispatched),
            // What the real handler does to the queue's view of the connection: the character is gone.
            [NetworkPacketType.CMSG_CHARACTER_LEAVE] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LEAVE, _dispatched,
                connection => connection.Character = null),
        });

        _connection = new TestConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        GameplayTestAdmission.Admit(_connection);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    /// <summary>One tick's two passes, as WorldServer.Update and MapInstance.Update run them.</summary>
    private void Pump()
    {
        _connection.UpdateSession();
        if (_connection.Character is not null)
            _connection.UpdateMap();
    }

    private void Spawn() => _connection.Character = new CharacterEntity
    {
        Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 }
    };

    [Fact]
    public void Accept_a_leave_in_the_session_filter_whatever_the_connection_holds_and_never_in_the_map_filter()
    {
        var session = new WorldSessionFilter(_connection);
        var map = new MapSessionFilter(_connection);

        Assert.True(session.CanProcess(NetworkPacketType.CMSG_CHARACTER_LEAVE));
        Assert.False(map.CanProcess(NetworkPacketType.CMSG_CHARACTER_LEAVE));

        Spawn();

        Assert.True(session.CanProcess(NetworkPacketType.CMSG_CHARACTER_LEAVE));
        Assert.False(map.CanProcess(NetworkPacketType.CMSG_CHARACTER_LEAVE));
    }

    /// <summary>Acceptance case: movement and casts sent before the leave act on the old character; none after it act at all.</summary>
    [Fact]
    public void Run_in_map_packets_queued_before_a_leave_first_and_drop_those_queued_behind_it()
    {
        Spawn();
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LEAVE, new CCharacterLeavePacket());
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        _connection.Deliver(NetworkPacketType.CMSG_CAST_ABILITY, new CCastAbilityPacket());

        // The map pass takes the input and stops at the leave; the next session pass takes the leave.
        Pump();
        Assert.Equal([NetworkPacketType.CMSG_PLAYER_INPUT], _dispatched);
        Pump();
        Assert.Equal([NetworkPacketType.CMSG_PLAYER_INPUT, NetworkPacketType.CMSG_CHARACTER_LEAVE], _dispatched);
        Assert.Null(_connection.Character);

        // The character list the client sends once it has the answer is not held back.
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        Pump();

        Assert.Equal(
            [NetworkPacketType.CMSG_PLAYER_INPUT, NetworkPacketType.CMSG_CHARACTER_LEAVE, NetworkPacketType.CMSG_CHARACTER_LIST],
            _dispatched);
    }

    /// <summary>With a character still held, an in-map packet at the head waits for the map pass, as before.</summary>
    [Fact]
    public void Not_drop_an_in_map_packet_while_the_character_is_still_held()
    {
        Spawn();
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());

        _connection.UpdateSession();
        Assert.Empty(_dispatched);

        _connection.UpdateMap();
        Assert.Equal([NetworkPacketType.CMSG_PLAYER_INPUT], _dispatched);
    }

    private sealed class Recorder(NetworkPacketType type, List<NetworkPacketType> log, Action<IWorldConnection>? then = null)
        : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet)
        {
            log.Add(type);
            then?.Invoke(connection);
        }
    }

    /// <summary>Reaches the arrival path without a socket read behind it.</summary>
    private sealed class TestConnection(
        IWorldServer server, TcpClient client, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        IPacketReader reader) : Avalon.World.WorldConnection(server, client, loggerFactory, reader)
    {
        public void Deliver(NetworkPacketType type, Packet payload) =>
            OnReceive(new NetworkPacketHeader { Type = type }, payload).GetAwaiter().GetResult();
    }
}
