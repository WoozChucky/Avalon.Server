namespace Avalon.World.GameAuth;

/// <summary>
/// Lets one admission failure of a kind through to the log at most once per <see cref="Interval" />, and counts the
/// ones it holds back (#1050): a reconnect storm that overflows the admission work queue refuses hundreds of
/// admissions a second, each of which was an Error with its stack. Thread-safe: redemptions fail on the thread pool.
/// </summary>
internal sealed class AdmissionFailureLog
{
    /// <summary>The least time between two lines of one kind, the world entry check's.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private long _lastLoggedTicks = long.MinValue;
    private int _suppressed;

    /// <summary>
    /// Whether this failure is the one to log; when it is, <paramref name="suppressed" /> is how many were held back
    /// since the last one logged.
    /// </summary>
    public bool TryTake(TimeProvider time, out int suppressed)
    {
        long now = time.GetUtcNow().UtcTicks;
        long last = Interlocked.Read(ref _lastLoggedTicks);
        if ((last != long.MinValue && now - last < Interval.Ticks) ||
            Interlocked.CompareExchange(ref _lastLoggedTicks, now, last) != last)
        {
            Interlocked.Increment(ref _suppressed);
            suppressed = 0;
            return false;
        }

        suppressed = Interlocked.Exchange(ref _suppressed, 0);
        return true;
    }
}
