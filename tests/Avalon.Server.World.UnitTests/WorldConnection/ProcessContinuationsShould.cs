using System.Net;
using System.Net.Sockets;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

public sealed class ProcessContinuationsShould : IDisposable
{
    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;

    public ProcessContinuationsShould()
    {
        // Build a mock that satisfies both IWorldServer and IServerBase (WorldConnection
        // casts its first arg to IServerBase in the base constructor).
        var server = Substitute.For<IWorldServer, IServerBase>();
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

    // Creates a real connected TCP pair on loopback using synchronous API to avoid
    // async complexity in test constructors. The connection is only needed so
    // WorldConnection's base constructor can read RemoteEndPoint; no packets are sent.
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
    public void Should_Not_Invoke_Callback_When_Task_Is_Faulted()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetException(new InvalidOperationException("simulated DB fault"));
        var callbackInvoked = false;

        _connection.EnqueueContinuation(tcs.Task, () => callbackInvoked = true);
        _connection.FlushContinuations();

        Assert.False(callbackInvoked);
    }

    [Fact]
    public void Should_Defer_Incomplete_Task_To_Next_Tick_And_Invoke_When_Complete()
    {
        // One flush processes each queued item at most once: an item whose task is still
        // running is re-enqueued past the count snapshot, so it waits for the next tick
        // instead of spinning in this one.
        var tcs = new TaskCompletionSource();
        var callbackInvoked = false;

        _connection.EnqueueContinuation(tcs.Task, () => callbackInvoked = true);

        _connection.FlushContinuations();   // tick 1: deferred, not re-processed
        Assert.False(callbackInvoked);

        tcs.SetResult();                    // task completes between ticks

        _connection.FlushContinuations();   // tick 2: task complete → callback fires
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void Should_Invoke_Typed_Callback_With_Correct_Result()
    {
        int receivedValue = 0;

        _connection.EnqueueContinuation(Task.FromResult(42), value => receivedValue = value);
        _connection.FlushContinuations();

        Assert.Equal(42, receivedValue);
    }
}
