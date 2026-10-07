using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.World.GameAuth;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldTlsTransportShould
{
    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using X509Certificate2 temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        return X509CertificateLoader.LoadPkcs12(temporary.Export(X509ContentType.Pfx), null);
    }

    [Fact]
    public async Task Protect_world_bytes_before_any_admission_packet_is_read()
    {
        using X509Certificate2 certificate = Certificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        Task<SslStream> serverTask = WorldTlsTransport.AuthenticateAsync(accepted.GetStream(), certificate);
        await using var client = new SslStream(socket.GetStream(), false, (_, presented, _, _) =>
            presented is not null && Convert.ToHexString(SHA256.HashData(presented.GetRawCertData())) == Convert.ToHexString(SHA256.HashData(certificate.RawData)));
        await client.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 });
        await using SslStream server = await serverTask;
        Assert.True(server.IsAuthenticated);
        Assert.True(server.IsEncrypted);
        await client.WriteAsync(new byte[] { 42 });
        byte[] received = new byte[1];
        Assert.Equal(1, await server.ReadAsync(received));
        Assert.Equal(42, received[0]);
    }

    [Fact]
    public async Task Reject_plaintext_without_entering_packet_framing()
    {
        using X509Certificate2 certificate = Certificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        Task<SslStream> serverTask = WorldTlsTransport.AuthenticateAsync(accepted.GetStream(), certificate);
        await socket.GetStream().WriteAsync("plain-world-key"u8.ToArray());
        socket.Client.Shutdown(SocketShutdown.Send);
        await Assert.ThrowsAnyAsync<Exception>(async () => await serverTask);
    }

    [Fact]
    public async Task Wrong_certificate_is_not_an_authenticated_world()
    {
        using X509Certificate2 certificate = Certificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        Task<SslStream> serverTask = WorldTlsTransport.AuthenticateAsync(accepted.GetStream(), certificate);
        await using var client = new SslStream(socket.GetStream(), false, (_, _, _, _) => false);
        await Assert.ThrowsAsync<AuthenticationException>(() => client.AuthenticateAsClientAsync("localhost"));
        socket.Dispose();
        try { await using SslStream server = await serverTask; } catch (Exception) { }
    }
}
