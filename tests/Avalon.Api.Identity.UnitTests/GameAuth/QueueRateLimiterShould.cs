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
/// #967: the queue's limits are never shared by the callers behind one address. A neighbour's spam, with a valid
/// credential or junk, spends only its own credential's budget or slows its source's reads, and never refuses another
/// credential's call; every refusal asks again within one segment, well inside a waiter's removal deadline.
/// </summary>
public sealed class QueueRateLimiterShould
{
    private static readonly string s_neighbour = new('N', 43);
    private static readonly string s_honest = new('H', 43);

    [Fact]
    public void Bound_each_credential_and_account_on_its_own()
    {
        using var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig()), Substitute.For<IExemptSources>());

        // A neighbour spams with its own valid credential: refused past its budget, the honest waiter's untouched.
        for (int call = 0; call < 30; call++) Assert.Null(limiter.TryAcquireCredential(s_neighbour));
        Assert.InRange(limiter.TryAcquireCredential(s_neighbour)!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.Null(limiter.TryAcquireCredential(s_honest));

        for (int call = 0; call < 30; call++) Assert.Null(limiter.TryAcquire(7));
        Assert.InRange(limiter.TryAcquire(7)!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.Null(limiter.TryAcquire(8));
        Assert.True(QueueRateLimiter.Segment < TimeSpan.FromSeconds(10)); // inside FrontTimeoutSeconds - FrontCheckInSeconds
    }

    [Fact]
    public async Task Slow_a_source_that_keeps_failing_without_refusing_the_honest_calls_behind_it()
    {
        using var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig()), Substitute.For<IExemptSources>());
        var household = new DefaultHttpContext();
        household.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        // A neighbour presents junk: each junk credential has a budget of its own, so it is the failures that bound it.
        for (int failure = 0; failure < QueueRateLimiter.FailedCredentialsPerMinute; failure++)
        {
            Assert.Null(limiter.TryAcquireCredential(Guid.NewGuid().ToString("N")));
            limiter.CredentialFailed(household);
        }

        // Past them the source's reads go two at a time; the honest waiter's call waits its turn, it is not refused.
        RateLimitLease first = (await limiter.StartCredentialReadAsync(household, default))!;
        using RateLimitLease second = (await limiter.StartCredentialReadAsync(household, default))!;
        ValueTask<RateLimitLease?> honest = limiter.StartCredentialReadAsync(household, default);
        Assert.False(honest.IsCompleted);
        first.Dispose();
        using RateLimitLease served = (await honest)!;
        Assert.True(served.IsAcquired);
        Assert.Null(limiter.TryAcquireCredential(s_honest));
    }
}
