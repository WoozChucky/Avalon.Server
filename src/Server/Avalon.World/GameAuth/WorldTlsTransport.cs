using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.World.GameAuth;

public sealed class WorldHostingSecurity
{
    /// <summary>The world TLS leaf (PEM), issued by the Avalon CA and renewed in place.</summary>
    public string CertificatePath { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    /// <summary>The Avalon CA certificate (PEM).</summary>
    public string CaCertificatePath { get; set; } = string.Empty;
}

/// <summary>
/// Loaded before listening. The context a handshake uses is rebuilt when the leaf or the CA file is renewed, so new
/// connections see the renewed certificate while open ones keep theirs.
/// </summary>
public sealed class WorldTlsTransport
{
    private sealed record Built(X509Certificate2 Leaf, X509Certificate2 Ca, SslStreamCertificateContext Context);

    private readonly ReloadingCertificate _leaf;
    private readonly ReloadingCertificate _ca;
    private readonly Lock _gate = new();
    private Built _built;

    public WorldTlsTransport(ReloadingCertificate leaf, ReloadingCertificate ca)
    {
        _leaf = leaf;
        _ca = ca;
        X509Certificate2 certificate = leaf.Current;
        if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            throw new InvalidOperationException("A current world TLS certificate with a private key is required.");
        _built = Build(certificate, ca.Current);
    }

    public SslStreamCertificateContext CurrentContext
    {
        get
        {
            // The two holders swap independently, so the context is paired with the references it was built from.
            X509Certificate2 leaf = _leaf.Current, ca = _ca.Current;
            Built built = Volatile.Read(ref _built);
            if (ReferenceEquals(built.Leaf, leaf) && ReferenceEquals(built.Ca, ca)) return built.Context;
            lock (_gate)
            {
                leaf = _leaf.Current; ca = _ca.Current;
                if (!ReferenceEquals(_built.Leaf, leaf) || !ReferenceEquals(_built.Ca, ca))
                    Volatile.Write(ref _built, Build(leaf, ca));
                return _built.Context;
            }
        }
    }

    private static Built Build(X509Certificate2 leaf, X509Certificate2 ca)
        => new(leaf, ca, SslStreamCertificateContext.Create(leaf, [ca], offline: true));

    public static async Task<SslStream> AuthenticateAsync(Stream network, SslStreamCertificateContext context, CancellationToken cancellationToken = default)
    {
        var stream = new SslStream(network, leaveInnerStreamOpen: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GameAuthPolicy.TransportTimeout);
        try
        {
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = context,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);
            return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
}
