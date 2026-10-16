using System.Net;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>#967: the queue's limit is the account's, not the address's, and a source that keeps failing is stopped.</summary>
public sealed class QueueRateLimiterShould
{
    [Fact]
    public void Allow_each_account_its_own_check_ins_and_stop_a_source_that_keeps_failing()
    {
        using var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig()), Substitute.For<IExemptSources>());
        var household = new DefaultHttpContext();
        household.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        for (int call = 0; call < 30; call++) Assert.Null(limiter.TryAcquire(7));
        Assert.NotNull(limiter.TryAcquire(7));
        Assert.Null(limiter.TryAcquire(8)); // another account behind the same address is not throttled by 7's calls

        for (int failure = 0; failure < QueueRateLimiter.FailedCredentialsPerMinute; failure++)
        {
            Assert.True(limiter.MayTryCredential(household));
            limiter.CredentialFailed(household);
        }

        Assert.False(limiter.MayTryCredential(household));
    }
}
