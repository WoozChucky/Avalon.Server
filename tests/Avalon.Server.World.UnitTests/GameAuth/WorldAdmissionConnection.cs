using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.GameAuth;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.GameAuth;

// Packet tests exercise a connected socket without starting a read loop. Transport authentication
// is supplied here only; WorldTlsTransportShould checks the real TLS path separately.
internal sealed class WorldAdmissionConnection : Avalon.World.WorldConnection
{
    private readonly TcpClient _peer;
    private WorldAdmissionConnection(IWorldServer server, TcpClient client, TcpClient peer)
        : base(server, client, NullLoggerFactory.Instance, Substitute.For<IPacketReader>()) { _peer = peer; }
    public List<NetworkPacket> Sent { get; } = [];
    public override void Send(NetworkPacket packet) => Sent.Add(packet);
    protected override Task OnClose(bool expected = true) => Task.CompletedTask;
    public void Deliver(NetworkPacketType type, Packet payload) => OnReceive(new() { Type = type }, payload).GetAwaiter().GetResult();
    public static WorldAdmissionConnection Create(IWorldServer? server = null, bool tls = true)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var client = new TcpClient(); client.Connect((IPEndPoint)listener.LocalEndpoint);
        var peer = listener.AcceptTcpClient();
        server ??= Substitute.For<IWorldServer, IServerBase>();
        var result = new WorldAdmissionConnection(server, client, peer);
        typeof(Avalon.World.WorldConnection).GetField("_tlsAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(result, tls);
        return result;
    }
    public static GameSessionLease Lease(TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        return GameSessionLease.TryCreate(new()
        {
            State = "active",
            AccountId = "42",
            GameSessionId = Guid.NewGuid().ToString("D"),
            GameContextId = Guid.NewGuid().ToString("D"),
            FencingToken = "7",
            ServerId = "world-one",
            WorldId = 1,
            AccessLevel = 1,
            CredentialsVersion = 3,
            SessionEpoch = "9",
            LeaseUntil = clock.GetUtcNow().UtcDateTime.AddSeconds(44),
            AuthorizationUntil = clock.GetUtcNow().UtcDateTime.AddMinutes(5)
        }, "world-one", 1, clock)!;
    }
    public new void Dispose() { base.Dispose(); _peer.Dispose(); }
}
