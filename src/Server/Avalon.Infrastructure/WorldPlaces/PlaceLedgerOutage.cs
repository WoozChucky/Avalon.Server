using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>
/// A renewal, an end or a withdrawal the place ledger could not take (#967), logged at Warning by exception type only, at
/// most once per <see cref="Interval"/>, with how many were left out since: during a Redis outage every heartbeat of
/// every player meets it. The session is unaffected: its next renewal that reaches Redis puts the place back, and a place
/// nothing renews lapses at its expiry.
/// </summary>
public sealed class PlaceLedgerOutage(ILogger<PlaceLedgerOutage> logger, TimeProvider clock)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly LogThrottle _throttle = new(clock, Interval);
    private readonly LogThrottle _refusals = new(clock, Interval);

    /// <summary>
    /// Redis refused the expiry of a field of the place ledger (<c>HPEXPIRE</c>: the identity user lacks it, or Redis is
    /// older than 7.4): an ended session's tombstone, a lapsed squatter's lockout, or a redeemed session's pending marker.
    /// The script went on without the field, so ended sessions' late renewals are not refused, and the lockout and the
    /// pending marker are lost: logged at Warning, at most once per <see cref="Interval"/>, with how many were left out.
    /// </summary>
    public void ReportExpiryRefused()
    {
        if (!_refusals.TryEnter(out int suppressed)) return;
        logger.LogWarning("Redis refused the expiry of a place ledger field (HPEXPIRE: no permission for the Redis user, or Redis before 7.4): ended sessions' tombstones, the WORLD_FULL lockout and redeemed sessions' pending markers are not kept until it is allowed. {Suppressed} earlier refusals were not logged",
            suppressed);
    }

    public void Report(string operation, Exception error)
    {
        if (!_throttle.TryEnter(out int suppressed)) return;
        logger.LogWarning("The place ledger could not take a {Operation} ({ExceptionType}); the session goes on, its next renewal puts the place back and a place nothing renews lapses at its expiry. {Suppressed} earlier failures were not logged",
            operation, error.GetType().Name, suppressed);
    }

    /// <summary>Whether <paramref name="error"/> is Redis being unreachable or slow, which a renewal or an end survives.</summary>
    public static bool IsOutage(Exception error) => error is StackExchange.Redis.RedisException or TimeoutException;
}

/// <summary>At most one log line per interval (#967), with how many were left out since; safe across threads.</summary>
public sealed class LogThrottle(TimeProvider clock, TimeSpan interval)
{
    private long _lastLoggedTicks = long.MinValue;
    private int _suppressed;

    /// <summary>True when a line may be written now, with <paramref name="suppressed"/> the lines left out since the last.</summary>
    public bool TryEnter(out int suppressed)
    {
        long now = clock.GetUtcNow().UtcTicks;
        long last = Interlocked.Read(ref _lastLoggedTicks);
        if ((last != long.MinValue && now - last < interval.Ticks) ||
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
