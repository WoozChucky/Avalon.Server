using System.Globalization;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The queue's own limits (#967), in memory as the shared limiter is (identity runs as one replica), in a window of one
/// minute in thirty segments, so a refusal's <c>Retry-After</c> is at most <see cref="Segment"/> (2 s), well inside the
/// shortest gap a waiter has between its check-in interval and its removal (10 s by default):
/// <list type="bullet">
/// <item>a ceiling per source, <see cref="RateLimitingConfig.QueueSourcePermitsPerMinute"/>, on every call before anything
/// is read, so no source makes unbounded Redis reads (<see cref="TryAdmitSource"/>);</item>
/// <item>a source that has presented <see cref="FailedCredentialsPerMinute"/> credentials that were not current is never
/// shut out, so a neighbour behind the same address cannot push honest waiters out of the line: its credential reads go
/// through at most <see cref="ReadsInFlightPastFailures"/> at once, the rest waiting their turn
/// (<see cref="StartCredentialReadAsync"/>);</item>
/// <item>the fair share per account, <see cref="RateLimitingConfig.QueuePermitsPerMinute"/> (<see cref="TryAcquire"/>).</item>
/// </list>
/// An exempt source (<see cref="IExemptSources"/>) is bounded by none of the per-source limits.
/// </summary>
public sealed class QueueRateLimiter : IDisposable
{
    /// <summary>Credentials that were not current one source may present in a minute before its reads are capped. Not a setting.</summary>
    public const int FailedCredentialsPerMinute = 30;

    /// <summary>A source past its failures reads this many credentials at once; the others wait.</summary>
    public const int ReadsInFlightPastFailures = 2;

    /// <summary>How many of such a source's reads may wait; past it a call is refused for one segment.</summary>
    public const int ReadsWaitingPastFailures = 64;

    private const int Segments = 30;

    /// <summary>The soonest any permit comes back, and the longest <c>Retry-After</c> the queue calls answer.</summary>
    public static readonly TimeSpan Segment = ApiRateLimiting.Window / Segments;

    private static readonly RateLimitLease s_unbounded = new FreeLease();

    private readonly bool _enabled;
    private readonly IExemptSources _exempt;
    private readonly PartitionedRateLimiter<string> _sources;
    private readonly PartitionedRateLimiter<string> _accounts;
    private readonly PartitionedRateLimiter<string> _failures;
    private readonly PartitionedRateLimiter<string> _reads = PartitionedRateLimiter.Create<string, string>(
        source => RateLimitPartition.GetConcurrencyLimiter(source, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = ReadsInFlightPastFailures,
            QueueLimit = ReadsWaitingPastFailures,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }), StringComparer.Ordinal);

    public QueueRateLimiter(IOptions<RateLimitingConfig> config, IExemptSources exempt)
    {
        _enabled = config.Value.Enabled;
        _exempt = exempt;
        _sources = Window(config.Value.QueueSourcePermitsPerMinute);
        _accounts = Window(config.Value.QueuePermitsPerMinute);
        _failures = Window(FailedCredentialsPerMinute);
    }

    /// <summary>Null when the source may make another call; otherwise how long until it may.</summary>
    public TimeSpan? TryAdmitSource(HttpContext context)
    {
        if (!_enabled || _exempt.IsExempt(context.Connection.RemoteIpAddress)) return null;
        using RateLimitLease lease = _sources.AttemptAcquire(ApiRateLimiting.SourceOf(context));
        return lease.IsAcquired ? null : RetryAfter(lease);
    }

    /// <summary>
    /// A lease to hold while the call's credential is read, or null when the source is past its failures and has as many
    /// reads waiting as it may (refuse for <see cref="Segment"/>). Under its failures, or exempt, the read is not bounded.
    /// </summary>
    public async ValueTask<RateLimitLease?> StartCredentialReadAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!_enabled || _exempt.IsExempt(context.Connection.RemoteIpAddress)) return s_unbounded;
        string source = ApiRateLimiting.SourceOf(context);
        using (RateLimitLease left = _failures.AttemptAcquire(source, 0))
        {
            if (left.IsAcquired) return s_unbounded;
        }

        RateLimitLease read = await _reads.AcquireAsync(source, 1, cancellationToken);
        if (read.IsAcquired) return read;
        read.Dispose();
        return null;
    }

    /// <summary>Spends one of the source's failures, for a credential that was not current.</summary>
    public void CredentialFailed(HttpContext context)
    {
        if (_enabled && !_exempt.IsExempt(context.Connection.RemoteIpAddress))
            _failures.AttemptAcquire(ApiRateLimiting.SourceOf(context)).Dispose();
    }

    /// <summary>Null when the account may make the call; otherwise how long until it may.</summary>
    public TimeSpan? TryAcquire(long accountId)
    {
        if (!_enabled) return null;
        using RateLimitLease lease = _accounts.AttemptAcquire(accountId.ToString(CultureInfo.InvariantCulture));
        return lease.IsAcquired ? null : RetryAfter(lease);
    }

    public void Dispose()
    {
        _sources.Dispose();
        _accounts.Dispose();
        _failures.Dispose();
        _reads.Dispose();
    }

    /// <summary>The limiter's hint, held to one segment at most: a waiter refused must still check in before its removal.</summary>
    private static TimeSpan RetryAfter(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan hint) && hint > TimeSpan.Zero && hint < Segment ? hint : Segment;

    private static PartitionedRateLimiter<string> Window(int permits) => PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = ApiRateLimiting.Window,
            SegmentsPerWindow = Segments,
            QueueLimit = 0,
            AutoReplenishment = true,
        }), StringComparer.Ordinal);

    /// <summary>The lease of a read no in-flight bound applies to.</summary>
    private sealed class FreeLease : RateLimitLease
    {
        public override bool IsAcquired => true;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
