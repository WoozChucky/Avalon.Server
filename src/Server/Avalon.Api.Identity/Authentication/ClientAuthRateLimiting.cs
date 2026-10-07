using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The named rate-limit policy on the launcher sign-in endpoints (#591): per source, whether or not the caller is
/// signed in, and in addition to the shared request rate limiter (<see cref="ApiRateLimiting"/>), whose rejection
/// answers it too.
/// </summary>
public static class ClientAuthRateLimiting
{
    /// <summary>The policy's name, for <c>[EnableRateLimiting]</c>.</summary>
    public const string Policy = "client-auth";

    public static IServiceCollection AddClientAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingConfig>>((options, config) =>
            {
                RateLimitingConfig limits = config.Value;
                options.AddPolicy(Policy, context => limits.Enabled
                    ? RateLimitPartition.GetSlidingWindowLimiter(ApiRateLimiting.SourceOf(context), _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.ClientAuthPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = ApiRateLimiting.SegmentsPerWindow,
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter(string.Empty));
            });
        return services;
    }
}
