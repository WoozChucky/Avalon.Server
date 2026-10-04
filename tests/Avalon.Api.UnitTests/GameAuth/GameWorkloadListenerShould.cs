using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Api.Authentication;
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

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameWorkloadListenerShould
{
    [Fact]
    public async Task Enforce_client_certificate_possession_on_the_real_kestrel_tls_listener()
    {
        using var server = Certificate(false);
        using var workload = Certificate(true);
        using var wrong = Certificate(true);
        var serverPin = Pin(server);
        var clientPin = Pin(workload);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
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
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/internal/test", (System.Security.Claims.ClaimsPrincipal user) => user.FindFirst(GameServerAuthHandler.ServerIdClaim)!.Value)
            .RequireAuthorization(new AuthorizationPolicyBuilder(GameServerAuthHandler.Scheme).RequireAuthenticatedUser().Build());
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var accepted = Client(workload, serverPin);
            var result = await accepted.PostAsync(address + "/internal/test", new StringContent(""));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("world-1", await result.Content.ReadAsStringAsync());
            foreach (var certificate in new[] { null, wrong })
            {
                using var refused = Client(certificate, serverPin);
                refused.DefaultRequestHeaders.Authorization = new("Bearer", "user-token");
                refused.DefaultRequestHeaders.Add("X-Client-Cert", Convert.ToBase64String(workload.RawData));
                refused.DefaultRequestHeaders.Add("X-Server-Id", "world-1");
                try { Assert.False((await refused.PostAsync(address + "/internal/test", new StringContent(""))).IsSuccessStatusCode); }
                catch (HttpRequestException) { /* The TLS handshake may refuse before HTTP authorization. */ }
            }
        }
        finally { await app.StopAsync(); }
    }
    private static HttpClient Client(X509Certificate2? certificate, string serverPin)
    {
        var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && Pin(cert) == serverPin };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        return new(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }
    private static string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));
    private static X509Certificate2 Certificate(bool client)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows Schannel cannot use the generator's ephemeral key. A default PFX import owns a
        // temporary key container that is deleted on disposal; no trust store or user is created.
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
    }
}
