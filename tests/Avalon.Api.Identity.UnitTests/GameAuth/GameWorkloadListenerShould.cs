using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Api.Identity.Authentication;
using Avalon.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameWorkloadListenerShould
{
    [Fact]
    public async Task Enforce_client_certificate_possession_on_the_real_kestrel_tls_listener()
    {
        using X509Certificate2 server = Certificate(false);
        using X509Certificate2 workload = Certificate(true);
        using X509Certificate2 wrong = Certificate(true);
        string serverPin = Pin(server);
        string clientPin = Pin(workload);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        var warnings = new WarningCapture();
        builder.Logging.AddProvider(warnings);
        builder.Services.AddSingleton<RefusedWorkloadCertificateLog>();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Kestrel:Endpoints:GameInternal:Url"] = "https://127.0.0.1:0",
            ["Application:GameWorkloads:Servers:0:ServerId"] = "world-1",
            ["Application:GameWorkloads:Servers:0:WorldId"] = "1",
            ["Application:GameWorkloads:Servers:0:TlsServerName"] = "localhost",
            ["Application:GameWorkloads:Servers:0:TlsCertificateSha256"] = serverPin,
            ["Application:GameWorkloads:Servers:0:ClientCertificateSha256"] = clientPin,
        });
        builder.WebHost.ConfigureKestrel(o => o.ConfigureHttpsDefaults(h => h.ServerCertificate = server));
        builder.ConfigureGameWorkloadListener();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOptions<GameWorkloadConfiguration>().BindConfiguration("Application:GameWorkloads");
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, GameServerAuthHandler>(GameServerAuthHandler.Scheme, _ => { });
        builder.Services.AddAuthorization();
        await using WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/internal/test", (System.Security.Claims.ClaimsPrincipal user) => user.FindFirst(GameServerAuthHandler.ServerIdClaim)!.Value)
            .RequireAuthorization(new AuthorizationPolicyBuilder(GameServerAuthHandler.Scheme).RequireAuthenticatedUser().Build());
        await app.StartAsync();
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using HttpClient accepted = Client(workload, serverPin);
            HttpResponseMessage result = await accepted.PostAsync(address + "/internal/test", new StringContent(""));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("world-1", await result.Content.ReadAsStringAsync());
            foreach (X509Certificate2? certificate in new[] { null, wrong })
            {
                using HttpClient refused = Client(certificate, serverPin);
                refused.DefaultRequestHeaders.Authorization = new("Bearer", "user-token");
                refused.DefaultRequestHeaders.Add("X-Client-Cert", Convert.ToBase64String(workload.RawData));
                refused.DefaultRequestHeaders.Add("X-Server-Id", "world-1");
                try { Assert.False((await refused.PostAsync(address + "/internal/test", new StringContent(""))).IsSuccessStatusCode); }
                catch (HttpRequestException) { /* The TLS handshake may refuse before HTTP authorization. */ }
            }

            // The refused leaf is named by its pin and reason (#1051), never by the certificate itself.
            string warning = Assert.Single(warnings.Lines);
            Assert.Contains(Pin(wrong), warning, StringComparison.Ordinal);
            Assert.Contains("assigned to no game server", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(wrong.RawData), warning, StringComparison.Ordinal);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("expired", "outside its validity period")]
    [InlineData("server-usage", "without the client authentication usage")]
    [InlineData("unassigned", "assigned to no game server")]
    [InlineData("assigned", null)]
    public void Name_why_it_refuses_a_client_certificate(string kind, string? refusal)
    {
        using X509Certificate2 certificate = Certificate(client: kind != "server-usage",
            notAfter: kind == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddDays(1));
        var configuration = new GameWorkloadConfiguration();
        configuration.Servers.Add(new GameServerDefinition
        {
            ServerId = "world-1",
            WorldId = 1,
            ClientCertificateSha256 = kind == "unassigned" ? new string('0', 64) : Pin(certificate),
        });

        Assert.Equal(refusal, GameWorkloadHosting.RefusalOf(certificate, Pin(certificate), DateTime.UtcNow, configuration));
    }

    /// <summary>
    /// Unknown certificates share one line a minute; a configured server's pin has its own, so junk handshakes cannot
    /// hide a real world's refusal (#1051).
    /// </summary>
    [Fact]
    public void Log_a_configured_servers_refusal_even_after_unknown_ones_took_the_shared_line()
    {
        var warnings = new WarningCapture();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(warnings));
        var log = new RefusedWorkloadCertificateLog(factory.CreateLogger<RefusedWorkloadCertificateLog>(), TimeProvider.System);
        string world = new('A', 64);

        log.Refused("assigned to no game server", new string('1', 64), configured: false);
        log.Refused("assigned to no game server", new string('2', 64), configured: false);
        log.Refused("outside its validity period", world, configured: true);
        log.Refused("outside its validity period", world, configured: true);

        Assert.Equal(2, warnings.Lines.Count);
        Assert.Contains(world, warnings.Lines[1], StringComparison.Ordinal);
    }

    private sealed class WarningCapture : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => categoryName == typeof(RefusedWorkloadCertificateLog).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) lock (Lines) Lines.Add(formatter(state, exception));
        }
    }
    private static HttpClient Client(X509Certificate2? certificate, string serverPin)
    {
        var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && Pin(cert) == serverPin };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        // The limit ends a request that hangs; it does not time the listener. The accept, the handshake and the request
        // run on the thread pool, which every test in the process shares: with four processors and 149 work items
        // queued, the listener took a connection 2.4 s after the client dialled, and the first request took up to
        // 4.6 s of the 5 s it had here (#942). Every request still ended as the test expects.
        return new(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
    private static string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));
    private static X509Certificate2 Certificate(bool client, DateTimeOffset? notAfter = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
        // Windows Schannel cannot use the generator's ephemeral key. A default PFX import owns a
        // temporary key container that is deleted on disposal; no trust store or user is created.
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
    }
}
