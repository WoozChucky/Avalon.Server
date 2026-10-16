using System.Net;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// #967: the queue's limits. Each account has its own share, so the waiters behind one address do not throttle each
/// other; a source that keeps failing is slowed to a few reads at a time but never shut out, so a neighbour cannot push
/// honest waiters out of the line; every source has a ceiling before anything is read; and every refusal asks again
/// within one segment, well inside a waiter's removal deadline.
/// </summary>
public sealed class QueueRateLimiterShould
{
    [Fact]
    public async Task Give_each_account_its_share_and_bound_a_source_without_shutting_it_out()
    {
        using var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig { QueueSourcePermitsPerMinute = 40 }),
            Substitute.For<IExemptSources>());
        var household = new DefaultHttpContext();
        household.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        for (int call = 0; call < 30; call++) Assert.Null(limiter.TryAcquire(7));
        Assert.InRange(limiter.TryAcquire(7)!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.Null(limiter.TryAcquire(8)); // another account behind the same address is not throttled by 7's calls

        // A neighbour presents credentials that are not current: the source's reads go on, two at a time.
        for (int failure = 0; failure < QueueRateLimiter.FailedCredentialsPerMinute; failure++) limiter.CredentialFailed(household);
        RateLimitLease first = (await limiter.StartCredentialReadAsync(household, default))!;
        using RateLimitLease second = (await limiter.StartCredentialReadAsync(household, default))!;
        ValueTask<RateLimitLease?> third = limiter.StartCredentialReadAsync(household, default);
        Assert.False(third.IsCompleted);
        first.Dispose();
        using RateLimitLease served = (await third)!;
        Assert.True(served.IsAcquired);

        for (int call = 0; call < 40; call++) Assert.Null(limiter.TryAdmitSource(household));
        Assert.InRange(limiter.TryAdmitSource(household)!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.True(QueueRateLimiter.Segment < TimeSpan.FromSeconds(10)); // inside FrontTimeoutSeconds - FrontCheckInSeconds
    }
}
