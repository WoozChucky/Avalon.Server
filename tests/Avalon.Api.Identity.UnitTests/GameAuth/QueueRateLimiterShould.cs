using System.Net;
using System.Threading.RateLimiting;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// #967: a credential identity knows is served on its own budget whatever its source does; strangers' credentials are
/// gated by their source, in flight and in rate, and leave no state of their own; every refusal asks again within one
/// segment, well inside a waiter's removal deadline.
/// </summary>
public sealed class QueueRateLimiterShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly DefaultHttpContext _household = new();

    public QueueRateLimiterShould() => _household.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

    private DateTime Expiry => _clock.GetUtcNow().UtcDateTime.AddMinutes(5);

    [Fact]
    public async Task Serve_a_known_credential_while_its_source_is_flooded_with_junk_and_on_its_own_budget()
    {
        using QueueRateLimiter limiter = Limiter();
        string honest = GameAuthCryptography.NewToken(), neighbour = GameAuthCryptography.NewToken();
        limiter.Issued(honest, Expiry, null);
        limiter.Issued(neighbour, Expiry, null);
        for (int failure = 0; failure < QueueRateLimiter.FailedCredentialsPerMinute; failure++)
            limiter.CredentialFailed(_household, GameAuthCryptography.NewToken());

        // Junk fills the source's gate: two reads in flight, the rest waiting.
        var junk = new List<Task<QueueRead>>();
        for (int read = 0; read < QueueRateLimiter.ReadsInFlight + QueueRateLimiter.ReadsWaiting; read++)
            junk.Add(limiter.StartReadAsync(_household, GameAuthCryptography.NewToken(), default).AsTask());
        Assert.NotNull((await limiter.StartReadAsync(_household, GameAuthCryptography.NewToken(), default)).RetryAfter);

        QueueRead served = await limiter.StartReadAsync(_household, honest, default);
        Assert.True(served.Lease!.IsAcquired);

        // The neighbour spends its own credential's budget, never the honest one's.
        for (int call = 0; call < 30; call++) Assert.Null((await limiter.StartReadAsync(_household, neighbour, default)).RetryAfter);
        Assert.InRange((await limiter.StartReadAsync(_household, neighbour, default)).RetryAfter!.Value,
            TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.Null((await limiter.StartReadAsync(_household, honest, default)).RetryAfter);
        Assert.True(QueueRateLimiter.Segment < TimeSpan.FromSeconds(10)); // inside FrontTimeoutSeconds - FrontCheckInSeconds
        foreach (Task<QueueRead> waiting in junk.Where(t => t.IsCompletedSuccessfully)) (await waiting).Lease?.Dispose();
    }

    [Fact]
    public async Task Refuse_strangers_past_their_source_rate_without_keeping_anything_of_their_credentials()
    {
        using QueueRateLimiter limiter = Limiter(new RateLimitingConfig { QueueUnknownReadsPerMinute = 3 });
        for (int failure = 0; failure < QueueRateLimiter.FailedCredentialsPerMinute; failure++)
            limiter.CredentialFailed(_household, GameAuthCryptography.NewToken());

        for (int read = 0; read < 3; read++)
        {
            QueueRead admitted = await limiter.StartReadAsync(_household, GameAuthCryptography.NewToken(), default);
            admitted.Lease!.Dispose();
        }

        QueueRead refused = await limiter.StartReadAsync(_household, GameAuthCryptography.NewToken(), default);
        Assert.Null(refused.Lease);
        Assert.InRange(refused.RetryAfter!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.Equal(0, limiter.KnownCredentials);
    }

    [Fact]
    public void Keep_no_more_known_credentials_than_its_bound_and_forget_one_replaced()
    {
        using var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig()), Substitute.For<IExemptSources>(), _clock, 8);
        string first = GameAuthCryptography.NewToken();
        limiter.Issued(first, Expiry, null);
        limiter.Issued(GameAuthCryptography.NewToken(), Expiry, GameAuthCryptography.Digest(first)); // a refresh replaces it
        Assert.Equal(1, limiter.KnownCredentials);
        for (int credential = 0; credential < 20; credential++) limiter.Issued(GameAuthCryptography.NewToken(), Expiry, null);
        Assert.InRange(limiter.KnownCredentials, 1, 8);
    }

    [Fact]
    public async Task Refuse_strangers_past_the_process_bound_whatever_their_sources_but_never_a_known_credential()
    {
        using QueueRateLimiter limiter = Limiter(new RateLimitingConfig { QueueStrangerReadsInFlight = 1 });
        string known = GameAuthCryptography.NewToken();
        limiter.Issued(known, Expiry, null);
        var elsewhere = new DefaultHttpContext();
        elsewhere.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");

        using RateLimitLease held = (await limiter.StartReadAsync(_household, GameAuthCryptography.NewToken(), default)).Lease!;
        QueueRead refused = await limiter.StartReadAsync(elsewhere, GameAuthCryptography.NewToken(), default);
        Assert.Null(refused.Lease);
        Assert.InRange(refused.RetryAfter!.Value, TimeSpan.FromTicks(1), QueueRateLimiter.Segment);
        Assert.NotNull((await limiter.StartReadAsync(elsewhere, known, default)).Lease);
    }

    private QueueRateLimiter Limiter(RateLimitingConfig? config = null) =>
        new(Options.Create(config ?? new RateLimitingConfig()), Substitute.For<IExemptSources>(), _clock);
}
