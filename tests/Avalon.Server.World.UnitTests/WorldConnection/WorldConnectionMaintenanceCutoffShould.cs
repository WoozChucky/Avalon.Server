using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Movement;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.WorldConnection;

public sealed class WorldConnectionMaintenanceCutoffShould : IDisposable
{
    private readonly TcpClient _client;
    private readonly TcpClient _serverSocket;
    private readonly TestConnection _connection;
    private readonly List<NetworkPacketType> _handled = [];

    public WorldConnectionMaintenanceCutoffShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _client = new TcpClient();
        _client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSocket = listener.AcceptTcpClient();
        listener.Stop();

        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [NetworkPacketType.CMSG_CHARACTER_LEAVE] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LEAVE, _handled),
            [NetworkPacketType.CMSG_PLAYER_INPUT] = new Recorder(NetworkPacketType.CMSG_PLAYER_INPUT, _handled),
        });
        _connection = new TestConnection(server, _client, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        _connection.AccountId = new AccountId(42);
        GameplayTestAdmission.Admit(_connection);
        _connection.Character = new CharacterEntity
        {
            Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 }
        };
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSocket.Dispose();
    }

    [Fact]
    public void Dispatch_session_and_map_packets_before_the_cutoff()
    {
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LEAVE, new CCharacterLeavePacket());
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        _connection.UpdateSession();
        _connection.UpdateMap();
        Assert.Equal([NetworkPacketType.CMSG_CHARACTER_LEAVE, NetworkPacketType.CMSG_PLAYER_INPUT], _handled);
    }

    [Fact]
    public void Discard_queued_and_subsequent_packets_after_the_cutoff()
    {
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LEAVE, new CCharacterLeavePacket());
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        _connection.BlockForMaintenance();
        _connection.UpdateSession();
        _connection.UpdateMap();
        _connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LEAVE, new CCharacterLeavePacket());
        _connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        _connection.UpdateSession();
        _connection.UpdateMap();
        Assert.Empty(_handled);
    }

    private sealed class Recorder(NetworkPacketType type, List<NetworkPacketType> log) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => log.Add(type);
    }

    private sealed class TestConnection(IWorldServer server, TcpClient client,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory, IPacketReader reader)
        : Avalon.World.WorldConnection(server, client, loggerFactory, reader)
    {
        public void Deliver(NetworkPacketType type, Packet payload) =>
            OnReceive(new NetworkPacketHeader { Type = type }, payload).GetAwaiter().GetResult();
    }
}
