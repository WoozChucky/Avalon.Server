using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Middlewares;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Common.Accounts;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Middlewares;

/// <summary>
/// #561: the api limits plain request volume, per account for a signed-in caller and per source
/// (the login budgets' rule: IPv4 address, IPv6 /64) for everyone else, with a sliding window of
/// one minute. A rejection is 429 ProblemDetails LOCKED with Retry-After, whichever partition.
/// Run through <see cref="ApiAuthHost"/>, which has Program's middleware order. The class is in a
/// collection that runs alone, so the rejection counter, a static instrument, sees only its tests.
/// </summary>
[Collection(RateLimitingCollection.Name)]
public sealed class RateLimitingShould
{
    private const string OtherPeer = "203.0.113.9";
    private const long OtherAccountId = 8;
    private const string PatToken = "avp_0123456789abcdef0123456789abcdef0123456789a";

    private static Task<ApiAuthHost> StartAsync(int anonymous = 3, int authenticated = 5, bool enabled = true) =>
        ApiAuthHost.StartAsync(configure: services => services.Configure<RateLimitingConfig>(c =>
        {
            c.Enabled = enabled;
            c.AnonymousPermitsPerMinute = anonymous;
            c.AuthenticatedPermitsPerMinute = authenticated;
        }));

    private static async Task<HttpStatusCode> SendAsync(ApiAuthHost host, string path = "/anonymous",
        string? peer = null, string? token = null, string? forwardedFor = null, bool noAddress = false,
        string scheme = "Bearer")
    {
        using HttpResponseMessage response = await SendForResponseAsync(host, path, peer, token, forwardedFor,
            noAddress, scheme);
        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> SendForResponseAsync(ApiAuthHost host, string path = "/anonymous",
        string? peer = null, string? token = null, string? forwardedFor = null, bool noAddress = false,
        string scheme = "Bearer")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (peer != null) request.Headers.Add(ApiAuthHost.PeerHeader, peer);
        if (noAddress) request.Headers.Add(ApiAuthHost.NoAddressHeader, "1");
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return host.Client.SendAsync(request);
    }

    private static async Task SpendAsync(ApiAuthHost host, int count, string path = "/anonymous", string? peer = null,
        string? token = null, string? forwardedFor = null, bool noAddress = false, string scheme = "Bearer")
    {
        for (int i = 0; i < count; i++)
            Assert.Equal(HttpStatusCode.OK, await SendAsync(host, path, peer, token, forwardedFor, noAddress, scheme));
    }

    /// <summary>A token of the right form that no personal access token has.</summary>
    private static string MadeUpPat(int i) => "avp_" + i.ToString("D43", System.Globalization.CultureInfo.InvariantCulture);

    private static PersonalAccessToken PatFor(string token) => new()
    {
        Id = new PersonalAccessTokenId(5),
        AccountId = new AccountId(ApiAuthHost.AccountIdValue),
        TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
        Name = "ci",
        TokenPrefix = token[..8],
        Roles = AccountAccessLevel.Player,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(1),
    };

    private static Account AccountWithId(long id)
    {
        Account account = ApiAuthHost.MakeAccount();
        account.Id = new AccountId(id);
        return account;
    }

