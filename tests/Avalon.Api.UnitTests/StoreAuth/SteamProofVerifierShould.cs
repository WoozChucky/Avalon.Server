using System.Net;
using System.Text;
using Avalon.Configuration;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.StoreAuth;

public class SteamProofVerifierShould
{
    internal const string Identity = "avalon-auth-prod:00112233445566778899aabbccddeeff";
    internal const string SteamId = "76561198000000001";
    internal const string Key = "never-log-this-test-publisher-key";

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("GG")]
    public async Task Reject_invalid_hex_without_sending_it(string ticket)
    {
        using var handler = new RecordingSteamHandler();
        using var client = new HttpClient(handler);
        var result = await new SteamProofVerifier(client, Configuration()).VerifyAsync(ticket, Identity, CancellationToken.None);
        Assert.Equal(SteamProofStatus.InvalidProof, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reject_oversized_hex_and_wrong_environment_identity_without_sending()
    {
        using var handler = new RecordingSteamHandler();
        using var client = new HttpClient(handler);
        var verifier = new SteamProofVerifier(client, Configuration());
        Assert.Equal(SteamProofStatus.InvalidProof, (await verifier.VerifyAsync(new string('A', 5122), Identity, CancellationToken.None)).Status);
        Assert.Equal(SteamProofStatus.InvalidProof, (await verifier.VerifyAsync("ABCD", "avalon-auth-dev:00112233445566778899aabbccddeeff", CancellationToken.None)).Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"response\":{\"params\":{\"result\":\"OK\"}}}")]
    [InlineData("{\"response\":{\"params\":{\"result\":\"No\",\"steamid\":\"76561198000000001\"}}}")]
    [InlineData("{\"response\":{\"error\":{\"errorcode\":101,\"errordesc\":\"secret\"}}}")]
    [InlineData("{\"response\":{\"params\":{\"result\":\"OK\",\"steamid\":\"0\"}}}")]
    public async Task Refuse_http_success_without_verified_identity(string body)
    {
        using var handler = new RecordingSteamHandler { Body = body };
        using var client = new HttpClient(handler);
        Assert.Equal(SteamProofStatus.InvalidProof, (await new SteamProofVerifier(client, Configuration())
            .VerifyAsync("ABCD", Identity, CancellationToken.None)).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Use_only_the_verified_subject_and_fixed_application_and_attempt_identity()
    {
        using var handler = new RecordingSteamHandler { Body = "{\"response\":{\"params\":{\"result\":\"OK\",\"steamid\":\"76561198000000001\",\"ownersteamid\":\"76561198000000002\"}}}" };
        using var client = new HttpClient(handler);
        var result = await new SteamProofVerifier(client, Configuration()).VerifyAsync("aB01", Identity, CancellationToken.None);
        Assert.Equal(SteamProofStatus.Verified, result.Status);
        Assert.Equal(SteamId, result.ProviderSubject);
        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("partner.steam-api.com", uri.Host);
        Assert.Equal("/ISteamUserAuth/AuthenticateUserTicket/v1/", uri.AbsolutePath);
        Assert.Contains("appid=2499460", uri.Query, StringComparison.Ordinal);
        Assert.Contains("identity=" + Uri.EscapeDataString(Identity), uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Return_sanitized_unavailable_after_one_retry(HttpStatusCode status)
    {
        using var handler = new RecordingSteamHandler { Status = status, Body = Key };
        using var client = new HttpClient(handler);
        var result = await new SteamProofVerifier(client, Configuration()).VerifyAsync("ABCD", Identity, CancellationToken.None);
        Assert.Equal(SteamProofStatus.ProviderUnavailable, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(Key, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sanitize_network_failures_even_when_the_exception_contains_the_request_url()
    {
        using var handler = new RecordingSteamHandler { Failure = new HttpRequestException("https://partner.steam-api.com/?key=" + Key + "&ticket=ABCD") };
        using var client = new HttpClient(handler);
        var result = await new SteamProofVerifier(client, Configuration()).VerifyAsync("ABCD", Identity, CancellationToken.None);
        Assert.Equal(SteamProofStatus.ProviderUnavailable, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(Key, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Propagate_caller_cancellation_and_bound_timeout()
    {
        using var handler = new RecordingSteamHandler { WaitUntilCancelled = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(20) };
        Assert.Equal(SteamProofStatus.ProviderUnavailable, (await new SteamProofVerifier(client, Configuration())
            .VerifyAsync("ABCD", Identity, CancellationToken.None)).Status);
        Assert.Equal(2, handler.Requests.Count);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SteamProofVerifier(client, Configuration())
            .VerifyAsync("ABCD", Identity, cancelled.Token));
    }

    internal static IOptions<StoreAuthenticationConfiguration> Configuration() =>
        Options.Create(new StoreAuthenticationConfiguration { SteamPublisherKey = Key });
}

internal sealed class RecordingSteamHandler : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public string Body { get; init; } = "{}";
    public Exception? Failure { get; init; }
    public bool WaitUntilCancelled { get; init; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (WaitUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (Failure is not null) throw Failure;
        return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}
