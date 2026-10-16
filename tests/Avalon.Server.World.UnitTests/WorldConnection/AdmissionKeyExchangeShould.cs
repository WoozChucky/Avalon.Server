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
using Avalon.World.Configuration;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// The key exchange at admission in both of a world's modes, end to end over loopback through the connection's real read
/// loop, reader and admission handler, from what a 0.3.0 client sends (the ticket and a public key of its own, sent before
/// it knows the mode). A world that seals keys the session off the tick, replies with its own key, and opens what the
/// client then seals under it. One with <c>Network:PacketEncryption</c> off runs no exchange (#878): it replies with no key,
/// and closes a connection that sends a packet flagged Encrypted, counted as <c>sealed_before_key</c>: the connection has
/// no session, and will never have one.
/// </summary>
public sealed class AdmissionKeyExchangeShould
{
    /// <summary>
    /// A limit on a hang only: the ticket is redeemed, the session keyed and the frames read on the thread pool, which a
    /// loaded test run can hold for seconds (#960). So the test waits for each outcome, never for an amount of time
    /// (#1019).
    /// </summary>
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(30);

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

    private sealed class Recorder(List<NetworkPacketType> log) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => log.Add(CCharacterListPacket.PacketType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_a_sealed_packet_only_on_a_world_that_seals(bool packetEncryption)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        ((IServerBase)server).ProxyProtocol.Returns(ProxyProtocolPolicy.Disabled);
        List<NetworkPacketType> dispatched = [];
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [CCharacterListPacket.PacketType] = new Recorder(dispatched),
        });
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WorldAdmissionResult(lease, null));
        // A world with no player limit configured (#967): its backstop never refuses.
        var admission = new GameAdmissionHandler(api, new AdmissionSourceBudget(new AdmissionSourceOptions()),
            new WorldBackstop(new GameConfiguration(), TimeProvider.System, NullLogger<WorldBackstop>.Instance), server);
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

        Assert.Equal(packetEncryption, connection.CryptoSession is not null);

        // What a 0.3.0 client sends: the ticket, and a fresh key of its own.
        var clientKeys = new CryptoManager();
        await client.GetStream().WriteAsync(Frame(NetworkPacketType.CMSG_GAME_ADMISSION, NetworkPacketFlags.ClearText,
            Encoded(new CGameAdmissionPacket
            {
                JoinTicket = GameAuthCryptography.NewToken(),
                PublicKey = clientKeys.GetPublicKey(),
            })));
        await Until(connection.FlushContinuations, () => connection.AccountId is not null);

        Assert.Equal(lease.Authority.AccountId, connection.AccountId);
        Assert.True(connection.Sent.TryDequeue(out OutboundPacket sent));
        SGameAdmissionPacket reply = Serializer.Deserialize<SGameAdmissionPacket>(sent.PayloadMemory);
        Assert.Equal(GameAdmissionResult.Accepted, reply.Result);
        Assert.Equal(packetEncryption, reply.PacketEncryption);
        // The version handshake, which is not what this is about.
        Assert.True(connection.AcceptProtocol());

        byte[] sealedList;
        if (packetEncryption)
        {
            // The client keys its session from the reply, as a 0.3.0 client does on a world that seals.
            var session = new AvalonCryptoSession(CryptoRole.Client, clientKeys.GetKeyPair());
            session.Initialize(reply.PublicKey);
            sealedList = session.Encrypt(Encoded(new CCharacterListPacket()));
        }
        else
        {
            // A client that sealed regardless: there is no key it could have sealed under.
            Assert.Empty(reply.PublicKey);
            sealedList = new byte[40];
        }
        await client.GetStream().WriteAsync(Frame(CCharacterListPacket.PacketType, NetworkPacketFlags.Encrypted, sealedList));

        if (packetEncryption)
        {
            // Opened, decoded and queued: the tick's session pass runs it.
            await Until(connection.UpdateSession, () => dispatched.Count > 0 || connection.IsClosing);

            Assert.Equal([CCharacterListPacket.PacketType], dispatched);
            Assert.Empty(connection.Violations);
            Assert.False(connection.IsClosing);
        }
        else
        {
            await connection.Closed.Task.WaitAsync(s_wait);
            Assert.Equal(["sealed_before_key"], connection.Violations);
            Assert.Empty(dispatched);
        }
    }

    /// <summary>Runs <paramref name="pass" /> (a tick's part) until <paramref name="settled" /> holds after one.</summary>
    private static async Task Until(Action pass, Func<bool> settled)
    {
        using var hang = new CancellationTokenSource(s_wait);
        while (true)
        {
            pass();
            if (settled())
                return;
            await Task.Delay(1, hang.Token);
        }
    }
}
