using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.GameAuth;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldTlsTransportShould
{
    private sealed class Pki : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("world-tls").FullName;
        public X509Certificate2 Ca { get; } = TestCertificates.CreateCa(TestCertificates.UniqueName("avalon-ca"));
        public ReloadingCertificate LeafHolder { get; }
        public ReloadingCertificate CaHolder { get; }
        public WorldTlsTransport Transport { get; }

        public Pki()
        {
            using X509Certificate2 leaf = TestCertificates.CreateLeaf(Ca, "localhost", serverAuth: true);
            (string cert, string key) = TestCertificates.WritePem(leaf, _directory, "tls");
            string caPath = Path.Combine(_directory, "ca.crt");
            File.WriteAllText(caPath, Ca.ExportCertificatePem());
            LeafHolder = ReloadingCertificate.Load(cert, key, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan);
            CaHolder = ReloadingCertificate.Load(caPath, null, NullLogger.Instance, TimeProvider.System, Timeout.InfiniteTimeSpan);
            Transport = new WorldTlsTransport(LeafHolder, CaHolder);
        }

        public void Renew()
        {
            using X509Certificate2 leaf = TestCertificates.CreateLeaf(Ca, "localhost", serverAuth: true);
            TestCertificates.WritePem(leaf, _directory, "tls");
            Assert.True(LeafHolder.TryReload());
        }

        public void Dispose()
        {
            LeafHolder.Dispose(); CaHolder.Dispose(); Ca.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    /// <summary>Opens a TLS session to the transport, returning both ends and the certificate the client was shown.</summary>
    private static async Task<(SslStream Client, SslStream Server, X509Certificate2 Presented)> ConnectAsync(WorldTlsTransport transport)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var socket = new TcpClient();
            await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            TcpClient accepted = await listener.AcceptTcpClientAsync();
            Task<SslStream> serverTask = WorldTlsTransport.AuthenticateAsync(accepted.GetStream(), transport.CurrentContext);
            X509Certificate2? presented = null;
            var client = new SslStream(socket.GetStream(), false, (_, cert, _, _) =>
            {
                presented = X509CertificateLoader.LoadCertificate(cert!.GetRawCertData());
                return true;
            });
            await client.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 });
            return (client, await serverTask, presented!);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Protect_world_bytes_before_any_admission_packet_is_read()
    {
        using var pki = new Pki();
        (SslStream client, SslStream server, X509Certificate2 presented) = await ConnectAsync(pki.Transport);
        await using SslStream _ = client; await using SslStream __ = server;
        Assert.True(server.IsAuthenticated);
        Assert.True(server.IsEncrypted);
        Assert.True(AvalonCaTrust.Chains(presented, pki.Ca, DateTime.UtcNow));
        await client.WriteAsync(new byte[] { 42 });
        byte[] received = new byte[1];
        Assert.Equal(1, await server.ReadAsync(received));
        Assert.Equal(42, received[0]);
    }

    [Fact]
    public async Task Serve_the_renewed_leaf_to_new_handshakes_without_dropping_an_open_one()
    {
        using var pki = new Pki();
        (SslStream firstClient, SslStream firstServer, X509Certificate2 firstLeaf) = await ConnectAsync(pki.Transport);
        await using SslStream _ = firstClient; await using SslStream __ = firstServer;

        pki.Renew();
        (SslStream secondClient, SslStream secondServer, X509Certificate2 secondLeaf) = await ConnectAsync(pki.Transport);
        await using SslStream ___ = secondClient; await using SslStream ____ = secondServer;

        Assert.NotEqual(firstLeaf.Thumbprint, secondLeaf.Thumbprint);
        Assert.Equal(pki.LeafHolder.Current.Thumbprint, secondLeaf.Thumbprint);
        Assert.True(AvalonCaTrust.Chains(firstLeaf, pki.Ca, DateTime.UtcNow));
        Assert.True(AvalonCaTrust.Chains(secondLeaf, pki.Ca, DateTime.UtcNow));

        // The session opened before the renewal still carries bytes both ways.
        await firstClient.WriteAsync(new byte[] { 1 });
        byte[] received = new byte[1];
        Assert.Equal(1, await firstServer.ReadAsync(received));
        await firstServer.WriteAsync(new byte[] { 2 });
        Assert.Equal(1, await firstClient.ReadAsync(received));
        Assert.Equal(2, received[0]);
    }

    [Fact]
    public async Task Reject_plaintext_without_entering_packet_framing()
    {
        using var pki = new Pki();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        Task<SslStream> serverTask = WorldTlsTransport.AuthenticateAsync(accepted.GetStream(), pki.Transport.CurrentContext);
        await socket.GetStream().WriteAsync("plain-world-key"u8.ToArray());
        socket.Client.Shutdown(SocketShutdown.Send);
        await Assert.ThrowsAnyAsync<Exception>(async () => await serverTask);
    }
}
