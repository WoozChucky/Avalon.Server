using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Fakes;

/// <summary>The real receive/dispatch path, without a read loop, serialization or allocating substitutes.</summary>
public sealed class ReceiveQueueFixture : IDisposable
{
    private static readonly Func<WorldConnection, int> s_queueDepth = typeof(WorldConnection)
        .GetProperty("ReceiveQueueDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!
        .CreateDelegate<Func<WorldConnection, int>>();
    private readonly TcpClient _client;
    private readonly TcpClient _peer;
    private readonly Recorder _recorder = new();

    public ReceiveQueueFixture()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _client = new TcpClient();
        _client.Connect((IPEndPoint)listener.LocalEndpoint);
        _peer = listener.AcceptTcpClient();
        var server = new QueueServer(_recorder);
        Connection = new ReceiveConnection(server, _client, Clock);
        // Transport authentication is setup only; the TLS tests exercise the real handshake.
        typeof(WorldConnection).GetField("_tlsAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Connection, true);
        GameSessionLease lease = GameSessionLease.TryCreate(new SessionLeaseResponse
        {
            State = "active",
            AccountId = "42",
            GameSessionId = Guid.NewGuid().ToString("D"),
            GameContextId = Guid.NewGuid().ToString("D"),
            FencingToken = "1",
            ServerId = "queue-fixture",
            WorldId = 1,
            AccessLevel = 1,
            CredentialsVersion = 0,
            SessionEpoch = "0",
            LeaseUntil = Clock.GetUtcNow().UtcDateTime.AddSeconds(44),
            AuthorizationUntil = Clock.GetUtcNow().UtcDateTime.AddMinutes(5),
        }, "queue-fixture", 1, Clock)!;
        Connection.PublishAdmission(lease);
        Connection.AcceptProtocol();
        Connection.Character = new CharacterEntity { Data = new Character { Id = new CharacterId(7), Map = 1 } };
    }

    public ReceiveConnection Connection { get; }
    public QueueClock Clock { get; } = new();
    public int Dispatched => _recorder.Count;
    public int QueueDepth => s_queueDepth(Connection);
    public Action<IWorldConnection, Packet>? OnDispatch { get => _recorder.OnDispatch; set => _recorder.OnDispatch = value; }

    public void Pump()
    {
        Connection.UpdateSession();
        Connection.UpdateMap();
    }

    public void Dispose()
    {
        Connection.Close();
        Connection.Dispose();
        _client.Dispose();
        _peer.Dispose();
    }

    public sealed class QueueClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }

    public sealed class ReceiveConnection(IWorldServer server, TcpClient client, TimeProvider clock)
        : WorldConnection(server, client, NullLoggerFactory.Instance, new UnusedReader(), clock)
    {
        public void Deliver(NetworkPacketType type, Packet payload) =>
            OnReceive(new NetworkPacketHeader { Type = type }, payload).GetAwaiter().GetResult();

        protected override Task OnClose(bool expected = true) => Task.CompletedTask;
    }

    private sealed class Recorder : IWorldPacketHandler
    {
        public int Count { get; private set; }
        public Action<IWorldConnection, Packet>? OnDispatch { get; set; }
        public void Execute(IWorldConnection connection, Packet packet)
        {
            Count++;
            OnDispatch?.Invoke(connection, packet);
        }
    }

    private sealed class QueueServer : IWorldServer, IServerBase
    {
        public QueueServer(IWorldPacketHandler handler)
        {
            PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>
            {
                [NetworkPacketType.CMSG_PLAYER_INPUT] = handler,
                [NetworkPacketType.CMSG_CHARACTER_LIST] = handler,
                [NetworkPacketType.CMSG_CHARACTER_LEAVE] = handler,
                [NetworkPacketType.CMSG_CHARACTER_LOADED] = handler,
            };
        }

        public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }
        public PacketDispatchTelemetry PacketTelemetry => PacketDispatchTelemetry.Disabled;
        public bool PacketEncryption => false;
        public ImmutableArray<IWorldConnection> Connections => throw new NotSupportedException();
        public IWorld World => throw new NotSupportedException();
        public NetworkSendScheduler SendScheduler => throw new NotSupportedException();
        public IReadOnlyList<IWorldConnection> SessionsOf(AccountId accountId, IWorldConnection except) => throw new NotSupportedException();
        public IReadOnlyList<IWorldConnection> SessionsHoldingCharacter(CharacterId id, IWorldConnection except) => throw new NotSupportedException();
        public Task RemoveConnection(IConnection connection) => throw new NotSupportedException();
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => throw new NotSupportedException();
        public long ServerTime => throw new NotSupportedException();
        public int SendBufferCapacity => throw new NotSupportedException();
        public void CallConnectionListener(IConnection connection) => throw new NotSupportedException();
    }

    private sealed class UnusedReader : IPacketReader
    {
        public int BufferSize => throw new NotSupportedException();
        public Packet? Read(InboundPacketFrame frame, DecryptFunc? decrypt = null) => throw new NotSupportedException();
    }
}
