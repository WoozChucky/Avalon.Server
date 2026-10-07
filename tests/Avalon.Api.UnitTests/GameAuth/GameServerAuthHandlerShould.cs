using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Avalon.Api.Authentication;
using Avalon.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameServerAuthHandlerShould
{
    private static X509Certificate2 Certificate(bool clientAuth)
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=world-1", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(clientAuth ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
    private static async Task<AuthenticateResult> Authenticate(X509Certificate2? presented, string configuredPin, bool https = true)
    {
        var monitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        monitor.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var configuration = Options.Create(new GameWorkloadConfiguration
        {
            Servers = [new GameServerDefinition
        { ServerId = "world-1", WorldId = 1, TlsServerName = "localhost", TlsCertificateSha256 = new string('A', 64), ClientCertificateSha256 = configuredPin }]
        });
        var handler = new GameServerAuthHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, configuration, TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Scheme = https ? "https" : "http";
        context.Connection.ClientCertificate = presented;
        context.Request.Headers.Authorization = "Bearer attacker-selected-user-token";
        context.Request.Headers["X-Server-Id"] = "world-2";
        context.Request.Headers["X-Client-Cert"] = presented is null ? "fake" : Convert.ToBase64String(presented.RawData);
        await handler.InitializeAsync(new AuthenticationScheme(GameServerAuthHandler.Scheme, null, typeof(GameServerAuthHandler)), context);
        return await handler.AuthenticateAsync();
    }
    [Fact]
    public async Task Derive_workload_identity_only_from_the_pinned_tls_client_certificate()
    {
        using var certificate = Certificate(true);
        var pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var result = await Authenticate(certificate, pin);
        Assert.True(result.Succeeded);
        Assert.Equal("world-1", result.Principal!.FindFirst(GameServerAuthHandler.ServerIdClaim)!.Value);
    }
    [Fact]
    public async Task Refuse_player_tokens_headers_wrong_pins_plain_http_and_server_only_certificates()
    {
        using var certificate = Certificate(true);
        var pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        Assert.False((await Authenticate(null, pin)).Succeeded);
        Assert.False((await Authenticate(certificate, new string('C', 64))).Succeeded);
        Assert.False((await Authenticate(certificate, pin, false)).Succeeded);
        using var serverCertificate = Certificate(false);
        Assert.False((await Authenticate(serverCertificate, Convert.ToHexString(SHA256.HashData(serverCertificate.RawData)))).Succeeded);
    }
}
