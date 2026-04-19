// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.WorldConnection;

public class WorldConnectionOutboxShould : IDisposable
{
    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;

    public WorldConnectionOutboxShould()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).Crypto.Returns(new CryptoManager());
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        var (clientSide, serverSide) = CreateLoopbackPair();
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
        _connection.Dispose();
        _serverSide.Dispose();
    }

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        var serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    [Fact]
    public async Task FlushOutbox_AfterEnqueue_WritesBytes()
    {
        // Use an in-memory stream so the test is independent of socket timing.
        var mem = new MemoryStream();
        var stream = new PacketStream(mem);
        _connection.InitOutboxForTest(stream);

        _connection.Send(SPingPacket.Create(0L, 0L, 0L, 0L));
        _connection.FlushOutbox();

        // TickDrivenOutbox.Flush schedules an async WriteAsync continuation.
        // Give the thread-pool one tick to complete it.
        await Task.Delay(50);

        Assert.True(mem.Length > 0, "Expected packet bytes written to the outbox stream after FlushOutbox");
    }
}
