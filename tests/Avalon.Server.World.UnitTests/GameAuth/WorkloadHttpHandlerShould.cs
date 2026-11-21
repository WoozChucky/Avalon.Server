using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.GameAuth;
using Avalon.World.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorkloadHttpHandlerShould : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("avalon-workload-").FullName;
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
        Directory.Delete(_directory, true);
    }

    private X509Certificate2 Track(X509Certificate2 certificate)
    {
        _disposables.Add(certificate);
        return certificate;
    }

    private ReloadingCertificate Holder(X509Certificate2 certificate, string name, FakeTimeProvider clock, bool withKey)
    {
        string certPath, keyPath;
        if (withKey) (certPath, keyPath) = TestCertificates.WritePem(certificate, _directory, name);
        else File.WriteAllText(certPath = Path.Combine(_directory, name + ".crt"), certificate.ExportCertificatePem());
        var holder = ReloadingCertificate.Load(certPath, withKey ? Path.Combine(_directory, name + ".key") : null, NullLogger.Instance, clock, Timeout.InfiniteTimeSpan);
        _disposables.Add(holder);
        return holder;
    }

    private HttpClient Client(ReloadingCertificate caHolder, ReloadingCertificate clientHolder, FakeTimeProvider clock)
    {
        var http = new HttpClient(new WorkloadHttpHandler(clientHolder, new ApiCertificateTrust(caHolder, clock), clock));
        _disposables.Add(http);
        return http;
    }

    [Theory]
    [InlineData("other-ca")]
    [InlineData("wrong-hostname")]
    [InlineData("expired")]
    [InlineData("client-auth-only")]
    public async Task Refuse_an_api_certificate_the_ca_did_not_issue_for_this_host_before_any_HTTP_request(string failure)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        X509Certificate2 ca = Track(TestCertificates.CreateCa("avalon-ca"));
        X509Certificate2 issuer = failure == "other-ca" ? Track(TestCertificates.CreateCa("rogue-ca")) : ca;
        X509Certificate2 server = Track(TestCertificates.CreateLeaf(issuer, failure == "wrong-hostname" ? "api.avalon.example" : "localhost",
            serverAuth: failure != "client-auth-only", notAfter: failure == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : null));
        X509Certificate2 client = Track(TestCertificates.CreateLeaf(ca, "world-one", serverAuth: false));
        // A bare TLS listener: Kestrel itself refuses a certificate without the server-auth EKU, which is one of the cases.
        await using var fixture = new RejectedHandshakeListener(server);
        using HttpClient http = Client(Holder(ca, "ca", clock, false), Holder(client, "client", clock, true), clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(fixture.Url));
        Assert.Equal(0, fixture.ApplicationBytes);
    }

    [Fact]
    public async Task Authenticate_over_real_mTLS_and_present_the_renewed_client_leaf_on_a_new_connection()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        X509Certificate2 ca = Track(TestCertificates.CreateCa("avalon-ca"));
        X509Certificate2 server = Track(TestCertificates.CreateLeaf(ca, "localhost", serverAuth: true));
        X509Certificate2 first = Track(TestCertificates.CreateLeaf(ca, "world-one", serverAuth: false));
        X509Certificate2 renewed = Track(TestCertificates.CreateLeaf(ca, "world-one", serverAuth: false));
        await using Listener fixture = await Listener.Start(server, ca);
        ReloadingCertificate clientHolder = Holder(first, "client", clock, true);
        using HttpClient http = Client(Holder(ca, "ca", clock, false), clientHolder, clock);

        Assert.Equal(first.Thumbprint, await http.GetStringAsync(fixture.Url));
        TestCertificates.WritePem(renewed, _directory, "client");
        Assert.True(clientHolder.TryReload());
        Assert.Equal(renewed.Thumbprint, await http.GetStringAsync(fixture.Url));
        Assert.Equal(2, fixture.Requests);

        // The API leaf's validity is re-checked on every send, even over a pooled connection.
        clock.SetUtcNow(new DateTimeOffset(server.NotAfter.ToUniversalTime()));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(fixture.Url));
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url);
        Assert.Throws<HttpRequestException>(() => http.Send(request));
        Assert.Equal(2, fixture.Requests);
    }

    /// <summary>Completes the server side of the handshake and counts any application data the client then sends.</summary>
    private sealed class RejectedHandshakeListener : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _accepting;
        private int _bytes;

        public RejectedHandshakeListener(X509Certificate2 server)
        {
            _listener.Start();
            _accepting = AcceptAsync(server);
        }

        public int ApplicationBytes => Volatile.Read(ref _bytes);
        public string Url => $"https://localhost:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

        private async Task AcceptAsync(X509Certificate2 server)
        {
            try
            {
                while (true)
                {
                    using TcpClient tcp = await _listener.AcceptTcpClientAsync();
                    try
                    {
                        await using var tls = new SslStream(tcp.GetStream());
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = server });
                        Interlocked.Add(ref _bytes, await tls.ReadAsync(new byte[1]));
                    }
                    catch (Exception ex) when (ex is IOException or AuthenticationException) { }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException) { }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _accepting;
        }
    }

    private sealed class Listener(WebApplication application) : IAsyncDisposable
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public string Url => application.Urls.Single().Replace("127.0.0.1", "localhost");

        public static async Task<Listener> Start(X509Certificate2 server, X509Certificate2 ca)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
                listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = server,
                    ClientCertificateMode = ClientCertificateMode.RequireCertificate,
                    ClientCertificateValidation = (presented, _, _) => AvalonCaTrust.Chains(presented, ca, DateTime.UtcNow)
                })));
            WebApplication app = builder.Build();
            var fixture = new Listener(app);
            app.MapGet("/", (HttpContext context) =>
            {
                Interlocked.Increment(ref fixture._requests);
                // One request per connection, so the next request has to present whatever leaf is current.
                context.Response.Headers.Connection = "close";
                return context.Connection.ClientCertificate?.Thumbprint;
            });
            await app.StartAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync() { await application.StopAsync(); await application.DisposeAsync(); }
    }
}
