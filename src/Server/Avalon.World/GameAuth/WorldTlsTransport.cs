using Avalon.Common.GameAuth;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.World.GameAuth;

public sealed class WorldHostingSecurity
{
    public string CertificatePath { get; set; } = string.Empty;
    public string? CertificatePassword { get; set; }
}

/// <summary>Loaded before listening. World and workload credentials have separate identities.</summary>
public sealed class WorldTlsTransport : IDisposable
{
    public WorldTlsTransport(WorldHostingSecurity security)
        : this(X509CertificateLoader.LoadPkcs12FromFile(security.CertificatePath, security.CertificatePassword)) { }
    public WorldTlsTransport(X509Certificate2 certificate)
    {
        Certificate = certificate;
        if (!Certificate.HasPrivateKey || Certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            throw new InvalidOperationException("A current world TLS certificate with a private key is required.");
    }
    public X509Certificate2 Certificate { get; }
    public static async Task<SslStream> AuthenticateAsync(Stream network, X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        var stream = new SslStream(network, leaveInnerStreamOpen: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GameAuthPolicy.TransportTimeout);
        try
        {
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate, ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);
            return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public void Dispose() => Certificate.Dispose();
}
