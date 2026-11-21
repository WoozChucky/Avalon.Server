using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.World.GameAuth;

/// <summary>
/// The API's certificate must chain to exactly the Avalon CA (read from the reloading holder on every handshake, so a renewed
/// CA is honoured) and carry the server-authentication EKU and digital-signature usage. Chain errors alone are permitted
/// because the platform stores do not know the private CA; a name mismatch is always refused.
/// </summary>
public sealed class ApiCertificateTrust(ReloadingCertificate ca, TimeProvider clock)
{
    public const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";

    public bool Validate(X509Certificate2? peer, SslPolicyErrors errors)
    {
        if (peer is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None) return false;
        try
        {
            // Chains sets no EKU policy, so the server-auth EKU and key usage are checked here.
            return AvalonCaTrust.Chains(peer, ca.Current, clock.GetUtcNow().UtcDateTime) &&
                peer.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(extension =>
                    extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ServerAuthenticationEku)) &&
                peer.Extensions.OfType<X509KeyUsageExtension>().Any(extension =>
                    (extension.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0);
        }
        catch (CryptographicException) { return false; }
    }
}
