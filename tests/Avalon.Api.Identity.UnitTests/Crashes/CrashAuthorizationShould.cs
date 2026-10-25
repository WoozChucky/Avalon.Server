using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Crashes;

public class CrashAuthorizationShould
{
    [Theory]
    [InlineData(AccountAccessLevel.Admin, HttpStatusCode.ServiceUnavailable)]
    [InlineData(AccountAccessLevel.Console, HttpStatusCode.Forbidden)]
    [InlineData(AccountAccessLevel.GameMaster, HttpStatusCode.Forbidden)]
    [InlineData(AccountAccessLevel.Player, HttpStatusCode.Forbidden)]
    public async Task Require_explicit_admin_before_the_disabled_gate(AccountAccessLevel level, HttpStatusCode expected)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service]);
        Account account = ApiTestHost.MakeAccount(level);
        host.AccountNowIs(account);
        using HttpResponseMessage response = await host.GetAsync("/admin/crashes/groups?page=bad", ApiTestHost.Mint(account));
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (expected == HttpStatusCode.ServiceUnavailable)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("CRASH_ANALYSIS_DISABLED", json.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Refuse_anonymous_pat_and_cookie_only_callers()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service]);
        Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        host.AccountNowIs(account);
        using HttpResponseMessage anonymous = await host.Client.GetAsync("/admin/crashes/groups");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(anonymous.Headers.CacheControl?.NoStore);
        using var cookie = new HttpRequestMessage(HttpMethod.Get, "/admin/crashes/groups");
        cookie.Headers.Add("Cookie", $"AVToken={ApiTestHost.Mint(account)}");
        using HttpResponseMessage cookieResponse = await host.Client.SendAsync(cookie);
        Assert.Equal(HttpStatusCode.Forbidden, cookieResponse.StatusCode);
        string token = "avp_" + new string('A', 43);
        host.PatIs(token, new PersonalAccessToken
        {
            Id = new PersonalAccessTokenId(5),
            AccountId = account.Id,
            Roles = AccountAccessLevel.Admin,
            Name = "crash-test",
            TokenPrefix = token[..8],
            TokenHash = [],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        using var pat = new HttpRequestMessage(HttpMethod.Get, "/admin/crashes/groups");
        pat.Headers.Authorization = new AuthenticationHeaderValue("Avalon", token);
        using HttpResponseMessage patResponse = await host.Client.SendAsync(pat);
        Assert.Equal(HttpStatusCode.Forbidden, patResponse.StatusCode);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("credentials")]
    [InlineData("demoted")]
    public async Task Revalidate_account_before_crash_access(string change)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service]);
        Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        string token = ApiTestHost.Mint(account);
        if (change == "banned") account.Status = AccountStatus.Banned;
        if (change == "credentials") account.CredentialsVersion++;
        if (change == "demoted") account.AccessLevel = AccountAccessLevel.Player;
        host.AccountNowIs(account);
        using HttpResponseMessage response = await host.GetAsync("/admin/crashes/groups", token);
        Assert.Equal(change == "demoted" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task Refuse_a_valid_workload_certificate_without_an_account_session()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=crash-test-workload", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Configure = services => services.Configure<GameWorkloadConfiguration>(options => options.Servers =
            [new GameServerDefinition
            {
                ServerId = "crash-test-world",
                WorldId = 1,
                TlsServerName = "localhost",
                TlsCertificateSha256 = new string('A', 64),
                ClientCertificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            }]),
        });
        using (IServiceScope scope = host.Services.CreateScope())
        {
            var authentication = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            authentication.Request.Scheme = "https";
            authentication.Connection.ClientCertificate = certificate;
            AuthenticateResult accepted = await authentication.AuthenticateAsync(GameServerAuthHandler.Scheme);
            Assert.True(accepted.Succeeded);
        }
        var server = (TestServer)host.Services.GetRequiredService<IServer>();
        HttpContext response = await server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Path = "/admin/crashes/groups";
            context.Request.Scheme = "https";
            context.Connection.ClientCertificate = certificate;
        });
        Assert.Equal(401, response.Response.StatusCode);
        Assert.Equal("no-store", response.Response.Headers.CacheControl);
    }
}
