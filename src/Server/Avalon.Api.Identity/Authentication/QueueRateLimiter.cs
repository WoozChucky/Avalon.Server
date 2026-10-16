using System.Globalization;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The queue's own limit (#967): <see cref="RateLimitingConfig.QueuePermitsPerMinute"/> calls a minute per account, and
/// at most <see cref="FailedCredentialsPerMinute"/> failed credentials a minute per source (an exempt source is not
/// bounded per source). In memory, as the shared limiter is: identity runs as one replica.
/// </summary>
public sealed class QueueRateLimiter : IDisposable
{
    /// <summary>Failed credentials one source may present in a minute before its calls are refused unread. Not a setting.</summary>
    public const int FailedCredentialsPerMinute = 30;

    private readonly bool _enabled;
    private readonly IExemptSources _exempt;
    private readonly PartitionedRateLimiter<string> _accounts;
    private readonly PartitionedRateLimiter<string> _failures;

    public QueueRateLimiter(IOptions<RateLimitingConfig> config, IExemptSources exempt)
    {
        _enabled = config.Value.Enabled;
        _exempt = exempt;
        _accounts = Window(config.Value.QueuePermitsPerMinute);
        _failures = Window(FailedCredentialsPerMinute);
    }

    /// <summary>Whether a call from this source may present a credential: it has failures left, or it is exempt.</summary>
    public bool MayTryCredential(HttpContext context)
    {
        if (!_enabled || _exempt.IsExempt(context.Connection.RemoteIpAddress)) return true;
        using RateLimitLease lease = _failures.AttemptAcquire(ApiRateLimiting.SourceOf(context), 0);
        return lease.IsAcquired;
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
        if (lease.IsAcquired) return null;
        return lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan hint) && hint > TimeSpan.Zero ? hint : Segment;
    }

    /// <summary>The soonest any permit comes back: one segment of the window.</summary>
    public static TimeSpan Segment => ApiRateLimiting.Window / ApiRateLimiting.SegmentsPerWindow;

    public void Dispose()
    {
        _accounts.Dispose();
        _failures.Dispose();
    }

    private static PartitionedRateLimiter<string> Window(int permits) => PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = ApiRateLimiting.Window,
            SegmentsPerWindow = ApiRateLimiting.SegmentsPerWindow,
            QueueLimit = 0,
            AutoReplenishment = true,
        }), StringComparer.Ordinal);
}
