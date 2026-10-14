using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.Handlers;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A world with <c>Network:PacketEncryption</c> off runs no key exchange (#878), end to end over loopback through the
/// connection's real read loop, reader and admission handler: it admits what a 0.3.0 client sends (a public key of its
/// own, which it sends before it knows the mode), answers with no key, and closes a connection that then sends a packet
/// flagged Encrypted, counted as <c>sealed_before_key</c>: the connection has no session, and will never have one.
/// </summary>
public sealed class PlainWorldConnectionShould
{
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A real world connection whose stream is the socket itself: the world's TLS needs a running server, and what the
    /// read loop does with the bytes inside it is the same.
    /// </summary>
    private sealed class LoopbackConnection(IWorldServer server, TcpClient client, IPacketReader reader)
        : Avalon.World.WorldConnection(server, client, NullLoggerFactory.Instance, reader)
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<string> Violations = new();
        public readonly ConcurrentQueue<OutboundPacket> Sent = new();

        protected override Task<PacketStream> GetStream(TcpClient client)
        {
            typeof(Avalon.World.WorldConnection).GetField("_tlsAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, true);
            return Task.FromResult(new PacketStream(new NetworkStream(client.Client)));
        }

        protected override IOutbox OnCreateOutbox() => new RecordedOutbox();

        public override void Send(OutboundPacket packet) => Sent.Enqueue(packet);

        protected override void OnProtocolViolation(string reason)
        {
            Violations.Enqueue(reason);
            base.OnProtocolViolation(reason);
        }

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Nothing reaches it: the connection records what it sends.</summary>
    private sealed class RecordedOutbox : IOutbox
    {
        public bool Seals => false;
        public void Connect(PacketStream stream) { }

        public bool Enqueue(OutboundPacket packet)
        {
            packet.Release();
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Frame(NetworkPacketType type, NetworkPacketFlags flags, byte[] payload)
    {
        using var wire = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(wire,
            new NetworkPacket { Header = new NetworkPacketHeader { Type = type, Flags = flags }, Payload = payload },
            PrefixStyle.Base128);
        return wire.ToArray();
    }

    private static byte[] Encoded(Packet packet)
    {
        using var payload = new MemoryStream();
        Serializer.Serialize(payload, packet);
        return payload.ToArray();
    }

    [Fact]
    public async Task Admit_without_a_key_and_close_on_a_sealed_packet_as_sealed_before_key()
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).ProxyProtocol.Returns(ProxyProtocolPolicy.Disabled);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WorldAdmissionResult(lease, null));
        var admission = new GameAdmissionHandler(api);
        // The server runs admission as it arrives, as ServerBase.CallListener does.
        ((IServerBase)server).CallListener(Arg.Any<IConnection>(), Arg.Any<NetworkPacketHeader>(), Arg.Any<Packet?>())
            .Returns(call => admission.ExecuteAsync(new WorldPacketContext<CGameAdmissionPacket>
            {
                Connection = (IWorldConnection)call[0],
                Packet = (CGameAdmissionPacket)call[2]!,
            }));
        var reader = new PacketReader(NullLoggerFactory.Instance, Options.Create(new HostingConfiguration()),
            [typeof(CGameAdmissionPacket), typeof(CCharacterListPacket)]);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient accepted = await accept;
        using var connection = new LoopbackConnection(server, accepted, reader);
        await connection.StartAsync(CancellationToken.None);

        Assert.Null(connection.CryptoSession);

        // What a 0.3.0 client sends: the ticket, and a fresh key of its own.
        await client.GetStream().WriteAsync(Frame(NetworkPacketType.CMSG_GAME_ADMISSION, NetworkPacketFlags.ClearText,
            Encoded(new CGameAdmissionPacket
            {
                JoinTicket = GameAuthCryptography.NewToken(),
                PublicKey = new CryptoManager().GetPublicKey(),
            })));
        DateTime deadline = DateTime.UtcNow.Add(s_wait);
        while (connection.AccountId is null && DateTime.UtcNow < deadline)
        {
            connection.FlushContinuations();
            await Task.Yield();
        }

        Assert.Equal(lease.Authority.AccountId, connection.AccountId);
        Assert.True(connection.Sent.TryDequeue(out OutboundPacket sent));
        SGameAdmissionPacket reply = Serializer.Deserialize<SGameAdmissionPacket>(sent.PayloadMemory);
        Assert.Equal(GameAdmissionResult.Accepted, reply.Result);
        Assert.False(reply.PacketEncryption);
        Assert.Empty(reply.PublicKey);
        Assert.Null(connection.CryptoSession);

        // A client that sealed regardless: there is no key it could have sealed under.
        await client.GetStream().WriteAsync(Frame(CCharacterListPacket.PacketType, NetworkPacketFlags.Encrypted,
            new byte[40]));
        await connection.Closed.Task.WaitAsync(s_wait);

        Assert.Equal(["sealed_before_key"], connection.Violations);
    }
}
