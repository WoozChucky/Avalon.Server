using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
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

/// <summary>ProcessQueue runs the world packets handled on the tick; each one gets its span there.</summary>
public sealed class ProcessQueueTelemetryShould : IDisposable
{
    private readonly string _name = $"test-{Guid.NewGuid()}";
    private readonly ActivitySource _source;
    private readonly Meter _meter;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;
    private readonly List<NetworkPacketType> _dispatched = [];
    private TestConnection? _connection;

    public ProcessQueueTelemetryShould()
    {
        _source = new ActivitySource(_name);
        _meter = new Meter(_name);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _spans.Add(a),
        };
        ActivitySource.AddActivityListener(_listener);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _clientSide = new TcpClient();
        _clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _serverSide.Dispose();
        _listener.Dispose();
        _source.Dispose();
        _meter.Dispose();
    }

    private TestConnection Connect(IWorldPacketHandler characterList, PacketDispatchTelemetry? telemetry)
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);
        server.PacketTelemetry.Returns(telemetry);
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [NetworkPacketType.CMSG_CHARACTER_LIST] = characterList,
            [NetworkPacketType.CMSG_PLAYER_INPUT] = new Recorder(NetworkPacketType.CMSG_PLAYER_INPUT, _dispatched),
        });
        _connection = new TestConnection(server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        return _connection;
    }

    private static void Pump(TestConnection connection)
    {
        connection.UpdateSession();
        connection.UpdateMap();
    }

    [Fact]
    public void Start_a_span_for_a_queued_packet_it_handles()
    {
        TestConnection connection = Connect(new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched),
            new PacketDispatchTelemetry(_source, _meter));

        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        Pump(connection);

        Activity span = Assert.Single(_spans);
        Assert.Equal("packet CMSG_CHARACTER_LIST", span.DisplayName);
        Assert.Equal(connection.Id.ToString(), span.GetTagItem("avalon.connection.id"));
        Assert.Equal("ok", span.GetTagItem("avalon.outcome"));
    }

    [Fact]
    public void Carry_the_character_on_the_span_once_one_is_in_the_world()
    {
        TestConnection connection = Connect(new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched),
            new PacketDispatchTelemetry(_source, _meter, noSpanPacketTypes: []));
        connection.AccountId = new AccountId(42L);
        connection.Character = new CharacterEntity { Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 } };

        connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        Pump(connection);

        Activity span = Assert.Single(_spans);
        Assert.Equal(42L, span.GetTagItem("avalon.account.id"));
        Assert.Equal(7u, span.GetTagItem("avalon.character.id"));
    }

    [Fact]
    public void Handle_player_input_without_a_span()
    {
        TestConnection connection = Connect(new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched),
            new PacketDispatchTelemetry(_source, _meter));
        connection.Character = new CharacterEntity { Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 } };

        connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        Pump(connection);

        Assert.Equal([NetworkPacketType.CMSG_PLAYER_INPUT], _dispatched);
        Assert.Empty(_spans);
    }

    [Fact]
    public void Mark_the_span_as_an_error_when_the_handler_throws()
    {
        TestConnection connection = Connect(new Throwing(), new PacketDispatchTelemetry(_source, _meter));

        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        Pump(connection);

        Activity span = Assert.Single(_spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public void Still_dispatch_when_the_server_has_no_telemetry()
    {
        TestConnection connection = Connect(new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, _dispatched), telemetry: null);

        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        Pump(connection);

        Assert.Equal([NetworkPacketType.CMSG_CHARACTER_LIST], _dispatched);
    }

    private sealed class Recorder(NetworkPacketType type, List<NetworkPacketType> log) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => log.Add(type);
    }

    private sealed class Throwing : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => throw new InvalidOperationException("boom");
    }

    private sealed class TestConnection(
        IWorldServer server, TcpClient client, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        IPacketReader reader) : Avalon.World.WorldConnection(server, client, loggerFactory, reader)
    {
        public void Deliver(NetworkPacketType type, Packet payload) =>
            OnReceive(new NetworkPacketHeader { Type = type }, payload).GetAwaiter().GetResult();
    }
}
