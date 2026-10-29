using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Middlewares;
using Microsoft.AspNetCore.RateLimiting;

namespace Avalon.Api.Distribution;

public static class CrashReportRateLimiting
{
    public const string Policy = "client-crash";

    /// <summary>The upload-complete notice (#1252): a report gets one, a little room is left for retries.</summary>
    public const string NoticePolicy = "client-crash-notice";

    public static void AddCrashReportRateLimiting(this IServiceCollection services) =>
        services.Configure<RateLimiterOptions>(options =>
        {
            options.AddPolicy(Policy, context =>
                RateLimitPartition.GetSlidingWindowLimiter(ApiRateLimiting.SourceOf(context), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromHours(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));
            options.AddPolicy(NoticePolicy, context =>
                RateLimitPartition.GetSlidingWindowLimiter(ApiRateLimiting.SourceOf(context), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));
        });
}
