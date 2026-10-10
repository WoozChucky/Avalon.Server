using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

public class WorldConnectionOutboxShould : IDisposable
{
    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;
    private readonly NetworkSendScheduler _scheduler;

    public WorldConnectionOutboxShould()
    {
        // One send thread, never started: the test runs its pass.
        _scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 }, NullLoggerFactory.Instance,
            TimeProvider.System, NetworkSendMetrics.Disabled);
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.SendScheduler.Returns(_scheduler);

        (TcpClient? clientSide, TcpClient? serverSide) = CreateLoopbackPair();
        _serverSide = serverSide;

        _connection = new Avalon.World.WorldConnection(
            server,
            clientSide,
            NullLoggerFactory.Instance,
            Substitute.For<IPacketReader>());
    }

    public void Dispose()
    {
        _connection.Close();
        // The close's last visit, so the sender finishes and gives back its buffers.
        _scheduler.RunAllPasses();
        _connection.Dispose();
        _scheduler.Dispose();
        _serverSide.Dispose();
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

    /// <summary>The tick only queues (#875): the packet reaches the socket when its send thread runs.</summary>
    [Fact]
    public void Write_a_queued_packet_when_its_send_thread_runs()
    {
        var mem = new MemoryStream();
        _connection.InitOutboxForTest(new PacketStream(mem));

        _connection.Send(SPingPacket.Create(0L, 0L, 0L, 0L, PacketEncoder.Shared));
        Assert.Equal(0, mem.Length);

        _scheduler.RunAllPasses();
        Assert.True(mem.Length > 0, "Expected packet bytes written once the send thread ran");
    }
}
