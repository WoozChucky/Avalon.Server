using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Avalon.Api.Authentication.AV;
using Avalon.Api.Config;
using Avalon.Common.Telemetry;
using Avalon.Infrastructure.Login;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Middlewares;

/// <summary>
/// Request rate limiting (#561), on top of the login and registration budgets, which count
/// attempts and are unchanged. A sliding window of one minute in six segments, in memory, since
/// the api runs as one replica. A request is counted against its account when it carries a valid
/// access token or personal access token, and against its source otherwise (an invalid token
/// included): the login budgets' source, the IPv4 address or the IPv6 /64, read after the
/// trusted forwarded-headers middleware. Every caller with no peer address shares one partition.
/// <c>/health</c> and <c>/alive</c> are never limited. A refusal is 429 ProblemDetails
/// <c>LOCKED</c> with <c>Retry-After</c>, the same whichever partition refused it.
/// </summary>
public static class ApiRateLimiting
{
    public const string Section = "Application:RateLimiting";
    public const string RejectionsMetric = "avalon.api.rate_limit.rejections";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public const int SegmentsPerWindow = 6;

    /// <summary>
    /// Failed personal-access-token lookups one source may make in the window before the early
    /// lookup stops for it (#561 review). Not a setting: it bounds the database queries made for
    /// requests the limiter may refuse, and a client with a valid token never spends it.
    /// </summary>
    public const int FailedPatLookupsPerMinute = 10;

    /// <summary>The partition every caller with no peer address shares; no source key has this form.</summary>
    public const string UnknownSource = "unknown";

    /// <summary>How a personal access token is sent, as <see cref="AvalonAuthenticationHandler"/> reads it.</summary>
    private const string PersonalAccessTokenHeaderPrefix = "Avalon ";

    private const string PatAccountItem = "Avalon.RateLimiting.PatAccountId";

    private static readonly Counter<long> Rejections = DiagnosticsConfig.Api.Meter.CreateCounter<long>(
        RejectionsMetric, "{requests}", "Requests refused by the rate limiter, by partition kind");

    public enum PartitionKind
    {
        Exempt,
        Anonymous,
        Authenticated,
    }

    /// <summary>A limiter partition: the kind and, within it, the account id or the source.</summary>
    public readonly record struct Partition(PartitionKind Kind, string Key);

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        // Checked by the startup validation ApiStartup runs first (#543), naming the setting.
        services.AddOptions<RateLimitingConfig>()
            .BindConfiguration(Section)
            .Validate(c => c.AnonymousPermitsPerMinute >= 1,
                $"{Section}:{nameof(RateLimitingConfig.AnonymousPermitsPerMinute)} must be at least 1.")
            .Validate(c => c.AuthenticatedPermitsPerMinute >= 1,
                $"{Section}:{nameof(RateLimitingConfig.AuthenticatedPermitsPerMinute)} must be at least 1.")
            .ValidateOnStart();

