namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// Logs a TLS client certificate the game workload listener refused (#1051), at most once per <see cref="Interval"/>,
/// with how many were not logged since. Kestrel reports the failed handshake at Debug only, which production does not
/// write, so without this a world whose certificate expired or was never assigned is refused in silence. The line names
/// the reason and the leaf's SHA-256 pin, the value <c>Application:GameWorkloads:Servers:n:ClientCertificateSha256</c>
/// holds, and never the certificate itself.
/// </summary>
public sealed class RefusedWorkloadCertificateLog(ILogger<RefusedWorkloadCertificateLog> logger, TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private long _nextLogTicks = long.MinValue;
    private long _suppressed;

    public void Refused(string reason, string pin)
    {
        long now = time.GetUtcNow().UtcTicks;
        long next = Interlocked.Read(ref _nextLogTicks);
        if (now < next || Interlocked.CompareExchange(ref _nextLogTicks, now + Interval.Ticks, next) != next)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        logger.LogWarning(
            "Refused a game workload TLS client certificate {Reason}, SHA-256 {CertificateSha256}; {Suppressed} more since the last warning",
            reason, pin, Interlocked.Exchange(ref _suppressed, 0));
    }
}
