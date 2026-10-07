using System.Net;
using System.Text;
using AspNet.Security.OpenId.Steam;
using Avalon.Api.Authentication;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class SteamWebCallbackShould
{
    [Fact]
    public async Task Verify_with_maintained_provider_fixed_urls_and_one_use_browser_proof_without_signing_in()
    {
        var memory = new AtomicAuthStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var crypto = new GameAuthCryptography(new byte[32]);
        var root = new Account { Id = new AccountId(7), Username = "PLAYER", Email = null, Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
        var accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(root.Id, false, Arg.Any<CancellationToken>()).Returns(root);
        var store = new SteamWebLinkStore(memory, crypto, clock);
        var transaction = (await store.StartAsync(Guid.NewGuid(), root, "browser", default))!;
        Assert.True(await store.ChallengeAsync(transaction.Id, transaction.Cookie, default));
        var provider = new ValidSteamResponse();
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SteamWebLinkOptions.Section + ":CallbackUrl"] = "https://api.example.test" + SteamWebLinkOptions.CallbackPath,
                [SteamWebLinkOptions.Section + ":SiteUrl"] = "https://web.example.test"
            }).Build());
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton(crypto);
            services.AddSingleton<IGameContextStore>(memory); services.AddSingleton(accounts);
            services.AddAuthentication("AvalonSteamLinkUnused");
            services.AddSteamWebLink();
            services.PostConfigure<SteamAuthenticationOptions>(SteamWebLinkOptions.Scheme, options => options.Backchannel = new HttpClient(provider));
        }).Configure(app =>
        {
            app.UseMiddleware<SteamOpenIdCallbackMiddleware>(); app.UseAuthentication();
            app.Run(async context =>
            {
                var properties = new AuthenticationProperties { RedirectUri = "https://web.example.test/account/link-store" };
                properties.Items[SteamWebLinkRegistration.TransactionProperty] = transaction.Id.ToString("N");
                await context.ChallengeAsync(SteamWebLinkOptions.Scheme, properties);
            });
        }));
        var client = server.CreateClient(); client.BaseAddress = new("https://api.example.test");
        using var challengeRequest = new HttpRequestMessage(HttpMethod.Get, "/begin");
        challengeRequest.Headers.Host = "attacker.example.test";
        using var challenge = await client.SendAsync(challengeRequest);
        var destination = challenge.Headers.Location!;
        Assert.Equal("steamcommunity.com", destination.Host);
        var parameters = QueryHelpers.ParseQuery(destination.Query);
        Assert.Equal("https://api.example.test", parameters["openid.realm"].ToString());
        var returnTo = parameters["openid.return_to"].ToString();
        Assert.StartsWith("https://api.example.test" + SteamWebLinkOptions.CallbackPath + "?state=", returnTo);
        var callbackValues = new Dictionary<string, string?>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = SteamWebLinkOptions.ProviderEndpoint,
            ["openid.claimed_id"] = "https://steamcommunity.com/openid/id/76561198000000001",
            ["openid.identity"] = "https://steamcommunity.com/openid/id/76561198000000001",
            ["openid.return_to"] = returnTo,
            ["openid.response_nonce"] = "2026-10-04T12:00:00Zunique",
            ["openid.assoc_handle"] = "association",
            ["openid.sig"] = "provider-signature",
            ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
        };
        var callback = QueryHelpers.AddQueryString(returnTo, callbackValues);
        var correlation = string.Join("; ", challenge.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        async Task<HttpResponseMessage> Submit()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, callback);
            request.Headers.Add("Cookie", correlation + "; " + SteamWebLinkRegistration.CookieName(transaction.Id) + "=" + transaction.Cookie);
            return await client.SendAsync(request);
        }
        using var verified = await Submit();
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Equal("https://web.example.test/account/link-store?steamLinkId=" + transaction.Id.ToString("N"), verified.Headers.Location!.AbsoluteUri);
        Assert.Equal("verified", (await store.ReadBoundAsync(transaction.Id, root.Id, "browser", transaction.Cookie, default))!.State);
        Assert.DoesNotContain(verified.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("__Host-AvalonSteamLinkUnused=", StringComparison.Ordinal));
        using var reused = await Submit();
        Assert.Contains("verification_failed", reused.Headers.Location!.AbsoluteUri);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Reject_unpinned_backchannel_destinations_and_oversized_responses()
    {
        using var pinned = new HttpClient(new SteamOpenIdBackchannelHandler(new ValidSteamResponse()));
        await Assert.ThrowsAsync<HttpRequestException>(() => pinned.PostAsync("https://attacker.example.test/", new StringContent("proof")));
        using var large = new HttpClient(new SteamOpenIdBackchannelHandler(new LargeSteamResponse()));
        await Assert.ThrowsAsync<HttpRequestException>(() => large.PostAsync(SteamWebLinkOptions.ProviderEndpoint, new StringContent("proof")));
    }
    private sealed class ValidSteamResponse : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(SteamWebLinkOptions.ProviderEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("check_authentication", await request.Content!.ReadAsStringAsync(ct));
            Calls++; return new(HttpStatusCode.OK) { Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n", Encoding.UTF8) };
        }
    }
    private sealed class LargeSteamResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('a', 16385)) });
    }
}
