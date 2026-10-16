using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The queue's own limits (#967), in memory as the shared limiter is (identity runs as one replica), in a window of one
/// minute in thirty segments, so a refusal's <c>Retry-After</c> is at most <see cref="Segment"/> (2 s), well inside the
/// shortest gap a waiter has between its check-in interval and its removal (10 s by default). No limit is shared by the
/// callers behind one address, so a neighbour can never spend an honest waiter's budget:
/// <list type="bullet">
/// <item>before anything is read, each credential (by its SHA-256) has <see cref="RateLimitingConfig.QueuePermitsPerMinute"/>
/// calls a minute (<see cref="TryAcquireCredential"/>), so one credential cannot make unbounded Redis reads;</item>
/// <item>a source that has presented <see cref="FailedCredentialsPerMinute"/> credentials that were not current reads
/// them at most <see cref="ReadsInFlightPastFailures"/> at a time, the others waiting their turn
/// (<see cref="StartCredentialReadAsync"/>): slowed, never shut out, and its junk credentials bounded by that gate;</item>
/// <item>after the read, each account has <see cref="RateLimitingConfig.QueuePermitsPerMinute"/> calls a minute, its fair
/// share across the credentials it rotates through (<see cref="TryAcquire"/>).</item>
/// </list>
/// An exempt source (<see cref="IExemptSources"/>) is not gated for its failures. Every partition is dropped by the
/// limiter once it has been idle (its window replenished), as the shared limiter's are.
/// </summary>
public sealed class QueueRateLimiter : IDisposable
{
    /// <summary>Credentials that were not current one source may present in a minute before its reads are gated. Not a setting.</summary>
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
    private readonly PartitionedRateLimiter<string> _credentials;
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
        _credentials = Window(config.Value.QueuePermitsPerMinute);
        _accounts = Window(config.Value.QueuePermitsPerMinute);
        _failures = Window(FailedCredentialsPerMinute);
    }

    /// <summary>
    /// A lease to hold while the call's credential is read, or null when the source is past its failures and has as many
    /// reads waiting as it may (refuse for <see cref="Segment"/>). Under its failures, or exempt, the read is not gated.
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

    /// <summary>
    /// Null when a call presenting <paramref name="credential"/> may read it; otherwise how long until it may. Keyed by the
    /// credential's SHA-256, never the credential itself, and shared with nobody else's calls.
    /// </summary>
    public TimeSpan? TryAcquireCredential(string credential)
    {
        if (!_enabled) return null;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        using RateLimitLease lease = _credentials.AttemptAcquire(key);
        return lease.IsAcquired ? null : RetryAfter(lease);
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
        _credentials.Dispose();
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

    /// <summary>The lease of a read no gate applies to.</summary>
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
