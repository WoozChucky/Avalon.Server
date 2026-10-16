using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>
/// A renewal or an end the place ledger could not take (#967), logged at Warning by exception type only, at most once per
/// <see cref="Interval"/>, with how many were left out since: during a Redis outage every heartbeat of every player
/// meets it. The session is unaffected, and its next renewal that reaches Redis puts the place back.
/// </summary>
public sealed class PlaceLedgerOutage(ILogger<PlaceLedgerOutage> logger, TimeProvider clock)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private long _lastLoggedTicks = long.MinValue;
    private int _suppressed;

    public void Report(string operation, Exception error)
    {
        long now = clock.GetUtcNow().UtcTicks;
        long last = Interlocked.Read(ref _lastLoggedTicks);
        if ((last != long.MinValue && now - last < Interval.Ticks) ||
            Interlocked.CompareExchange(ref _lastLoggedTicks, now, last) != last)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        logger.LogWarning("The place ledger could not take a {Operation} ({ExceptionType}); the session goes on and its next renewal puts the place back. {Suppressed} earlier failures were not logged",
            operation, error.GetType().Name, Interlocked.Exchange(ref _suppressed, 0));
    }

    /// <summary>Whether <paramref name="error"/> is Redis being unreachable or slow, which a renewal or an end survives.</summary>
    public static bool IsOutage(Exception error) => error is StackExchange.Redis.RedisException or TimeoutException;
}
