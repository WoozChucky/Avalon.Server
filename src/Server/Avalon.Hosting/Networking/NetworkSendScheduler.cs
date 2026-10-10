using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

/// <summary>
/// The world server's send threads (#875): <c>Network:SendThreads</c> dedicated threads, <c>Network.Send.0</c> on, each
/// owning the connections given to it for their lives. The tick only enqueues and wakes every thread once, at its end
/// (<see cref="SignalAll" />); a send from any other thread wakes its owner at once.
/// </summary>
public sealed class NetworkSendScheduler : IDisposable
{
    /// <summary>The longest a send thread sleeps between wakes: its stall clock is read even when nothing is sent.</summary>
    public static readonly TimeSpan WakeInterval = TimeSpan.FromMilliseconds(100);

    // [ThreadStatic] fields take the t_ prefix, which a naming rule cannot select (the static-field rule asks for s_).
#pragma warning disable IDE1006
    [ThreadStatic] private static bool t_deferSignals;
#pragma warning restore IDE1006

    private readonly SendThread[] _threads;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly PacketEncoder _encoder;
    private long _created;
    private int _started;
    private volatile bool _stopping;

    public NetworkSendScheduler(NetworkConfiguration options, ILoggerFactory loggerFactory, TimeProvider time,
        NetworkSendMetrics metrics, PacketEncoder? encoder = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = loggerFactory.CreateLogger<NetworkSendScheduler>();
        _time = time;
        Metrics = metrics;
        _encoder = encoder ?? PacketEncoder.Shared;
        _threads = new SendThread[options.SendThreads];
        for (int i = 0; i < _threads.Length; i++)
            _threads[i] = new SendThread(i);
    }

    public int ThreadCount => _threads.Length;

    /// <summary>True from <see cref="Start" /> until <see cref="Stop" />.</summary>
    public bool IsRunning => Volatile.Read(ref _started) == 1 && !_stopping;

    internal NetworkSendMetrics Metrics { get; }

    /// <summary>
    /// The calling thread's sends leave the wake-up to <see cref="SignalAll" />: the tick, which signals once at its end
    /// rather than once per packet. Every other thread's send wakes its owner at once.
    /// </summary>
    public static void DeferSignalsOnCurrentThread() => t_deferSignals = true;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        foreach (SendThread owner in _threads)
        {
            owner.Thread = new Thread(Loop) { Name = $"Network.Send.{owner.Index}", IsBackground = true };
            owner.Thread.Start(owner);
        }
    }

    /// <summary>A sender for a new connection, owned by the next thread in turn. <paramref name="sealer" /> null: plain inside TLS.</summary>
    public ConnectionSender CreateSender(Guid connectionId, ILogger logger, IAvalonCryptoSession? sealer, Action close)
    {
        int owner = (int)((ulong)Interlocked.Increment(ref _created) % (ulong)_threads.Length);
        return new ConnectionSender(this, owner, connectionId, logger, sealer, _encoder, close, _time);
    }

    /// <summary>The tick's one wake-up per send thread, at its end (the <c>outbox</c> stage).</summary>
    public void SignalAll()
    {
        foreach (SendThread owner in _threads)
            owner.Wake.Set();
    }

    /// <summary>
    /// One wake of send thread <paramref name="thread" />: every connection marked dirty since its last pass is written,
    /// or finished if it closed. Runs on that thread only; public so the tests and the scenario runner can drive a
    /// scheduler whose threads were never started.
    /// </summary>
    public void RunPass(int thread)
    {
        SendThread owner = _threads[thread];
        long started = Stopwatch.GetTimestamp();
        int serviced = 0;
        long maxPending = 0;

        // A connection whose pending write has ended leaves the list its stall clock is read from.
        for (int i = owner.Writing.Count - 1; i >= 0; i--)
        {
            ConnectionSender writing = owner.Writing[i];
            if (!writing.IsWriteInFlight)
            {
                writing.Tracked = false;
                owner.Writing.RemoveAt(i);
            }
        }

        while (owner.Dirty.TryDequeue(out ConnectionSender? sender))
        {
            serviced++;
            try
            {
                sender.Service();
            }
            catch (Exception e)
            {
                // One connection's failure (a payload that cannot be sealed, a broken stream) closes that connection only.
                sender.Fault(e);
            }

            maxPending = Math.Max(maxPending, sender.PendingBytes);
        }

        if (serviced > 0)
            Metrics.Pass(owner.Tag, Stopwatch.GetElapsedTime(started), maxPending);
    }

    /// <summary>Every thread's pass, in turn, on the calling thread: the scenario runner's tick and the tests.</summary>
    public void RunAllPasses()
    {
        for (int i = 0; i < _threads.Length; i++)
            RunPass(i);
    }

    /// <summary>Stops the threads, joining them within one shared <paramref name="joinLimit" />; true when all stopped.</summary>
    public bool Stop(TimeSpan joinLimit)
    {
        _stopping = true;
        foreach (SendThread owner in _threads)
            owner.Wake.Set();

        long deadline = Environment.TickCount64 + (long)joinLimit.TotalMilliseconds;
        bool joined = true;
        foreach (SendThread owner in _threads)
        {
            if (owner.Thread is { } thread
                && !thread.Join(TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64))))
            {
                joined = false;
            }
        }

        return joined;
    }

    public void Dispose()
    {
        // The container disposes it after the world server stopped it; one never stopped stops here.
        if (!_stopping)
            Stop(TimeSpan.FromSeconds(2));

        foreach (SendThread owner in _threads)
            owner.Wake.Dispose();
    }

    internal void MarkDirty(ConnectionSender sender)
    {
        SendThread owner = _threads[sender.OwnerThread];
        owner.Dirty.Enqueue(sender);
        if (!t_deferSignals)
            owner.Wake.Set();
    }

    /// <summary>Owner thread only: a write of <paramref name="sender" /> is pending, so its stall clock is read each pass.</summary>
    internal void TrackWriting(ConnectionSender sender)
    {
        if (sender.Tracked)
            return;

        sender.Tracked = true;
        _threads[sender.OwnerThread].Writing.Add(sender);
    }

    private void Loop(object? state)
    {
        var owner = (SendThread)state!;
        while (!_stopping)
        {
            try
            {
                owner.Wake.Wait(WakeInterval, CancellationToken.None);
                // Reset before the pass, never after: a Set from a send that lands during the pass stays set, and the
                // next wait returns at once. Set and Reset are atomic on the event's state, so a send whose Set came
                // before this Reset pushed its connection before it, where the pass below finds it.
                owner.Wake.Reset();
                RunPass(owner.Index);
            }
            catch (Exception e)
            {
                if (_stopping)
                    break;

                // A send thread never ends silently: what escapes a pass is logged and counted, and the loop goes on.
                _logger.LogCritical(e, "Send thread {Thread} failed outside a connection; it goes on", owner.Index);
                Metrics.ThreadFault(owner.Tag);
            }
        }
    }

    private sealed class SendThread(int index)
    {
        public int Index { get; } = index;

        public ConcurrentQueue<ConnectionSender> Dirty { get; } = new();

        public ManualResetEventSlim Wake { get; } = new(initialState: false);

        /// <summary>Owner thread only: the connections with a write pending, whose stall clock each pass reads.</summary>
        public List<ConnectionSender> Writing { get; } = [];

        public KeyValuePair<string, object?> Tag { get; } = new("thread", index.ToString(CultureInfo.InvariantCulture));

        public Thread? Thread { get; set; }
    }
}
