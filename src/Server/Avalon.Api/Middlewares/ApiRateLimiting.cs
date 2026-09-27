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
    /// so it is authenticated here; the handler keeps its result for the request, and the
    /// authorization that follows reuses it rather than looking the token up again.
    /// </summary>
    public static IApplicationBuilder UseApiRateLimiting(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true && CarriesPersonalAccessToken(context.Request))
            {
                AuthenticateResult result = await context.AuthenticateAsync(AvalonAuthenticationSchemeOptions.SchemeName);
                if (result.Succeeded && AccountIdOf(result.Principal) is { } accountId)
                    context.Items[PatAccountItem] = accountId;
            }

            await next(context);
        });
        return app.UseRateLimiter();
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

        IPAddress? address = context.Connection.RemoteIpAddress;
        return new Partition(PartitionKind.Anonymous, address is null ? UnknownSource : RemoteAddress.SourceOf(address));
    }

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
