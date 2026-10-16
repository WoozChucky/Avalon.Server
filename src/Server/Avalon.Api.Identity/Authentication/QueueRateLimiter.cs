using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>A queue call's admission: a lease to hold while its credential is read, or how long until it may ask again.</summary>
public readonly record struct QueueRead(RateLimitLease? Lease, TimeSpan? RetryAfter);

/// <summary>
/// The queue's own limits (#967), in memory as the shared limiter is (identity runs as one replica), in a window of one
/// minute in thirty segments, so a refusal's <c>Retry-After</c> is at most <see cref="Segment"/> (2 s), well inside the
/// shortest gap a waiter has between its check-in interval and its removal (10 s by default).
/// <list type="bullet">
/// <item>A credential identity knows to be current (it issued it, at a sign-in, a link or a refresh, or read it current at
/// a check-in, until it expires) is limited on its own, <see cref="RateLimitingConfig.QueuePermitsPerMinute"/> calls a
/// minute, before it is read, and goes through no gate of its source: what other callers behind its address do never
/// slows or refuses it. It is still read every call, so a revocation answers at once.</item>
/// <item>Any other credential is a stranger's: its reads from one source go at most <see cref="ReadsInFlight"/> at a time,
/// the others waiting their turn (<see cref="ReadsWaiting"/> at most), and once the source has presented
/// <see cref="FailedCredentialsPerMinute"/> credentials that were not current, at most
/// <see cref="RateLimitingConfig.QueueUnknownReadsPerMinute"/> a minute; and at most
/// <see cref="RateLimitingConfig.QueueStrangerReadsInFlight"/> strangers' reads in flight in the whole process, whatever
/// their sources. Nothing is kept per stranger's credential, so a
/// flood of junk costs only its source's partitions. Such a source can delay, and refuse, a waiter identity has not seen
/// yet behind the same address (after an identity restart, say), never one it knows.</item>
/// <item>After the read, each account has <see cref="RateLimitingConfig.QueuePermitsPerMinute"/> calls a minute, its fair
/// share across the credentials it rotates through.</item>
/// </list>
/// The known credentials are a bounded cache (<see cref="KnownCredentialCapacity"/>, by the first 16 bytes of each one's
/// SHA-256), a credential read current kept before one only issued, and the credential a refresh or a sign-in replaces
/// forgotten at once; every limiter partition is dropped once idle, as the shared limiter's are. An exempt source
/// (<see cref="IExemptSources"/>) is not gated.
/// </summary>
public sealed class QueueRateLimiter : IGameCredentialIssued, IDisposable
{
    /// <summary>Credentials that were not current one source may present in a minute before its strangers' reads are rated.</summary>
    public const int FailedCredentialsPerMinute = 30;

    /// <summary>A source's reads of credentials identity does not know, at once; the others wait.</summary>
    public const int ReadsInFlight = 2;

    /// <summary>How many of those reads may wait; past it a call is refused for one segment.</summary>
    public const int ReadsWaiting = 64;

    /// <summary>The most credentials known at once; past it a credential is not remembered (it stays a stranger's).</summary>
    public const int KnownCredentialCapacity = 100_000;

    private const int Segments = 30;

    /// <summary>The soonest any permit comes back, and the longest <c>Retry-After</c> the queue calls answer.</summary>
    public static readonly TimeSpan Segment = ApiRateLimiting.Window / Segments;

    private static readonly RateLimitLease s_free = new FreeLease();

