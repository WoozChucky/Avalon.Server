using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// The premise the load report's filter placement rests on, checked rather than asserted in a
/// comment: OnReceive queues a packet if a filter accepts it AT ARRIVAL, and ProcessQueue peeks at
/// dispatch. A packet whose acceptance changed in between is not dropped -- it stays at the head of
/// the queue and nothing behind it is ever dispatched.
/// </summary>
public class ProcessQueueWedgeShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;
    private readonly TestConnection _connection;
    private readonly List<NetworkPacketType> _dispatched = [];

    public ProcessQueueWedgeShould()
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
            [NetworkPacketType.CMSG_CHARACTER_LOADED] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LOADED, _dispatched),
            [NetworkPacketType.CMSG_PLAYER_INPUT] = new Recorder(NetworkPacketType.CMSG_PLAYER_INPUT, _dispatched),
        });

        _connection = new TestConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    private void Pump()
    {
        _connection.UpdateSession();
        _connection.UpdateMap();
    }

    // CharacterEntity.Map reads through Data, so a row is what puts the character in a map.
    private void Spawn() => _connection.Character = new CharacterEntity
    {
        Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 }
    };

    /// <summary>
    /// CMSG_CHARACTER_LIST is accepted only while no character exists. Queued before a spawn and
    /// dispatched after one, it is refused by the session filter and absent from the map filter --
    /// and the packet behind it never runs.
    /// </summary>
    [Fact]
    public void Wedge_the_queue_on_a_packet_whose_acceptance_changed_after_it_arrived()
    {
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        Spawn();
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());

        Pump();

        Assert.Empty(_dispatched);
    }

    /// <summary>
    /// The same sequence with the load report in place of the list. It is accepted whatever the
    /// character state, so it dispatches and the packet behind it dispatches too.
    /// </summary>
    [Fact]
    public void Not_wedge_on_a_load_report_that_arrived_before_the_spawn()
    {
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LOADED, new CCharacterLoadedPacket());
        Spawn();
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());

        Pump();

        Assert.Equal(
            [NetworkPacketType.CMSG_CHARACTER_LOADED, NetworkPacketType.CMSG_PLAYER_INPUT],
            _dispatched);
    }

    private sealed class Recorder(NetworkPacketType type, List<NetworkPacketType> log) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => log.Add(type);
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
