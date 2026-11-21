using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Avalon.Api.Identity.Authentication;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameServerAuthHandlerShould
{
    private const string WorldPtr = AvalonCaTrust.ServerIdUriPrefix + "world-ptr";

    private static async Task<AuthenticateResult> Authenticate(X509Certificate2 ca, X509Certificate2? presented, bool https = true)
    {
        string directory = Directory.CreateTempSubdirectory("avalon-handler-ca-").FullName;
        try
        {
            string caPath = Path.Combine(directory, "ca.crt");
            File.WriteAllText(caPath, ca.ExportCertificatePem());
            using var trusted = ReloadingCertificate.Load(caPath, null, NullLogger.Instance, TimeProvider.System, ReloadingCertificateDefaults.PollInterval);
            ServiceProvider services = new ServiceCollection().AddKeyedSingleton(GameWorkloadHosting.CaKey, trusted).BuildServiceProvider();
            IOptionsMonitor<AuthenticationSchemeOptions> monitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
            monitor.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
            IOptions<GameWorkloadConfiguration> configuration = Options.Create(new GameWorkloadConfiguration
            {
                Servers =
                [
                    new GameServerDefinition { ServerId = "world-ptr", WorldId = 1, TlsServerName = "localhost" },
                    new GameServerDefinition { ServerId = "world-live", WorldId = 2, TlsServerName = "localhost" },
                ],
            });
            var handler = new GameServerAuthHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, configuration, TimeProvider.System, services);
            var context = new DefaultHttpContext();
            context.Request.Scheme = https ? "https" : "http";
            context.Connection.ClientCertificate = presented;
            context.Request.Headers.Authorization = "Bearer attacker-selected-user-token";
            context.Request.Headers["X-Server-Id"] = "world-live";
            context.Request.Headers["X-Client-Cert"] = presented is null ? "fake" : Convert.ToBase64String(presented.RawData);
            await handler.InitializeAsync(new AuthenticationScheme(GameServerAuthHandler.Scheme, null, typeof(GameServerAuthHandler)), context);
            return await handler.AuthenticateAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("world-ptr", "world-ptr")]
    [InlineData("no-uri", null)]
    [InlineData("two-uris", null)]
    [InlineData("unknown-id", null)]
    [InlineData("other-ca", null)]
    public async Task Authenticate_a_world_only_as_the_server_its_certificate_names(string kind, string? serverId)
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("Avalon CA");
        using X509Certificate2 otherCa = TestCertificates.CreateCa("Other CA");
        using X509Certificate2 certificate = TestCertificates.CreateLeaf(kind == "other-ca" ? otherCa : ca, "world-ptr", serverAuth: false,
            serverIdUri: kind switch { "no-uri" => null, "unknown-id" => AvalonCaTrust.ServerIdUriPrefix + "world-9", _ => WorldPtr },
            secondServerIdUri: kind == "two-uris" ? AvalonCaTrust.ServerIdUriPrefix + "world-live" : null);

        AuthenticateResult result = await Authenticate(ca, certificate);

        Assert.Equal(serverId is not null, result.Succeeded);
        // The caller's headers and token choose nothing: the identity is the certificate's.
        Assert.Equal(serverId, result.Principal?.FindFirst(GameServerAuthHandler.ServerIdClaim)?.Value);
    }

    [Fact]
    public async Task Refuse_missing_certificates_plain_http_and_server_only_certificates()
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("Avalon CA");
        using X509Certificate2 certificate = TestCertificates.CreateLeaf(ca, "world-ptr", serverAuth: false, WorldPtr);
        using X509Certificate2 serverCertificate = TestCertificates.CreateLeaf(ca, "world-ptr", serverAuth: true, WorldPtr);

        Assert.True((await Authenticate(ca, certificate)).Succeeded);
        Assert.False((await Authenticate(ca, null)).Succeeded);
        Assert.False((await Authenticate(ca, certificate, https: false)).Succeeded);
        Assert.False((await Authenticate(ca, serverCertificate)).Succeeded);
    }
}