    private readonly bool _enabled;
    private readonly IExemptSources _exempt;
    private readonly TimeProvider _clock;
    private readonly MemoryCache _known;
    private readonly PartitionedRateLimiter<UInt128> _credentials;
    private readonly PartitionedRateLimiter<string> _accounts;
    private readonly PartitionedRateLimiter<string> _failures;
    private readonly PartitionedRateLimiter<string> _strangers;
    private readonly ConcurrencyLimiter _allStrangers;
    private readonly PartitionedRateLimiter<string> _reads = PartitionedRateLimiter.Create<string, string>(
        source => RateLimitPartition.GetConcurrencyLimiter(source, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = ReadsInFlight,
            QueueLimit = ReadsWaiting,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }), StringComparer.Ordinal);

    public QueueRateLimiter(IOptions<RateLimitingConfig> config, IExemptSources exempt, TimeProvider clock)
        : this(config, exempt, clock, KnownCredentialCapacity)
    {
    }

    /// <param name="knownCapacity">The bound of the known credentials (<see cref="KnownCredentialCapacity"/> outside tests).</param>
    public QueueRateLimiter(IOptions<RateLimitingConfig> config, IExemptSources exempt, TimeProvider clock, int knownCapacity)
    {
        _enabled = config.Value.Enabled;
        _clock = clock;
        _exempt = exempt;
        _known = new MemoryCache(new MemoryCacheOptions { SizeLimit = knownCapacity });
        _credentials = PartitionedRateLimiter.Create<UInt128, UInt128>(key => RateLimitPartition.GetSlidingWindowLimiter(key,
            _ => Window(config.Value.QueuePermitsPerMinute)));
        _accounts = Partitioned(config.Value.QueuePermitsPerMinute);
        _failures = Partitioned(FailedCredentialsPerMinute);
        _strangers = Partitioned(config.Value.QueueUnknownReadsPerMinute);
        _allStrangers = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = config.Value.QueueStrangerReadsInFlight, QueueLimit = 0 });
    }

    /// <summary>How many credentials are known now (at most the capacity).</summary>
    public int KnownCredentials => _known.Count;

    /// <summary>
    /// Admits a call presenting <paramref name="credential"/> (already of a credential's shape) to have it read: a known
    /// credential against its own budget, any other through its source's gate. Hold the lease for the read only.
    /// </summary>
    public async ValueTask<QueueRead> StartReadAsync(HttpContext context, string credential, CancellationToken cancellationToken)
    {
        if (!_enabled) return new(s_free, null);
        UInt128 key = Key(credential);
        if (_known.TryGetValue(key, out _))
        {
            using RateLimitLease own = _credentials.AttemptAcquire(key);
            return own.IsAcquired ? new(s_free, null) : new(null, RetryAfter(own));
        }

        if (_exempt.IsExempt(context.Connection.RemoteIpAddress)) return new(s_free, null);
        string source = ApiRateLimiting.SourceOf(context);
        // Every stranger's read is counted in flight, so a burst cannot all pass before its failures are recorded.
        RateLimitLease perSource = await _reads.AcquireAsync(source, 1, cancellationToken);
        if (!perSource.IsAcquired)
        {
            perSource.Dispose();
            return new(null, Segment);
        }

        // And at most so many strangers' reads in flight in the whole process, whatever their sources.
        RateLimitLease everywhere = _allStrangers.AttemptAcquire();
        if (!everywhere.IsAcquired)
        {
            everywhere.Dispose();
            perSource.Dispose();
            return new(null, Segment);
        }

        RateLimitLease read = new PairLease(perSource, everywhere);

        using (RateLimitLease left = _failures.AttemptAcquire(source, 0))
        {
            if (left.IsAcquired) return new(read, null);
        }

        using RateLimitLease rated = _strangers.AttemptAcquire(source);
        if (rated.IsAcquired) return new(read, null);
        read.Dispose();
        return new(null, RetryAfter(rated));
    }

    /// <summary>The credential was read current: known until it expires, kept before credentials only issued.</summary>
    public void CredentialCurrent(string credential, DateTime expiresAtUtc)
    {
        if (_enabled && !_known.TryGetValue(Key(credential), out _)) Remember(Key(credential), expiresAtUtc, CacheItemPriority.High);
    }

    /// <summary>
    /// A credential identity committed (<see cref="IGameCredentialIssued"/>): known from its first check-in, and the one it
    /// replaces forgotten. Never throws.
    /// </summary>
    public void Issued(string credential, DateTime expiresAtUtc, string? replacedDigest)
    {
        if (!_enabled) return;
        try
        {
            if (replacedDigest is { Length: >= 32 })
                _known.Remove(BinaryPrimitives.ReadUInt128LittleEndian(Convert.FromHexString(replacedDigest.AsSpan(0, 32))));
            Remember(Key(credential), expiresAtUtc, CacheItemPriority.Normal);
        }
        catch (Exception error) when (error is ObjectDisposedException or FormatException)
        {
            // A cache disposed at shutdown, or a digest that does not read: the credential stays a stranger's.
        }
    }

    /// <summary>The credential was not current: forgotten, and one of its source's failures spent.</summary>
    public void CredentialFailed(HttpContext context, string credential)
    {
        if (!_enabled) return;
        _known.Remove(Key(credential));
        if (!_exempt.IsExempt(context.Connection.RemoteIpAddress))
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
        _known.Dispose();
        _credentials.Dispose();
        _accounts.Dispose();
        _failures.Dispose();
        _strangers.Dispose();
        _reads.Dispose();
        _allStrangers.Dispose();
    }

    private void Remember(UInt128 key, DateTime expiresAtUtc, CacheItemPriority priority)
    {
        // Relative to identity's own clock, which the credential's expiry is on; the cache counts it on its own.
        TimeSpan left = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc) - _clock.GetUtcNow().UtcDateTime;
        if (left <= TimeSpan.Zero) return;
        _known.Set(key, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = left, Priority = priority });
    }

    /// <summary>The first 16 bytes of the credential's SHA-256: the credential itself is never kept.</summary>
    private static UInt128 Key(string credential) =>
        BinaryPrimitives.ReadUInt128LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    /// <summary>The limiter's hint, held to one segment at most: a waiter refused must still check in before its removal.</summary>
    private static TimeSpan RetryAfter(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan hint) && hint > TimeSpan.Zero && hint < Segment ? hint : Segment;

    private static SlidingWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = ApiRateLimiting.Window,
        SegmentsPerWindow = Segments,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static PartitionedRateLimiter<string> Partitioned(int permits) => PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetSlidingWindowLimiter(key, _ => Window(permits)), StringComparer.Ordinal);

    /// <summary>A stranger's read holds its source's permit and the process's: both go back together.</summary>
    private sealed class PairLease(RateLimitLease first, RateLimitLease second) : RateLimitLease
    {
        public override bool IsAcquired => true;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                first.Dispose();
                second.Dispose();
            }

            base.Dispose(disposing);
        }
    }

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
