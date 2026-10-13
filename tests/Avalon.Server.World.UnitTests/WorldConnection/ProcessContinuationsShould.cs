using System.Net;
using System.Net.Sockets;
using Avalon.Hosting.Networking;
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
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();

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
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    [Fact]
    public void Should_Not_Invoke_Callback_When_Task_Is_Faulted()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetException(new InvalidOperationException("simulated DB fault"));
        bool callbackInvoked = false;

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
        bool callbackInvoked = false;

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

    /// <summary>
    /// A callback that throws is one request failing, not the tick. Escaping here, it would end the
    /// flush for every connection after this one, and leave the rest of this connection's queue
    /// waiting a tick.
    /// </summary>
    [Fact]
    public void Contain_a_callback_that_throws_and_still_run_the_callbacks_queued_after_it()
    {
        bool laterInvoked = false;

        _connection.EnqueueContinuation(Task.CompletedTask, () => throw new NullReferenceException("simulated"));
        _connection.EnqueueContinuation(Task.FromResult(1), _ => laterInvoked = true);

        Exception? escaped = Record.Exception(() => _connection.FlushContinuations());

        Assert.Null(escaped);
        Assert.True(laterInvoked);
    }

    public enum Outcome
    {
        Succeeds,
        Faults,
        IsCancelled,
    }

    /// <summary>
    /// A continuation queued to run whatever its task's outcome (<see cref="ISettledContinuations" />) waits for its
    /// task like any other, then runs, handed the task, for a task that failed as for one that succeeded: the
    /// ordinary continuation's callback is dropped then, and whatever it was to end never ends. It keeps its place
    /// in the queue, ahead of what is queued after it, and a throw out of it is contained like any other's.
    /// </summary>
    [Theory]
    [InlineData(Outcome.Succeeds, TaskStatus.RanToCompletion)]
    [InlineData(Outcome.Faults, TaskStatus.Faulted)]
    [InlineData(Outcome.IsCancelled, TaskStatus.Canceled)]
    public void Run_a_settled_continuation_once_its_task_has_ended_whatever_the_outcome(Outcome outcome, TaskStatus status)
    {
        var ran = new List<string>();
        var work = new TaskCompletionSource<int>();

        _connection.EnqueueContinuation(Task.CompletedTask, () => ran.Add("before"));
        ((ISettledContinuations)_connection).EnqueueSettled(work.Task, task =>
        {
            ran.Add($"settled: {task.Status}");
            throw new InvalidOperationException("simulated");
        });
        _connection.EnqueueContinuation(Task.FromResult(1), _ => ran.Add("after"));

        _connection.FlushContinuations(); // its task has not ended: the others run, it waits
        Assert.Equal(["before", "after"], ran);

        switch (outcome)
        {
            case Outcome.Succeeds:
                work.SetResult(1);
                break;
            case Outcome.Faults:
                work.SetException(new InvalidOperationException("simulated build fault"));
                break;
            case Outcome.IsCancelled:
                work.SetCanceled();
                break;
        }

        _connection.EnqueueContinuation(Task.CompletedTask, () => ran.Add("later"));
        Exception? escaped = Record.Exception(() => _connection.FlushContinuations());

        Assert.Null(escaped);
        Assert.Equal(["before", "after", $"settled: {status}", "later"], ran);
    }

    /// <summary>
    /// #704: a task that succeeds on another thread while the tick is flushing still gets its callback.
    /// The flush used to read "succeeded?" and then "finished?", so a task that finished between the two
    /// reads looked faulted and its callback was dropped for good (a Change Character leave then never
    /// answered or closed its connection). The race is forced as hard as a test can: one thread completes
    /// each task after a random spin while this one flushes in a tight loop. Before the fix about one
    /// callback in twenty was lost this way; after it none can be, so this cannot fail spuriously, and a
    /// run that happens not to hit the window only proves less.
    /// </summary>
    [Fact]
    public void Run_the_callback_of_a_task_that_succeeds_while_the_flush_is_reading_it()
    {
        const int Iterations = 50_000;
        TaskCompletionSource<bool>? pending = null;
        bool stop = false;
        var completer = new Thread(() =>
        {
            var spin = new SpinWait();
            while (!Volatile.Read(ref stop))
            {
                if (Volatile.Read(ref pending) is not { } tcs)
                {
                    spin.SpinOnce(sleep1Threshold: -1);
                    continue;
                }

                Thread.SpinWait(Random.Shared.Next(0, 200));
                tcs.SetResult(true);
                Volatile.Write(ref pending, null);
            }
        })
        { IsBackground = true };
        completer.Start();

        int lost = 0;
        try
        {
            // Bounded in time too, so a slow or single-core runner only runs fewer rounds.
            var budget = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < Iterations && budget.Elapsed < TimeSpan.FromSeconds(3); i++)
            {
                var tcs = new TaskCompletionSource<bool>();
                bool invoked = false;
                _connection.EnqueueContinuation(tcs.Task, _ => invoked = true);
                Volatile.Write(ref pending, tcs);

                // Flush as the tick does. Once a flush that began with the task already finished still has
                // not run the callback, an earlier flush dropped it.
                while (!invoked)
                {
                    bool finishedBefore = tcs.Task.IsCompleted;
                    _connection.FlushContinuations();
                    if (!invoked && finishedBefore)
                    {
                        lost++;
                        break;
                    }
                }

                var spin = new SpinWait();
                while (Volatile.Read(ref pending) is not null)
                    spin.SpinOnce(sleep1Threshold: -1);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            completer.Join();
        }

        Assert.Equal(0, lost);
    }
}
