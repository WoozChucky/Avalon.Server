using System.Collections.Concurrent;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// Logs a TLS client certificate the game workload listener refused (#1051), at most once per <see cref="Interval"/>,
/// with how many were not logged since. Kestrel reports the failed handshake at Debug only, which production does not
/// write, so without this a world whose certificate expired or was never assigned is refused in silence. The line names
/// the reason, the leaf's subject and the server ID it names, and never the certificate itself.
/// <para>
/// A server ID a configured game server has gets a limit of its own, so a stream of unknown certificates (a scan, a
/// misdirected client) cannot take each minute's line and hide a real world's refusal; every other certificate shares
/// one. The configured IDs are a small, fixed set, so their limits are too.
/// </para>
/// </summary>
public sealed class RefusedWorkloadCertificateLog(ILogger<RefusedWorkloadCertificateLog> logger, TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Limit _unknown = new();
    private readonly ConcurrentDictionary<string, Limit> _configured = new(StringComparer.Ordinal);

    /// <summary>
    /// A refusal of the certificate with <paramref name="subject"/> naming <paramref name="serverId"/> (null when it names
    /// none); <paramref name="assigned"/> when a configured game server has that ID.
    /// </summary>
    public void Refused(string reason, string subject, string? serverId, bool assigned)
    {
        Limit limit = assigned && serverId is not null ? _configured.GetOrAdd(serverId, static _ => new Limit()) : _unknown;
        if (!limit.TryTake(time.GetUtcNow().UtcTicks, out long suppressed))
            return;

        logger.LogWarning(
            "Refused a game workload TLS client certificate {Reason}, subject {Subject}, server ID {ServerId}; {Suppressed} more since the last warning",
            reason, Printable(subject), serverId is null ? "none" : Printable(serverId), suppressed);
    }

    /// <summary>The peer chose these strings: control characters go and the length is bounded before they reach a log line.</summary>
    private static string Printable(string value)
    {
        string clean = string.Concat(value.Select(c => char.IsControl(c) ? '?' : c));
        return clean.Length <= 128 ? clean : clean[..128];
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
