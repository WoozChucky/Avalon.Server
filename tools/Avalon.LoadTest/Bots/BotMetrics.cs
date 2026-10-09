using System.Collections.Concurrent;
using System.Diagnostics;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// What the bots measure on the client side, by window: input-to-ack latency, entry time (ticket to first ack), entry
/// attempts and failures by kind, leave failures by kind, sign-in and sign-out failures and unexpected disconnects.
/// <see cref="TakeWindow"/> closes the current window and opens the next. Every recording method is safe from any
/// thread and takes no lock.
/// </summary>
/// <remarks>
/// <para>
/// The counted events (entries, sign-in, sign-out and leave failures, disconnects) each land in exactly one window,
/// whole: a recorder registers on the window it read (<see cref="Enter"/>) and moves to the new one if that was
/// swapped meanwhile, and <see cref="TakeWindow"/> waits for the recorders still registered on the window it closed
/// before reading it. Entry attempts are not a counter of their own but successes plus failures, so the two always
/// agree. Ack samples are not gated: they feed percentiles, and a sample landing in the closed window just after it
/// was read is one sample fewer, not a miscount. An entry time is written with its success, inside the gate.
/// </para>
/// <para>
/// An input's send time is kept per bot in a ring of <see cref="RingSize"/> slots indexed by <c>seq % 64</c>; its ack
/// takes it back out. An input still unanswered when its slot is needed again (about a second at 60 Hz) is recorded
/// at its age then, a lower bound, so a server too slow to answer shows in the tail rather than vanishing from it.
/// </para>
/// </remarks>
public sealed class BotMetrics
{
    /// <summary>The most latency samples a window keeps; past it, reservoir sampling keeps a uniform subset.</summary>
    public const int MaxSamples = 200_000;

    private const int RingSize = 64;
    private const int MaxEntrySamples = 10_000;

    private readonly ConcurrentDictionary<int, PendingRing> _pending = new();
    private Window _window = new();

    /// <summary>Notes input <paramref name="seq"/> of <paramref name="bot"/> sent at <paramref name="timestamp"/> (<see cref="Stopwatch.GetTimestamp"/>).</summary>
    public void InputSent(int bot, uint seq, long timestamp)
    {
        PendingRing ring = _pending.GetOrAdd(bot, static _ => new PendingRing());
        if (ring.Put(seq, timestamp, out long overwritten))
            Volatile.Read(ref _window).Acks.Add(Milliseconds(timestamp - overwritten));
    }

    /// <summary>An ack of input <paramref name="seq"/> arrived at <paramref name="timestamp"/>: one latency sample, if the send is known.</summary>
    public void AckReceived(int bot, uint seq, long timestamp)
    {
        if (_pending.TryGetValue(bot, out PendingRing? ring) && ring.Take(seq, out long sent))
            Volatile.Read(ref _window).Acks.Add(Milliseconds(timestamp - sent));
    }

    /// <summary>
    /// Forgets <paramref name="bot"/>'s unanswered inputs: those sent before its character was in the world, or on a
    /// connection now closed, are never answered and are not latency.
    /// </summary>
    public void ForgetPending(int bot)
    {
        if (_pending.TryGetValue(bot, out PendingRing? ring)) ring.Clear();
    }

