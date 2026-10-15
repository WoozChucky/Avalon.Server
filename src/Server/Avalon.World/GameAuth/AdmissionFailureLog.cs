namespace Avalon.World.GameAuth;

/// <summary>
/// Lets one admission failure of a kind through to the log at most once per <see cref="Interval" />, and counts the
/// ones it holds back (#1050): a reconnect storm that overflows the admission work queue refuses hundreds of
/// admissions a second, each of which was an Error with its stack. Timed on the monotonic clock, so a wall clock set
/// back cannot mute a line for as long as the step. Thread-safe: admissions fail on the receive path and the thread pool.
/// </summary>
internal sealed class AdmissionFailureLog
{
    /// <summary>The least time between two lines of one kind, the world entry check's.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    // The monotonic timestamp of the last line; meaningless until _hasLogged is set, since any value is a timestamp.
    private long _lastLogged;
    private int _hasLogged;
    private int _suppressed;

    /// <summary>
    /// Whether this failure is the one to log; when it is, <paramref name="suppressed" /> is how many were held back
    /// since the last one logged.
    /// </summary>
    public bool TryTake(TimeProvider time, out int suppressed)
    {
        long now = time.GetTimestamp();
        long last = Interlocked.Read(ref _lastLogged);
        bool due = Volatile.Read(ref _hasLogged) == 0 || time.GetElapsedTime(last, now) >= Interval;
        if (!due || Interlocked.CompareExchange(ref _lastLogged, now, last) != last)
        {
            Interlocked.Increment(ref _suppressed);
            suppressed = 0;
            return false;
        }

        Volatile.Write(ref _hasLogged, 1);
        suppressed = Interlocked.Exchange(ref _suppressed, 0);
        return true;
    }
}
