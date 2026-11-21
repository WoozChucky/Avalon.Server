using System.Net;
using System.Security.Cryptography.X509Certificates;
using Avalon.Api.Identity.Authentication;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameWorkloadListenerShould
{
    private const string WorldUri = AvalonCaTrust.ServerIdUriPrefix + "world-1";

    /// <summary>The CA, the server leaf's and the CA's PEM files, and the configuration naming them, in a directory of its own.</summary>
    private sealed class Pki : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("avalon-workload-").FullName;
        public X509Certificate2 Ca { get; } = TestCertificates.CreateCa("Avalon CA");
        public string LeafPath => Path.Combine(_directory, "server.crt");
        public string LeafKeyPath => Path.Combine(_directory, "server.key");
        public string CaPath => Path.Combine(_directory, "ca.crt");

        public Pki()
        {
            // The CA's key is not exportable on Windows, and only its certificate is a trust anchor.
            File.WriteAllText(CaPath, Ca.ExportCertificatePem());
            WriteServerLeaf();
        }

        /// <summary>Issues a new server leaf and writes it over the previous files, as cert-manager's renewal does.</summary>
        public string WriteServerLeaf()
        {
            using X509Certificate2 leaf = TestCertificates.CreateLeaf(Ca, "localhost", serverAuth: true);
            TestCertificates.WritePem(leaf, _directory, "server");
            return leaf.Thumbprint;
        }

        public Dictionary<string, string?> Configuration() => new(StringComparer.Ordinal)
        {
            ["Kestrel:Endpoints:GameInternal:Url"] = "https://127.0.0.1:0",
            ["Application:GameWorkloads:CertificatePath"] = LeafPath,
            ["Application:GameWorkloads:KeyPath"] = LeafKeyPath,
            ["Application:GameWorkloads:CaCertificatePath"] = CaPath,
            ["Application:GameWorkloads:Servers:0:ServerId"] = "world-1",
            ["Application:GameWorkloads:Servers:0:WorldId"] = "1",
            ["Application:GameWorkloads:Servers:0:TlsServerName"] = "localhost",
        };

        public void Dispose()
        {
            Ca.Dispose();
            try { Directory.Delete(_directory, true); }
            catch (IOException) { /* A leftover temp directory is harmless. */ }
        }
    }

    /// <summary>Starts the workload listener as the API composes it; its Kestrel certificate comes only from the selector.</summary>
    private static async Task<WebApplication> Start(Pki pki, TimeProvider clock, WarningCapture warnings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(warnings);
        builder.Configuration.AddInMemoryCollection(pki.Configuration());
        builder.Services.AddSingleton(clock);
        builder.Services.AddOptions<GameWorkloadConfiguration>().BindConfiguration("Application:GameWorkloads");
        builder.Services.AddIdentityAuthentication();
        builder.ConfigureGameWorkloadListener();
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/internal/test", (System.Security.Claims.ClaimsPrincipal user) => user.FindFirst(GameServerAuthHandler.ServerIdClaim)!.Value)
            .RequireAuthorization(GameServerAuthHandler.Scheme);
        await app.StartAsync();
        return app;
    }

    private static string AddressOf(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    [Fact]
    public async Task Enforce_client_certificate_possession_on_the_real_kestrel_tls_listener()
    {
        using var pki = new Pki();
        using X509Certificate2 workload = TestCertificates.CreateLeaf(pki.Ca, "world-1", serverAuth: false, WorldUri);
        // Same subject, SAN and server ID, issued by another CA: only the Avalon CA is trusted.
        using X509Certificate2 otherCa = TestCertificates.CreateCa("Other CA");
        using X509Certificate2 wrong = TestCertificates.CreateLeaf(otherCa, "world-1", serverAuth: false, WorldUri);
        var warnings = new WarningCapture();
        await using WebApplication app = await Start(pki, TimeProvider.System, warnings);
        try
        {
            string address = AddressOf(app);
            using HttpClient accepted = Client(workload, pki.Ca);
            HttpResponseMessage result = await accepted.PostAsync(address + "/internal/test", new StringContent(""));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("world-1", await result.Content.ReadAsStringAsync());
            foreach (X509Certificate2? certificate in new[] { null, wrong })
            {
                using HttpClient refused = Client(certificate, pki.Ca);
                refused.DefaultRequestHeaders.Authorization = new("Bearer", "user-token");
                refused.DefaultRequestHeaders.Add("X-Client-Cert", Convert.ToBase64String(workload.RawData));
                refused.DefaultRequestHeaders.Add("X-Server-Id", "world-1");
                try { Assert.False((await refused.PostAsync(address + "/internal/test", new StringContent(""))).IsSuccessStatusCode); }
                catch (HttpRequestException) { /* The TLS handshake may refuse before HTTP authorization. */ }
            }

            // The refused leaf is named by its subject and reason (#1051), never by the certificate itself.
            string warning = Assert.Single(warnings.Lines);
            Assert.Contains("CN=world-1", warning, StringComparison.Ordinal);
            Assert.Contains("not issued by the Avalon CA", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(wrong.RawData), warning, StringComparison.Ordinal);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Serve_the_renewed_server_leaf_to_new_workload_connections()
    {
        using var pki = new Pki();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using WebApplication app = await Start(pki, clock, new WarningCapture());
        try
        {
            string address = AddressOf(app);
            string before = await ServedThumbprint(address, pki.Ca);

            string renewed = pki.WriteServerLeaf();
            clock.Advance(ReloadingCertificateDefaults.PollInterval);

            Assert.NotEqual(before, renewed);
            Assert.Equal(renewed, await ServedThumbprint(address, pki.Ca));
        }
        finally { await app.StopAsync(); }
    }

    /// <summary>The thumbprint of the certificate a fresh connection to the listener is served, which must chain to the CA.</summary>
    private static async Task<string> ServedThumbprint(string address, X509Certificate2 ca)
    {
        string? served = null;
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                served = cert?.Thumbprint;
                return cert is not null && AvalonCaTrust.Chains(cert, ca, DateTime.UtcNow);
            },
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // No client certificate: the server certificate is read, then the listener refuses the connection.
        try { await client.PostAsync(address + "/internal/test", new StringContent("")); }
        catch (HttpRequestException) { /* RequireCertificate refuses after the server certificate was sent. */ }
        return served ?? throw new InvalidOperationException("The listener presented no certificate.");
    }

    [Theory]
    [InlineData("expired", "outside its validity period")]
    [InlineData("server-usage", "without the client authentication usage")]
    [InlineData("other-ca", "not issued by the Avalon CA")]
    [InlineData("no-uri", "naming no game server")]
    [InlineData("unassigned", "assigned to no game server")]
    [InlineData("assigned", null)]
    public void Name_why_it_refuses_a_client_certificate(string kind, string? refusal)
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("Avalon CA");
        using X509Certificate2 otherCa = TestCertificates.CreateCa("Other CA");
        using X509Certificate2 certificate = TestCertificates.CreateLeaf(kind == "other-ca" ? otherCa : ca, "world-1",
            serverAuth: kind == "server-usage",
            serverIdUri: kind switch { "no-uri" => null, "unassigned" => AvalonCaTrust.ServerIdUriPrefix + "world-9", _ => WorldUri },
            notAfter: kind == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddDays(1));
        var configuration = new GameWorkloadConfiguration();
        configuration.Servers.Add(new GameServerDefinition { ServerId = "world-1", WorldId = 1 });

        Assert.Equal(refusal, GameWorkloadHosting.RefusalOf(certificate, DateTime.UtcNow, ca, configuration));
    }

    /// <summary>
    /// Unknown certificates share one line a minute; a configured server's ID has its own, so junk handshakes cannot
    /// hide a real world's refusal (#1051).
    /// </summary>
    [Fact]
    public void Log_a_configured_servers_refusal_even_after_unknown_ones_took_the_shared_line()
    {
        var warnings = new WarningCapture();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(warnings));
        var log = new RefusedWorkloadCertificateLog(factory.CreateLogger<RefusedWorkloadCertificateLog>(), TimeProvider.System);

        log.Refused("assigned to no game server", "CN=scan-1", "world-8", assigned: false);
        log.Refused("assigned to no game server", "CN=scan-2", null, assigned: false);
        log.Refused("outside its validity period", "CN=world-1", "world-1", assigned: true);
        log.Refused("outside its validity period", "CN=world-1", "world-1", assigned: true);

        Assert.Equal(2, warnings.Lines.Count);
        Assert.Contains("server ID world-1", warnings.Lines[1], StringComparison.Ordinal);
    }

    /// <summary>A certificate from another CA naming world-1 does not take world-1's log line; a real world-1 leaf, even expired, does.</summary>
    [Fact]
    public void Give_a_configured_servers_log_line_only_to_certificates_of_the_avalon_ca()
    {
        using X509Certificate2 ca = TestCertificates.CreateCa("Avalon CA");
        using X509Certificate2 otherCa = TestCertificates.CreateCa("Other CA");
        using X509Certificate2 forged = TestCertificates.CreateLeaf(otherCa, "world-1", serverAuth: false, WorldUri);
        using X509Certificate2 expired = TestCertificates.CreateLeaf(ca, "world-1", serverAuth: false, WorldUri, DateTimeOffset.UtcNow.AddMinutes(-1));
        var configuration = new GameWorkloadConfiguration();
        configuration.Servers.Add(new GameServerDefinition { ServerId = "world-1", WorldId = 1 });
        var warnings = new WarningCapture();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(warnings));
        var log = new RefusedWorkloadCertificateLog(factory.CreateLogger<RefusedWorkloadCertificateLog>(), TimeProvider.System);

        string? forgedId = GameWorkloadHosting.TrustedServerIdOf(forged, ca, configuration);
        string? expiredId = GameWorkloadHosting.TrustedServerIdOf(expired, ca, configuration);
        log.Refused("not issued by the Avalon CA", forged.Subject, forgedId, forgedId is not null);
        log.Refused("outside its validity period", expired.Subject, expiredId, expiredId is not null);

        Assert.Null(forgedId);
        Assert.Equal("world-1", expiredId);
        Assert.Equal(2, warnings.Lines.Count);
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
    private static HttpClient Client(X509Certificate2? certificate, X509Certificate2 ca)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && AvalonCaTrust.Chains(cert, ca, DateTime.UtcNow),
        };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        // The limit ends a request that hangs; it does not time the listener. The accept, the handshake and the request
        // run on the thread pool, which every test in the process shares: with four processors and 149 work items
        // queued, the listener took a connection 2.4 s after the client dialled, and the first request took up to
        // 4.6 s of the 5 s it had here (#942). Every request still ended as the test expects.
        return new(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}