    /// <summary>
    /// An attempt to reach the world (an entry, or a change of character) reached it, <paramref name="ticketToFirstAck"/>
    /// after its join ticket was issued. An attempt is counted with its outcome, in the window the outcome lands in, so
    /// one that ends neither way (cancelled) counts nowhere and failures ÷ attempts is never diluted.
    /// </summary>
    public void EntrySucceeded(TimeSpan ticketToFirstAck)
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref window.EntrySuccesses);
            window.Entries.Add(ticketToFirstAck.TotalMilliseconds);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>
    /// An attempt to reach the world failed, counted as an attempt too; <paramref name="kind"/> names the step and what
    /// went wrong (<c>join:ACTIVE_GAME_SESSION</c>, <c>spawn:timeout</c>, ...).
    /// </summary>
    public void EntryFailed(string kind)
    {
        Window window = Enter();
        try
        {
            window.EntryFailures.AddOrUpdate(kind, 1, static (_, n) => n + 1);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>A sign-in or a context refresh failed; <paramref name="kind"/> names the REST step.</summary>
    public void SignInFailed(string kind)
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref window.SignInFailures);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>
    /// A leave outside an entry attempt (a disconnect's) failed; <paramref name="kind"/> names how (<c>leave:timeout</c>,
    /// ...). Not an entry failure: no attempt goes with it, so it stays out of the admission ratio.
    /// </summary>
    public void LeaveFailed(string kind)
    {
        Window window = Enter();
        try
        {
            window.LeaveFailures.AddOrUpdate(kind, 1, static (_, n) => n + 1);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>A game context's sign-out failed (no reply, a 5xx, another error, or its own timeout).</summary>
    public void SignOutFailed()
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref window.SignOutFailures);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>A bot in the world lost its connection without asking to.</summary>
    public void Disconnected(int bot)
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref window.Disconnects);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>
    /// Everything recorded since the last call (or since construction), and a fresh window for what follows. Read once
    /// no recorder is still registered on the closed window, so each snapshot is whole; its attempts are its successes
    /// plus its failures.
    /// </summary>
    public StepClientValues TakeWindow()
    {
        Window closed = Interlocked.Exchange(ref _window, new Window());
        var spin = new SpinWait();
        while (Volatile.Read(ref closed.Writers) != 0) spin.SpinOnce();

        double[] acks = closed.Acks.Sorted();
        double[] entries = closed.Entries.Sorted();
        var entryFailures = new Dictionary<string, int>(closed.EntryFailures, StringComparer.Ordinal);
        int entrySuccesses = (int)Volatile.Read(ref closed.EntrySuccesses);
        return new StepClientValues(
            Percentile(acks, 0.50), Percentile(acks, 0.95), Percentile(acks, 0.99),
            entrySuccesses + entryFailures.Values.Sum(),
            entryFailures,
            (int)Volatile.Read(ref closed.SignInFailures),
            (int)Volatile.Read(ref closed.Disconnects))
        {
            AckSamples = acks.Length,
            EntrySuccesses = entrySuccesses,
            EntryP95 = Percentile(entries, 0.95),
            LeaveFailures = new Dictionary<string, int>(closed.LeaveFailures, StringComparer.Ordinal),
            SignOutFailures = (int)Volatile.Read(ref closed.SignOutFailures),
        };
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// The current window, with this recorder registered on it: registered first, then checked still current, so
    /// either <see cref="TakeWindow"/> sees the registration and waits for <see cref="Exit"/>, or this sees the swap and
    /// moves to the new window. Nothing is written to a window after it was read.
    /// </summary>
    private Window Enter()
    {
        while (true)
        {
            Window window = Volatile.Read(ref _window);
            Interlocked.Increment(ref window.Writers);
            if (ReferenceEquals(window, Volatile.Read(ref _window))) return window;

            Interlocked.Decrement(ref window.Writers);
        }
    }

    private static void Exit(Window window) => Interlocked.Decrement(ref window.Writers);

    /// <summary>The nearest-rank percentile of sorted samples; NaN when there are none.</summary>
    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed class Window
    {
        public readonly Reservoir Acks = new(MaxSamples);
        public readonly Reservoir Entries = new(MaxEntrySamples);
        public readonly ConcurrentDictionary<string, int> EntryFailures = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> LeaveFailures = new(StringComparer.Ordinal);
        public long EntrySuccesses;

        /// <summary>Recorders registered on the window (<see cref="Enter"/>); read once they are gone.</summary>
        public int Writers;
        public long SignInFailures;
        public long SignOutFailures;
        public long Disconnects;
    }

    /// <summary>
    /// A uniform sample of at most <c>capacity</c> values, written without a lock. Slots start as NaN, so one a writer
    /// had claimed but not yet filled when the window closed is skipped rather than read as zero.
    /// </summary>
    private sealed class Reservoir
    {
        private readonly double[] _samples;
        private long _count;

        public Reservoir(int capacity)
        {
            _samples = new double[capacity];
            Array.Fill(_samples, double.NaN);
        }

        public void Add(double value)
        {
            long n = Interlocked.Increment(ref _count) - 1;
            if (n < _samples.Length)
            {
                _samples[n] = value;
                return;
            }

            long slot = Random.Shared.NextInt64(n + 1);
            if (slot < _samples.Length) _samples[slot] = value;
        }

        public double[] Sorted()
        {
            int filled = (int)Math.Min(Volatile.Read(ref _count), _samples.Length);
            double[] copy = _samples.AsSpan(0, filled).ToArray().Where(value => !double.IsNaN(value)).ToArray();
            Array.Sort(copy);
            return copy;
        }
    }

    /// <summary>
    /// One bot's unanswered inputs: slot <c>seq % 64</c> holds the seq (0 when free; seqs start at 1) and its send time.
    /// One writer (whoever sends the bot's inputs) and one taker (its connection's read loop).
    /// </summary>
    private sealed class PendingRing
    {
        private readonly uint[] _seqs = new uint[RingSize];
        private readonly long[] _sent = new long[RingSize];

        /// <summary>Records a send; true, with the old send time, when it displaced an input never answered.</summary>
        public bool Put(uint seq, long timestamp, out long overwritten)
        {
            int slot = (int)(seq % RingSize);
            uint previous = Interlocked.Exchange(ref _seqs[slot], 0);
            overwritten = _sent[slot];
            _sent[slot] = timestamp;
            Volatile.Write(ref _seqs[slot], seq);
            return previous != 0;
        }

        /// <summary>Takes back the send time of <paramref name="seq"/>, once; false when it is not held.</summary>
        public bool Take(uint seq, out long sent)
        {
            int slot = (int)(seq % RingSize);
            sent = _sent[slot];
            return Interlocked.CompareExchange(ref _seqs[slot], 0, seq) == seq;
        }

        public void Clear()
        {
            for (int slot = 0; slot < RingSize; slot++) Volatile.Write(ref _seqs[slot], 0);
        }
    }
}

/// <summary>
/// What the bots measured over one window: input-to-ack latency percentiles in milliseconds (NaN with no sample),
/// entry attempts, entry failures by kind, sign-in failures and unexpected disconnects; leave failures by kind and
/// sign-out failures (<see cref="LeaveFailures"/>, <see cref="SignOutFailures"/>).
/// </summary>
/// <param name="EntryAttempts">Attempts that ended in the window, succeeded or failed (a cancelled one is not counted).</param>
public sealed record StepClientValues(
    double AckP50, double AckP95, double AckP99, int EntryAttempts, IReadOnlyDictionary<string, int> EntryFailures,
    int SignInFailures, int Disconnects)
{
    /// <summary>How many latency samples the percentiles are over (at most <see cref="BotMetrics.MaxSamples"/>).</summary>
    public int AckSamples { get; init; }

    /// <summary>Attempts that reached the world.</summary>
    public int EntrySuccesses { get; init; }

    /// <summary>The 95th percentile of entry time, join ticket issued to first ack, in milliseconds; NaN with none.</summary>
    public double EntryP95 { get; init; }

    /// <summary>Failed leaves outside entry attempts (disconnects), by kind; not part of the admission ratio.</summary>
    public IReadOnlyDictionary<string, int> LeaveFailures { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Failed sign-outs of game contexts.</summary>
    public int SignOutFailures { get; init; }
}
