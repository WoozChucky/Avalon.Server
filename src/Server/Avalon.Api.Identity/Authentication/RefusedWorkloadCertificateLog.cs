using System.Collections.Concurrent;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// Logs a TLS client certificate the game workload listener refused (#1051), at most once per <see cref="Interval"/>,
/// with how many were not logged since. Kestrel reports the failed handshake at Debug only, which production does not
/// write, so without this a world whose certificate expired or was never assigned is refused in silence. The line names
/// the reason and the leaf's SHA-256 pin, the value <c>Application:GameWorkloads:Servers:n:ClientCertificateSha256</c>
/// holds, and never the certificate itself.
/// <para>
/// A pin a configured game server holds has a limit of its own, so a stream of unknown certificates (a scan, a
/// misdirected client) cannot take each minute's line and hide a real world's refusal; every unknown pin shares one.
/// The configured pins are a small, fixed set, so their limits are too.
/// </para>
/// </summary>
public sealed class RefusedWorkloadCertificateLog(ILogger<RefusedWorkloadCertificateLog> logger, TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Limit _unknown = new();
    private readonly ConcurrentDictionary<string, Limit> _configured = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A refusal of the certificate whose SHA-256 is <paramref name="pin"/>; <paramref name="configured"/> when a
    /// configured game server holds that pin.
    /// </summary>
    public void Refused(string reason, string pin, bool configured)
    {
        Limit limit = configured ? _configured.GetOrAdd(pin, static _ => new Limit()) : _unknown;
        if (!limit.TryTake(time.GetUtcNow().UtcTicks, out long suppressed))
            return;

        logger.LogWarning(
            "Refused a game workload TLS client certificate {Reason}, SHA-256 {CertificateSha256}; {Suppressed} more since the last warning",
            reason, pin, suppressed);
    }

    private sealed class Limit
    {
        private long _nextLogTicks = long.MinValue;
        private long _suppressed;

        public bool TryTake(long now, out long suppressed)
        {
            long next = Interlocked.Read(ref _nextLogTicks);
            if (now < next || Interlocked.CompareExchange(ref _nextLogTicks, now + Interval.Ticks, next) != next)
            {
                Interlocked.Increment(ref _suppressed);
                suppressed = 0;
                return false;
            }

            suppressed = Interlocked.Exchange(ref _suppressed, 0);
            return true;
        }
    }
}
