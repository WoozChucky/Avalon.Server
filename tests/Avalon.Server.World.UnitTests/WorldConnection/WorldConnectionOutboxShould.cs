using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Org.BouncyCastle.Crypto;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.WorldConnection;

public class WorldConnectionOutboxShould : IDisposable
{
    private readonly NetworkSendScheduler _scheduler;
    private Avalon.World.WorldConnection? _connection;
    private TcpClient? _serverSide;

    public WorldConnectionOutboxShould() =>
        // One send thread, never started: the test runs its pass.
        _scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 }, NullLoggerFactory.Instance,
            TimeProvider.System, NetworkSendMetrics.Disabled);

    public void Dispose()
    {
        _connection?.Close();
        // The close's last visit, so the sender finishes and gives back its buffers.
        _scheduler.RunAllPasses();
        _connection?.Dispose();
        _scheduler.Dispose();
        _serverSide?.Dispose();
    }

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    /// <summary>
    /// The tick only queues (#875): the packet reaches the socket when its send thread runs, sealed with the session
    /// exactly when the world seals (Network:PacketEncryption), plain inside TLS otherwise.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_a_queued_packet_when_its_send_thread_runs_sealed_only_while_the_world_seals(bool packetEncryption)
    {
        Avalon.World.WorldConnection connection = Connect(packetEncryption);
        AvalonCryptoSession client = PairWithClient(connection);
        var wire = new MemoryStream();
        connection.InitOutboxForTest(new PacketStream(wire));

        connection.Send(SPlayerStateAckPacket.Create(7, 1, 2, 3, 0, 0, 90, PacketEncoder.Shared)); // flagged Encrypted
        Assert.Equal(0, wire.Length);

        _scheduler.RunAllPasses();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (NetworkPacketHeader header, byte[] payload) = Assert.Single(await FramesAsync(wire.ToArray(), timeout.Token));
        Assert.Equal(packetEncryption, (header.Flags & NetworkPacketFlags.Encrypted) != 0);
        byte[] plain = packetEncryption ? Open(client, payload) : payload;
        Assert.Equal(7u, Serializer.Deserialize<SPlayerStateAckPacket>(plain.AsSpan()).Seq);
    }

    private Avalon.World.WorldConnection Connect(bool packetEncryption)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.SendScheduler.Returns(_scheduler);
        server.PacketEncryption.Returns(packetEncryption);
        (TcpClient clientSide, TcpClient serverSide) = CreateLoopbackPair();
        _serverSide = serverSide;
        _connection = new Avalon.World.WorldConnection(server, clientSide, NullLoggerFactory.Instance,
            Substitute.For<IPacketReader>());
        return _connection;
    }

    /// <summary>A client session keyed against the connection's, as admission keys the two.</summary>
    private static AvalonCryptoSession PairWithClient(Avalon.World.WorldConnection connection)
    {
        AsymmetricCipherKeyPair keys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, keys);
        connection.CryptoSession.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(keys)));
        client.Initialize(connection.CryptoSession.GetPublicKey());
        return client;
    }

    private static byte[] Open(AvalonCryptoSession client, byte[] sealedPayload)
    {
        byte[] plain = new byte[sealedPayload.Length];
        return plain[..client.Decrypt(sealedPayload, plain)];
    }

    private static async Task<List<(NetworkPacketHeader Header, byte[] Payload)>> FramesAsync(byte[] wire, CancellationToken ct)
    {
        var frames = new List<(NetworkPacketHeader, byte[])>();
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(wire)).EnumerateRawFramesAsync(256, ct))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            frames.Add((frame.Header, frame.Payload.ToArray()));
        }

        return frames;
    }
}
