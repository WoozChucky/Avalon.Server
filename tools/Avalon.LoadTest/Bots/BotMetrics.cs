using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// What the bots measure on the client side, by window: input-to-ack latency, entry time (ticket to first ack), entry
/// attempts and failures by kind, leave and sign-in failures by kind, sign-out failures and unexpected disconnects; and
/// the fighters' trips: forest entry time (portal request to transition), trips completed, casts sent and refused by
/// reason, kills seen, their own deaths and failed steps by kind, and their parties formed and failed by reason.
/// <see cref="TakeWindow"/> closes the current window and opens the next. Every recording method is safe from any
/// thread and takes no lock of its own: counters are interlocked, and a count by kind is a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> update, which briefly locks one of its stripes.
/// </summary>
/// <remarks>
/// <para>
/// The counted events (entries, sign-in, sign-out and leave failures, disconnects, every fighter count) each land in
/// exactly one window,
/// whole: a recorder registers on the window it read (<see cref="Enter"/>) and moves to the new one if that was
/// swapped meanwhile, and <see cref="TakeWindow"/> waits for the recorders still registered on the window it closed
/// before reading it. Entry attempts are not a counter of their own but successes plus failures, so the two always
/// agree. The bots that tried to enter and those that got in (admission's sets, by bot index) are noted with each
/// outcome inside the gate too, so a bot's success always lands in the same window as its being counted as tried.
/// Ack samples are not gated: they feed percentiles, and a sample landing in the closed window just after it was read
/// is one sample fewer, not a miscount. An entry time is written with its success, inside the gate.
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
    private long _admittedSealed;
    private long _admittedPlain;

    /// <summary>
    /// Notes input <paramref name="seq"/> of <paramref name="bot"/> sent at <paramref name="timestamp"/>
    /// (<see cref="Stopwatch.GetTimestamp"/>). Only the input driver's inputs are noted: an entry's probes, repeated
    /// until one is answered, are not latency, and more than 64 of them would displace each other into the samples.
    /// </summary>
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
    /// Forgets <paramref name="bot"/>'s unanswered inputs: those sent for a character it has since left (a Change
    /// Character), or on a connection now closed, are never answered and are not latency.
    /// </summary>
    public void ForgetPending(int bot)
    {
        if (_pending.TryGetValue(bot, out PendingRing? ring)) ring.Clear();
    }

    /// <summary>
    /// An attempt of <paramref name="bot"/> to reach the world (an entry, or a change of character) reached it,
    /// <paramref name="ticketToFirstAck"/> after its join ticket was issued. An attempt is counted with its outcome, in
    /// the window the outcome lands in, so one that ends neither way (cancelled) counts nowhere. The bot is noted as
    /// having tried and got in within that window, whatever else it did there.
    /// </summary>
    public void EntrySucceeded(int bot, TimeSpan ticketToFirstAck)
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref window.EntrySuccesses);
            window.Entries.Add(ticketToFirstAck.TotalMilliseconds);
            window.BotsTried.TryAdd(bot, 0);
            window.BotsGotIn.TryAdd(bot, 0);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>
    /// An attempt of <paramref name="bot"/> to reach the world failed, counted as an attempt too;
    /// <paramref name="kind"/> names the step and what went wrong (<c>join:ACTIVE_GAME_SESSION</c>,
    /// <c>spawn:timeout</c>, ...). The bot is noted as having tried within the window, once however many of its
    /// attempts fail there.
    /// </summary>
    public void EntryFailed(int bot, string kind)
    {
        Window window = Enter();
        try
        {
            window.EntryFailures.AddOrUpdate(kind, 1, static (_, n) => n + 1);
            window.BotsTried.TryAdd(bot, 0);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>
    /// A sign-in or a context refresh failed, identity's side and not an entry: no attempt goes with it, so it stays out
    /// of the admission ratio. <paramref name="kind"/> names the call and what went wrong (<c>sign-in:authenticate</c>,
    /// <c>sign-in-again:redeem</c>, <c>refresh:no-reply</c>, <c>refresh:CONTEXT_REVOKED</c>, ...).
    /// </summary>
    public void SignInFailed(string kind)
    {
        Window window = Enter();
        try
        {
            window.SignInFailures.AddOrUpdate(kind, 1, static (_, n) => n + 1);
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

    /// <summary>An admission, by the mode the world's reply named (#875).</summary>
    public void Admitted(bool packetEncryption) =>
        Interlocked.Increment(ref packetEncryption ? ref _admittedSealed : ref _admittedPlain);

    /// <summary>Every admission so far, by mode: sealed (Network:PacketEncryption on) and plain (TLS alone).</summary>
    public (long Sealed, long Plain) Admissions => (Interlocked.Read(ref _admittedSealed), Interlocked.Read(ref _admittedPlain));

    /// <summary>
    /// The packet encryption <paramref name="admissions"/> name (<see cref="Admissions"/>): on, off, or both with their
    /// counts when the world changed mode between them. The ramp report's header and <c>check</c> print it alike.
    /// </summary>
    public static string EncryptionText((long Sealed, long Plain) admissions) => admissions switch
    {
        (Sealed: 0, Plain: 0) => "no bot was admitted",
        (Sealed: > 0, Plain: 0) => "on (sealed inside TLS, Network:PacketEncryption)",
        (Sealed: 0, Plain: > 0) => "off (TLS alone)",
        var (sealedCount, plainCount) => FormattableString.Invariant(
            $"mixed: {sealedCount} admissions sealed, {plainCount} plain (the world changed mode in between)"),
    };

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

    /// <summary>A fighter's request to enter the forest was answered with the forest, <paramref name="portalToTransition"/> after it was sent.</summary>
    public void ForestEntered(TimeSpan portalToTransition)
    {
        Window window = Enter();
        try
        {
            window.ForestEntries.Add(portalToTransition.TotalMilliseconds);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>A fighter walked out of the forest into town.</summary>
    public void ForestTripCompleted() => Count(static window => ref window.ForestTrips);

    /// <summary>A fighter sent a cast.</summary>
    public void CastSent() => Count(static window => ref window.CastsSent);

    /// <summary>The world refused a fighter's cast; <paramref name="reason"/> names its <c>CastRejectReason</c>.</summary>
    public void CastRefused(string reason) => CountKind(static window => window.CastRefusals, reason);

    /// <summary>A creature a fighter cast at was seen dead.</summary>
    public void KillSeen() => Count(static window => ref window.Kills);

    /// <summary>A fighter's character died.</summary>
    public void OwnDeath() => Count(static window => ref window.OwnDeaths);

    /// <summary>
    /// A step of a fighter's trip failed; <paramref name="kind"/> names it (<c>forest:portal-timeout</c>,
    /// <c>forest:enter:&lt;result&gt;</c>, <c>forest:exit-timeout:trail</c>, ...).
    /// </summary>
    public void FighterFailed(string kind) => CountKind(static window => window.FighterFailures, kind);

    /// <summary>A party of fighters formed: its leader's roster listed every member.</summary>
    public void PartyFormed() => Count(static window => ref window.PartiesFormed);

    /// <summary>
    /// A party of fighters failed to form twice, or fell apart once formed, and its members fight solo; counted once per
    /// party. <paramref name="reason"/> names why (<c>party:timeout</c>, <c>party:invite:&lt;result&gt;</c>,
    /// <c>party:fell-apart</c>, ...).
    /// </summary>
    public void PartyFormFailed(string reason) => CountKind(static window => window.PartyFormFailures, reason);

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
        double[] forestEntries = closed.ForestEntries.Sorted();
        var entryFailures = new Dictionary<string, int>(closed.EntryFailures, StringComparer.Ordinal);
        int entrySuccesses = (int)Volatile.Read(ref closed.EntrySuccesses);
        return new StepClientValues(
            Percentile(acks, 0.50), Percentile(acks, 0.95), Percentile(acks, 0.99),
            entrySuccesses + entryFailures.Values.Sum(),
            entryFailures,
            new Dictionary<string, int>(closed.SignInFailures, StringComparer.Ordinal),
            (int)Volatile.Read(ref closed.Disconnects))
        {
            AckSamples = acks.Length,
            EntrySuccesses = entrySuccesses,
            EntryP95 = Percentile(entries, 0.95),
            LeaveFailures = new Dictionary<string, int>(closed.LeaveFailures, StringComparer.Ordinal),
            SignOutFailures = (int)Volatile.Read(ref closed.SignOutFailures),
            TriedBots = closed.BotsTried.Keys.ToHashSet(),
            GotInBots = closed.BotsGotIn.Keys.ToHashSet(),
            ForestEntries = (int)closed.ForestEntries.Count,
            ForestEntryP50 = Percentile(forestEntries, 0.50),
            ForestEntryP95 = Percentile(forestEntries, 0.95),
            ForestTrips = (int)Volatile.Read(ref closed.ForestTrips),
            CastsSent = (int)Volatile.Read(ref closed.CastsSent),
            CastsRefused = new Dictionary<string, int>(closed.CastRefusals, StringComparer.Ordinal),
            Kills = (int)Volatile.Read(ref closed.Kills),
            OwnDeaths = (int)Volatile.Read(ref closed.OwnDeaths),
            FighterFailures = new Dictionary<string, int>(closed.FighterFailures, StringComparer.Ordinal),
            PartiesFormed = (int)Volatile.Read(ref closed.PartiesFormed),
            PartyFormFailures = new Dictionary<string, int>(closed.PartyFormFailures, StringComparer.Ordinal),
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

    /// <summary>Adds one to the current window's counter <paramref name="counter"/> picks, inside the gate.</summary>
    private void Count(CounterOf counter)
    {
        Window window = Enter();
        try
        {
            Interlocked.Increment(ref counter(window));
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>Adds one to <paramref name="kind"/> in the current window's count by kind <paramref name="counts"/> picks, inside the gate.</summary>
    private void CountKind(Func<Window, ConcurrentDictionary<string, int>> counts, string kind)
    {
        Window window = Enter();
        try
        {
            counts(window).AddOrUpdate(kind, 1, static (_, n) => n + 1);
        }
        finally
        {
            Exit(window);
        }
    }

    /// <summary>The nearest-rank percentile of sorted samples; NaN when there are none.</summary>
    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed class Window
    {
        public readonly Reservoir Acks = new(MaxSamples);
        public readonly Reservoir Entries = new(MaxEntrySamples);
        public readonly ConcurrentDictionary<string, int> EntryFailures = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> LeaveFailures = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> SignInFailures = new(StringComparer.Ordinal);

        /// <summary>The bots with an entry attempt that ended in the window, by index.</summary>
        public readonly ConcurrentDictionary<int, byte> BotsTried = new();

        /// <summary>The bots with an entry attempt that succeeded in the window: a subset of <see cref="BotsTried"/>.</summary>
        public readonly ConcurrentDictionary<int, byte> BotsGotIn = new();
        public long EntrySuccesses;

        /// <summary>Recorders registered on the window (<see cref="Enter"/>); read once they are gone.</summary>
        public int Writers;
        public long SignOutFailures;
        public long Disconnects;

        /// <summary>The fighters' forest entry times, portal request to transition, in milliseconds.</summary>
        public readonly Reservoir ForestEntries = new(MaxEntrySamples);
        public readonly ConcurrentDictionary<string, int> CastRefusals = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> FighterFailures = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> PartyFormFailures = new(StringComparer.Ordinal);
        public long ForestTrips;
        public long CastsSent;
        public long Kills;
        public long OwnDeaths;
        public long PartiesFormed;
    }

    /// <summary>Picks one of a window's counters, by reference.</summary>
    private delegate ref long CounterOf(Window window);

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

        /// <summary>How many values were added, kept or not.</summary>
        public long Count => Volatile.Read(ref _count);

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
/// entry attempts, entry failures by kind, sign-in failures by kind and unexpected disconnects; leave failures by kind
/// and sign-out failures (<see cref="LeaveFailures"/>, <see cref="SignOutFailures"/>).
/// </summary>
/// <param name="EntryAttempts">Attempts that ended in the window, succeeded or failed (a cancelled one is not counted).</param>
/// <param name="SignInFailures">
/// Failed sign-ins and context refreshes by kind (<see cref="BotMetrics.SignInFailed"/>): identity's side, not part of
/// the admission ratio.
/// </param>
public sealed record StepClientValues(
    double AckP50, double AckP95, double AckP99, int EntryAttempts, IReadOnlyDictionary<string, int> EntryFailures,
    IReadOnlyDictionary<string, int> SignInFailures, int Disconnects)
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

    /// <summary>The bots, by index, with an entry attempt that ended in the window; the report gives their count.</summary>
    [JsonIgnore]
    public IReadOnlySet<int> TriedBots { get; init; } = new HashSet<int>();

    /// <summary>The bots, by index, with an entry attempt that succeeded in the window.</summary>
    [JsonIgnore]
    public IReadOnlySet<int> GotInBots { get; init; } = new HashSet<int>();

    /// <summary>Fighters' requests to enter the forest answered with the forest.</summary>
    public int ForestEntries { get; init; }

    /// <summary>The median forest entry time, a fighter's portal request to its transition, in milliseconds; NaN with none.</summary>
    public double ForestEntryP50 { get; init; } = double.NaN;

    /// <summary>The 95th percentile of forest entry time in milliseconds; NaN with none.</summary>
    public double ForestEntryP95 { get; init; } = double.NaN;

    /// <summary>Fighters' trips completed: out of the forest into town.</summary>
    public int ForestTrips { get; init; }

    /// <summary>Casts the fighters sent.</summary>
    public int CastsSent { get; init; }

    /// <summary>Casts the world refused, by <c>CastRejectReason</c>.</summary>
    public IReadOnlyDictionary<string, int> CastsRefused { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Creatures a fighter cast at seen dead afterwards; a creature several fighters cast at counts for each.</summary>
    public int Kills { get; init; }

    /// <summary>Deaths of the fighters' characters.</summary>
    public int OwnDeaths { get; init; }

    /// <summary>Failed steps of fighters' trips, by kind (<see cref="BotMetrics.FighterFailed"/>).</summary>
    public IReadOnlyDictionary<string, int> FighterFailures { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Parties of fighters formed (<see cref="BotMetrics.PartyFormed"/>).</summary>
    public int PartiesFormed { get; init; }

    /// <summary>
    /// Parties of fighters that failed to form or fell apart, their members fighting solo, by reason
    /// (<see cref="BotMetrics.PartyFormFailed"/>).
    /// </summary>
    public IReadOnlyDictionary<string, int> PartyFormFailures { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Bots that tried to enter in the window (an attempt of theirs ended there), each once.</summary>
    public int BotsTried => TriedBots.Count;

    /// <summary>
    /// Bots that tried to enter in the window and never got in there: every attempt of theirs that ended in it failed.
    /// A bot whose failure and later success both fall in the window got in.
    /// </summary>
    public int BotsFailing => TriedBots.Count(bot => !GotInBots.Contains(bot));
}
