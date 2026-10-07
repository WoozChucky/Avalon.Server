using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.World.GameAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorkloadHttpHandlerShould
{
    private const string ClientEku = "1.3.6.1.5.5.7.3.2";
    private static X509Certificate2 Certificate(string? eku = ApiCertificateTrust.ServerAuthenticationEku,
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        if (eku is not null) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(eku) }, false));
        using X509Certificate2 temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(temporary.Export(X509ContentType.Pfx), null);
    }
    private static string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AA")]
    [InlineData("invalid")]
    public void Refuse_missing_or_malformed_pins(string? pin)
    {
        Assert.False(ApiCertificateTrust.IsValidPin(pin));
        Assert.Throws<ArgumentException>(() => new ApiCertificateTrust(pin!, TimeProvider.System));
    }

    [Fact]
    public void Trust_only_the_exact_current_server_leaf_and_still_require_name_and_server_authentication()
    {
        using X509Certificate2 certificate = Certificate();
        using X509Certificate2 other = Certificate();
        using X509Certificate2 clientOnly = Certificate(ClientEku);
        using X509Certificate2 noEku = Certificate(null);
        using X509Certificate2 wrongUsage = Certificate(usage: X509KeyUsageFlags.KeyEncipherment);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trust = new ApiCertificateTrust(Pin(certificate), clock);
        Assert.True(trust.Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(trust.Validate(certificate, SslPolicyErrors.None));
        Assert.False(trust.Validate(other, SslPolicyErrors.None));
        Assert.False(trust.Validate(null, SslPolicyErrors.RemoteCertificateNotAvailable));
        Assert.False(trust.Validate(certificate, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(trust.Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
        foreach (X509Certificate2? invalid in new[] { clientOnly, noEku, wrongUsage })
            Assert.False(new ApiCertificateTrust(Pin(invalid), clock).Validate(invalid, SslPolicyErrors.None));
        var before = new FakeTimeProvider(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()).AddSeconds(-1));
        Assert.False(new ApiCertificateTrust(Pin(certificate), before).Validate(certificate, SslPolicyErrors.None));
        clock.SetUtcNow(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
        Assert.False(trust.Validate(certificate, SslPolicyErrors.None));
        Assert.False(ApiCertificateTrust.IsValidPin(new string('G', 64)));
        Assert.False(ApiCertificateTrust.IsValidPin(" " + Pin(certificate)));
    }

    [Fact]
    public async Task Authenticate_a_private_leaf_over_real_mTLS_and_refuse_an_expired_pooled_connection()
    {
        using X509Certificate2 server = Certificate();
        using X509Certificate2 client = Certificate(ClientEku);
        await using Listener fixture = await Listener.Start(server, client);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var http = new HttpClient(new WorkloadHttpHandler(client, Pin(server), clock));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(fixture.Url)).StatusCode);
        Assert.Equal(1, fixture.Requests);
        clock.SetUtcNow(new DateTimeOffset(server.NotAfter.ToUniversalTime()));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(fixture.Url));
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url);
        Assert.Throws<HttpRequestException>(() => http.Send(request));
        Assert.Equal(1, fixture.Requests);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("hostname")]
    [InlineData("expiry")]
    public async Task Refuse_wrong_pin_wrong_hostname_or_expiry_before_any_HTTP_request(string failure)
    {
        using X509Certificate2 server = Certificate();
        using X509Certificate2 client = Certificate(ClientEku);
        await using Listener fixture = await Listener.Start(server, client);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        if (failure == "expiry") clock.SetUtcNow(new DateTimeOffset(server.NotAfter.ToUniversalTime()));
        using var http = new HttpClient(new WorkloadHttpHandler(client, failure == "pin" ? new string('0', 64) : Pin(server), clock));
        string url = failure == "hostname" ? fixture.Url.Replace("localhost", "127.0.0.1") : fixture.Url;
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));
        Assert.Equal(0, fixture.Requests);
    }

    private sealed class Listener(WebApplication application) : IAsyncDisposable
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public string Url => application.Urls.Single().Replace("127.0.0.1", "localhost");
        public static async Task<Listener> Start(X509Certificate2 server, X509Certificate2 client)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
                listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = server,
                    ClientCertificateMode = ClientCertificateMode.RequireCertificate,
                    ClientCertificateValidation = (presented, _, _) => Pin(presented) == Pin(client)
                })));
            WebApplication app = builder.Build();
            var fixture = new Listener(app);
            app.MapGet("/", () => { Interlocked.Increment(ref fixture._requests); return "ok"; });
            await app.StartAsync();
            return fixture;
        }
        public async ValueTask DisposeAsync() { await application.StopAsync(); await application.DisposeAsync(); }
    }
}