    /// <summary>A second account the host's account service knows, with its own access token.</summary>
    private static string OtherAccountToken(ApiAuthHost host)
    {
        Account other = AccountWithId(OtherAccountId);
        host.Accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == OtherAccountId), Arg.Any<CancellationToken>())
            .Returns(other);
        return ApiAuthHost.Mint(other);
    }

    [Fact]
    public async Task Pass_the_sixtieth_anonymous_request_a_minute_and_refuse_the_sixty_first_with_locked()
    {
        await using ApiAuthHost host = await ApiAuthHost.StartAsync();

        await SpendAsync(host, 60);
        using HttpResponseMessage refused = await SendForResponseAsync(host);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem!.Status);
        Assert.Equal("LOCKED", problem.Detail);
        Assert.True(refused.Headers.RetryAfter?.Delta is { TotalSeconds: >= 1 },
            $"Retry-After was '{refused.Headers.RetryAfter}'");
    }

    [Fact]
    public async Task Give_two_ipv4_addresses_separate_budgets()
    {
        await using ApiAuthHost host = await StartAsync();

        await SpendAsync(host, 3, peer: "198.51.100.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, peer: "198.51.100.1"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, peer: "198.51.100.2"));
    }

    [Fact]
    public async Task Share_one_budget_within_an_ipv6_64_and_not_across_them()
    {
        await using ApiAuthHost host = await StartAsync();

        await SpendAsync(host, 2, peer: "2001:db8:1:2::1");
        await SpendAsync(host, 1, peer: "2001:db8:1:2:ffff:ffff:ffff:ffff");

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, peer: "2001:db8:1:2::7"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, peer: "2001:db8:1:3::1"));
    }

    [Fact]
    public async Task Count_an_ipv4_mapped_address_as_its_ipv4_address()
    {
        await using ApiAuthHost host = await StartAsync();

        await SpendAsync(host, 3, peer: "198.51.100.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, peer: "::ffff:198.51.100.1"));
    }

    [Fact]
    public async Task Share_one_budget_among_callers_with_no_peer_address()
    {
        await using ApiAuthHost host = await StartAsync();

        await SpendAsync(host, 3, noAddress: true);

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, noAddress: true));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, peer: "198.51.100.1"));
    }

    [Fact]
    public async Task Keep_an_accounts_budget_apart_from_its_addresss_anonymous_budget()
    {
        await using ApiAuthHost host = await StartAsync();
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        string token = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());

        await SpendAsync(host, 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));

        // Same address, signed in: the account's own budget, five here.
        await SpendAsync(host, 5, "/player", token: token);
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, "/player", token: token));
    }

    [Fact]
    public async Task Give_two_accounts_separate_budgets()
    {
        await using ApiAuthHost host = await StartAsync();
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        string first = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());
        string second = OtherAccountToken(host);

        await SpendAsync(host, 5, "/player", token: first);

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, "/player", token: first));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, "/player", token: second));
    }

    /// <summary>A token that does not validate is an anonymous caller, counted against its source.</summary>
    [Fact]
    public async Task Count_a_request_with_an_invalid_token_against_its_source()
    {
        await using ApiAuthHost host = await StartAsync();

        await SpendAsync(host, 3, token: "not-a-jwt");

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));
    }

    /// <summary>
    /// A token whose account is banned since it was issued fails the revalidation, so the request
    /// is anonymous: the limiter runs after the revalidation, never in place of it.
    /// </summary>
    [Fact]
    public async Task Count_a_token_whose_account_is_no_longer_active_against_its_source()
    {
        await using ApiAuthHost host = await StartAsync();
        string token = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());
        host.AccountNowIs(ApiAuthHost.MakeAccount(status: AccountStatus.Banned));

        await SpendAsync(host, 3, token: token);

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));
        await host.Accounts.Received().FindByIdAsync(Arg.Is<AccountId>(id => id.Value == ApiAuthHost.AccountIdValue),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A personal access token is not the default authentication scheme, yet it is the account's
    /// budget, the one its access tokens spend. It is looked up once per request.
    /// </summary>
    [Fact]
    public async Task Count_a_personal_access_token_against_its_account_and_look_it_up_once()
    {
        await using ApiAuthHost host = await StartAsync();
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        host.Pats.FindByRawTokenAsync(PatToken, Arg.Any<CancellationToken>()).Returns(PatFor(PatToken));
        string jwt = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());

        await SpendAsync(host, 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));

        await SpendAsync(host, 3, "/player", token: PatToken, scheme: "Avalon");
        await host.Pats.Received(3).FindByRawTokenAsync(PatToken, Arg.Any<CancellationToken>());
        await SpendAsync(host, 2, "/player", token: jwt);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            await SendAsync(host, "/player", token: PatToken, scheme: "Avalon"));
    }

    /// <summary>
    /// #561 review: the early lookup ran before the limiter, so made-up tokens forced one database
    /// query per request even past the limit. Failed lookups are budgeted per source: past it, a
    /// request is anonymous without a lookup, and one past the anonymous limit reaches nothing.
    /// </summary>
    [Fact]
    public async Task Stop_looking_up_made_up_personal_access_tokens_once_a_source_spends_its_failed_lookup_budget()
    {
        await using ApiAuthHost host = await StartAsync(anonymous: 3);

        int requests = ApiRateLimiting.FailedPatLookupsPerMinute + 20;
        for (int i = 0; i < requests; i++)
        {
            HttpStatusCode status = await SendAsync(host, "/player", token: MadeUpPat(i), scheme: "Avalon");
            Assert.Equal(i < 3 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, status);
        }

        // Each lookup is made once per request (authorization reuses the early one); none past the budget.
        await host.Pats.Received(ApiRateLimiting.FailedPatLookupsPerMinute)
            .FindByRawTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Another source still has its own budget.
        Assert.Equal(HttpStatusCode.Unauthorized,
            await SendAsync(host, "/player", peer: "198.51.100.3", token: MadeUpPat(999), scheme: "Avalon"));
    }

    [Fact]
    public async Task Still_give_a_valid_personal_access_token_its_accounts_partition()
    {
        await using ApiAuthHost host = await StartAsync(anonymous: 1, authenticated: 50);
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        host.Pats.FindByRawTokenAsync(PatToken, Arg.Any<CancellationToken>()).Returns(PatFor(PatToken));

        await SpendAsync(host, 3, "/player", token: PatToken, scheme: "Avalon");
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));
        // Still the account's budget, not the spent anonymous one.
        await SpendAsync(host, 3, "/player", token: PatToken, scheme: "Avalon");
    }

    /// <summary>Disabled, the limiter needs no partition, so nothing is looked up early.</summary>
    [Fact]
    public async Task Not_look_up_a_personal_access_token_early_when_disabled()
    {
        var authentication = Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        HttpContext context = PatRequest(authentication, enabled: false, requiresAuthorization: true);

        await ApiRateLimiting.IdentifyPersonalAccessTokenAsync(context);

        await authentication.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default);
    }

    [Fact]
    public async Task Look_up_a_personal_access_token_early_when_enabled_on_an_endpoint_that_authorizes()
    {
        var authentication = Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        authentication.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>())
            .Returns(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        HttpContext context = PatRequest(authentication, enabled: true, requiresAuthorization: true);

        await ApiRateLimiting.IdentifyPersonalAccessTokenAsync(context);

        await authentication.Received(1).AuthenticateAsync(context, Avalon.Api.Authentication.AV.AvalonAuthenticationSchemeOptions.SchemeName);
    }

    /// <summary>
    /// An endpoint with no authorization never ran the token's scheme before, so its last-used
    /// time was not touched; the early lookup must not start doing either (#561 review).
    /// </summary>
    [Fact]
    public async Task Not_look_up_or_touch_a_personal_access_token_sent_to_an_anonymous_endpoint()
    {
        await using ApiAuthHost host = await StartAsync();
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        host.Pats.FindByRawTokenAsync(PatToken, Arg.Any<CancellationToken>()).Returns(PatFor(PatToken));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, token: PatToken, scheme: "Avalon"));

        await host.Pats.DidNotReceiveWithAnyArgs().FindByRawTokenAsync(default!, default);
        await host.Pats.DidNotReceiveWithAnyArgs().TouchLastUsedAsync(default, default);
    }

    private static HttpContext PatRequest(Microsoft.AspNetCore.Authentication.IAuthenticationService authentication,
        bool enabled, bool requiresAuthorization)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authentication);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new RateLimitingConfig { Enabled = enabled }));
        services.AddSingleton<ApiRateLimiting.FailedPatLookups>();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        context.Request.Headers.Authorization = "Avalon " + PatToken;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            requiresAuthorization
                ? new EndpointMetadataCollection(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute())
                : EndpointMetadataCollection.Empty, "test"));
        return context;
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Never_limit_the_health_probes(string path)
    {
        await using ApiAuthHost host = await StartAsync(anonymous: 1);

        await SpendAsync(host, 5, path);

        Assert.Equal(HttpStatusCode.OK, await SendAsync(host));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host));
    }

    [Fact]
    public async Task Limit_nothing_when_disabled()
    {
        await using ApiAuthHost host = await StartAsync(anonymous: 1, authenticated: 1, enabled: false);
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        string token = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());

        await SpendAsync(host, 5);
        await SpendAsync(host, 5, "/player", token: token);
    }

    /// <summary>
    /// The existing forwarded-headers rules decide the address: X-Forwarded-For from a peer that is
    /// not a trusted proxy is ignored, so a caller cannot pick a fresh partition per request.
    /// </summary>
    [Fact]
    public async Task Not_let_forwarded_for_from_an_untrusted_peer_change_the_partition()
    {
        await using ApiAuthHost host = await StartAsync();

        for (int i = 1; i <= 3; i++)
            Assert.Equal(HttpStatusCode.OK, await SendAsync(host, peer: OtherPeer, forwardedFor: $"198.51.100.{i}"));

        Assert.Equal(HttpStatusCode.TooManyRequests,
            await SendAsync(host, peer: OtherPeer, forwardedFor: "198.51.100.200"));
    }

    [Fact]
    public async Task Take_the_partition_from_a_trusted_proxys_forwarded_for()
    {
        await using ApiAuthHost host = await StartAsync();

        // Loopback is a trusted proxy by default.
        await SpendAsync(host, 3, forwardedFor: "198.51.100.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, forwardedFor: "198.51.100.1"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(host, forwardedFor: "198.51.100.2"));
    }

    /// <summary>The answer is the same whichever partition refused it, so it does not say which one did.</summary>
    [Fact]
    public async Task Answer_both_partitions_alike()
    {
        await using ApiAuthHost host = await StartAsync(anonymous: 1, authenticated: 1);
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        string token = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());

        await SpendAsync(host, 1);
        await SpendAsync(host, 1, "/player", token: token);
        using HttpResponseMessage anonymous = await SendForResponseAsync(host, "/player");
        using HttpResponseMessage signedIn = await SendForResponseAsync(host, "/player", token: token);

        Assert.Equal(HttpStatusCode.TooManyRequests, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, signedIn.StatusCode);
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(), await signedIn.Content.ReadAsStringAsync());
        Assert.Equal(anonymous.Content.Headers.ContentType, signedIn.Content.Headers.ContentType);
        Assert.NotNull(anonymous.Headers.RetryAfter);
        Assert.NotNull(signedIn.Headers.RetryAfter);
    }

    [Fact]
    public async Task Count_each_rejection_tagged_with_its_partition()
    {
        var measured = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DiagnosticsConfig.Api.ServiceName && instrument.Name == ApiRateLimiting.RejectionsMetric)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
                if (tag.Key == "partition")
                    lock (measured)
                        for (long i = 0; i < value; i++)
                            measured.Add(tag.Value as string);
        });
        listener.Start();

        await using ApiAuthHost host = await StartAsync(anonymous: 1, authenticated: 1);
        host.AccountNowIs(ApiAuthHost.MakeAccount());
        string token = ApiAuthHost.Mint(ApiAuthHost.MakeAccount());
        await SpendAsync(host, 1, peer: "198.51.100.77");
        await SpendAsync(host, 1, "/player", token: token);

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, peer: "198.51.100.77"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, "/player", token: token));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(host, "/player", token: token));

        lock (measured)
        {
            Assert.Equal(1, measured.Count(t => t == "anonymous"));
            Assert.Equal(2, measured.Count(t => t == "authenticated"));
            Assert.Equal(3, measured.Count);
        }
    }
}

/// <summary>
/// Runs alone: the rejection counter is static, so a class running beside it that got a 429 would
/// be counted in its assertions.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RateLimitingCollection
{
    public const string Name = "Rate limiting";
}