        services.AddSingleton<FailedPatLookups>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingConfig>>((options, config) =>
            {
                RateLimitingConfig limits = config.Value;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, Partition>(
                    context => LimiterFor(PartitionOf(context, limits.Enabled), limits));
                options.OnRejected = OnRejectedAsync;
            });
        return services;
    }

    /// <summary>
    /// After <c>UseAuthentication</c>, so a JWT has been validated and its account revalidated
    /// (#480) before the partition is chosen. A personal access token is not the default scheme,
    /// so <see cref="IdentifyPersonalAccessTokenAsync"/> authenticates it first.
    /// </summary>
    public static IApplicationBuilder UseApiRateLimiting(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            await IdentifyPersonalAccessTokenAsync(context);
            await next(context);
        });
        return app.UseRateLimiter();
    }

    /// <summary>
    /// Authenticates a personal access token before the limiter, so it is counted against its
    /// account. The handler keeps its result for the request, and the authorization that follows
    /// reuses it rather than looking the token up again. Skipped, so the request is counted as
    /// anonymous (#561 review):
    /// <list type="bullet">
    /// <item>when limiting is disabled, since no partition is needed;</item>
    /// <item>on an endpoint with no authorization, whose token was never looked up (nor its last
    /// use recorded) before;</item>
    /// <item>when the source has spent its <see cref="FailedPatLookupsPerMinute"/>, so made-up
    /// tokens cannot force a database query per request past the limit. Authorization still
    /// answers 401 for a request the limiter lets through.</item>
    /// </list>
    /// </summary>
    public static async Task IdentifyPersonalAccessTokenAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true || !CarriesPersonalAccessToken(context.Request))
            return;
        if (!context.RequestServices.GetRequiredService<IOptions<RateLimitingConfig>>().Value.Enabled)
            return;
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is null)
            return;

        FailedPatLookups failures = context.RequestServices.GetRequiredService<FailedPatLookups>();
        string source = SourceOf(context);
        // Held for the whole lookup, so the failure below is recorded before another may start.
        using RateLimitLease? lookup = failures.TryStart(source);
        if (lookup is null)
            return;

        AuthenticateResult result = await context.AuthenticateAsync(AvalonAuthenticationSchemeOptions.SchemeName);
        if (result.Succeeded && AccountIdOf(result.Principal) is { } accountId)
            context.Items[PatAccountItem] = accountId;
        else
            failures.Record(source);
    }

    /// <summary>
    /// The early personal-access-token lookups one source may make (#561 review): at most
    /// <see cref="ConcurrentLookupsPerSource"/> at once, and only while it has failed fewer than
    /// <see cref="FailedPatLookupsPerMinute"/> in a sliding window of one minute. A failure is
    /// known only once its lookup ends, so without the concurrency bound lookups running side by
    /// side would all see a failure left; with it, a burst costs at most the budget plus the
    /// lookups in flight when it ran out. The two limiters are separate rather than chained,
    /// because a lookup spends the failure budget only if it fails: a valid token never does.
    /// </summary>
    public sealed class FailedPatLookups : IDisposable
    {
        public const int ConcurrentLookupsPerSource = 2;

        private readonly PartitionedRateLimiter<string> _inFlight = PartitionedRateLimiter.Create<string, string>(
            source => RateLimitPartition.GetConcurrencyLimiter(source, _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = ConcurrentLookupsPerSource,
                QueueLimit = 0,
            }), StringComparer.Ordinal);

        private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(
            source => RateLimitPartition.GetSlidingWindowLimiter(source, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = FailedPatLookupsPerMinute,
                Window = Window,
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            }), StringComparer.Ordinal);

        /// <summary>
        /// A lease to hold for one lookup, or null when the source already has its lookups in
        /// flight or has no failure left. Spends nothing of the failure budget.
        /// </summary>
        public RateLimitLease? TryStart(string source)
        {
            RateLimitLease lease = _inFlight.AttemptAcquire(source);
            if (lease.IsAcquired && MayFail(source))
                return lease;
            lease.Dispose();
            return null;
        }

        /// <summary>Spends one of the source's failures, for a lookup that found no valid token.</summary>
        public void Record(string source) => _limiter.AttemptAcquire(source).Dispose();

        public void Dispose()
        {
            _inFlight.Dispose();
            _limiter.Dispose();
        }

        private bool MayFail(string source)
        {
            using RateLimitLease lease = _limiter.AttemptAcquire(source, 0);
            return lease.IsAcquired;
        }
    }

    /// <summary>Which partition a request is counted against.</summary>
    public static Partition PartitionOf(HttpContext context, bool enabled)
    {
        if (!enabled || context.GetEndpoint()?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null)
            return new Partition(PartitionKind.Exempt, "");

        string? accountId = context.User.Identity?.IsAuthenticated == true
            ? AccountIdOf(context.User)
            : context.Items[PatAccountItem] as string;
        if (accountId is not null)
            return new Partition(PartitionKind.Authenticated, accountId);

        return new Partition(PartitionKind.Anonymous, SourceOf(context));
    }

    /// <summary>The login budgets' source (IPv4 address, IPv6 /64), or the one partition for no peer address.</summary>
    private static string SourceOf(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address ? RemoteAddress.SourceOf(address) : UnknownSource;

    private static RateLimitPartition<Partition> LimiterFor(Partition partition, RateLimitingConfig limits) =>
        partition.Kind switch
        {
            PartitionKind.Authenticated => SlidingWindow(partition, limits.AuthenticatedPermitsPerMinute),
            PartitionKind.Anonymous => SlidingWindow(partition, limits.AnonymousPermitsPerMinute),
            _ => RateLimitPartition.GetNoLimiter(partition),
        };

    private static RateLimitPartition<Partition> SlidingWindow(Partition partition, int permits) =>
        RateLimitPartition.GetSlidingWindowLimiter(partition, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = Window,
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static bool CarriesPersonalAccessToken(HttpRequest request) =>
        request.Headers.TryGetValue(HeaderNames.Authorization, out var value)
        && value.ToString().StartsWith(PersonalAccessTokenHeaderPrefix, StringComparison.OrdinalIgnoreCase);

    private static string? AccountIdOf(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None,
            CultureInfo.InvariantCulture, out long id)
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;

    private static async ValueTask OnRejectedAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        HttpContext context = rejected.HttpContext;
        PartitionKind kind = PartitionOf(context, enabled: true).Kind;
        Rejections.Add(1, new KeyValuePair<string, object?>("partition",
            kind == PartitionKind.Authenticated ? "authenticated" : "anonymous"));

        // The limiter's own hint when it gives one, otherwise one segment, the soonest any permit
        // comes back. Worked out the same way for either partition.
        TimeSpan retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan hint) && hint > TimeSpan.Zero
            ? hint
            : Window / SegmentsPerWindow;
        context.Response.Headers.RetryAfter =
            Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        // The login lockout's shape and wording (#478), so a client handles both alike.
        await ExceptionHandlerMiddleware.WriteProblemAsync(context, StatusCodes.Status429TooManyRequests,
            "TooManyRequests", "Too many requests", "LOCKED");
    }
}
