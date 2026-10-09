using System.Diagnostics;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// A breaker over one kind of call a ramp makes per bot (a sign-out, a leave) that can hang on a peer that stopped
/// answering. Once <see cref="Threshold"/> calls in a row fail that way, it trips: calls are skipped at once and
/// counted (<see cref="Skipped"/>), except one probe let through every <see cref="ProbeInterval"/>. A call answered,
/// a probe or one still in flight from before the trip, resets the count and closes the breaker; a failed probe keeps
/// it tripped. So a dead peer costs one window of calls rather than one timeout per bot, and a short outage costs no
/// more than itself. Thread-safe.
/// </summary>
public sealed class Breaker
{
    /// <summary>Failed calls in a row that trip the breaker: one window of the ramp's 32 leaves at once.</summary>
    public const int Threshold = 32;

    /// <summary>While tripped, one call is let through as a probe this often.</summary>
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromSeconds(5);

    private static readonly long s_probeTicks = (long)(ProbeInterval.TotalSeconds * Stopwatch.Frequency);

    private int _failuresInARow;
    private int _skipped;
    private volatile bool _tripped;

    /// <summary>The <see cref="Stopwatch"/> timestamp from which the next probe may go.</summary>
    private long _nextProbe;

    /// <summary>Calls skipped while the breaker was tripped.</summary>
    public int Skipped => Volatile.Read(ref _skipped);

    /// <summary>
    /// Whether to make the call: always while closed; while tripped, only for the one caller that takes a due probe.
    /// A call not made is counted as skipped.
    /// </summary>
    public bool TryEnter()
    {
        if (!_tripped) return true;

        long now = Stopwatch.GetTimestamp();
        long next = Interlocked.Read(ref _nextProbe);
        if (now >= next && Interlocked.CompareExchange(ref _nextProbe, now + s_probeTicks, next) == next) return true;

        Interlocked.Increment(ref _skipped);
        return false;
    }

    /// <summary>The call was answered: the count restarts and the breaker closes.</summary>
    public void Succeeded()
    {
        Interlocked.Exchange(ref _failuresInARow, 0);
        _tripped = false;
    }

    /// <summary>
    /// The call failed the way a dead peer fails (no answer in time); the <see cref="Threshold"/>th in a row trips the
    /// breaker.
    /// </summary>
    public void Failed()
    {
        if (Interlocked.Increment(ref _failuresInARow) < Threshold || _tripped) return;

        // The first probe goes one interval after the trip.
        Interlocked.Exchange(ref _nextProbe, Stopwatch.GetTimestamp() + s_probeTicks);
        _tripped = true;
    }
}
