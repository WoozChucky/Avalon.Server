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

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// A packet no filter accepts at arrival, once the connection is admitted, is dropped there (#861). It never
/// reaches the receive-path dispatcher, which runs off the tick, and it is not queued either: an input sent
/// before the spawn must not move a character the spawn creates while it is in flight.
/// </summary>
public sealed class UnacceptedPacketShould : IDisposable
{
    private readonly TcpClient _serverSide;
    private readonly IWorldServer _server;
    private readonly TestConnection _connection;
    private readonly List<NetworkPacketType> _dispatched = [];

    public UnacceptedPacketShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();

        _server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)_server).SendBufferCapacity.Returns(256);
        _server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [NetworkPacketType.CMSG_CHARACTER_LIST] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched),
            [NetworkPacketType.CMSG_PLAYER_INPUT] = new Recorder(NetworkPacketType.CMSG_PLAYER_INPUT, _dispatched),
        });

        _connection = new TestConnection(_server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        GameplayTestAdmission.Admit(_connection);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    // CharacterEntity.Map reads through Data, so a row is what puts the character in a map.
    private void Spawn() => _connection.Character = new CharacterEntity
    {
        Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 }
    };

    [Theory]
    // An in-map packet before the spawn: the client sends input from the moment it enters.
    [InlineData(NetworkPacketType.CMSG_PLAYER_INPUT, false)]
    // A select-phase packet once a character is in the world.
    [InlineData(NetworkPacketType.CMSG_CHARACTER_LIST, true)]
    // A packet no filter accepts in any state.
    [InlineData(NetworkPacketType.CMSG_AUTH, false)]
    public void Be_dropped_at_arrival_and_never_handled(NetworkPacketType type, bool spawnedBefore)
    {
        if (spawnedBefore) Spawn();

        _connection.Deliver(type, type == NetworkPacketType.CMSG_CHARACTER_LIST
            ? new CCharacterListPacket()
            : new CPlayerInputPacket());
        if (!spawnedBefore) Spawn();
        _connection.UpdateSession();
        _connection.UpdateMap();

        _ = ((IServerBase)_server).DidNotReceive()
            .CallListener(Arg.Any<IConnection>(), Arg.Any<NetworkPacketHeader>(), Arg.Any<Packet?>());
        Assert.Empty(_dispatched);
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
