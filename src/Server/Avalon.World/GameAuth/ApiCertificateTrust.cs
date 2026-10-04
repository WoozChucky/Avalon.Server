using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.World.GameAuth;

/// <summary>The exact API leaf is the trust anchor; chain errors alone are permitted for private leaves.</summary>
public sealed class ApiCertificateTrust
{
    public const int Sha256HexLength = 64;
    public const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";
    private readonly byte[] _pin;
    private readonly TimeProvider _clock;

    public ApiCertificateTrust(string sha256, TimeProvider clock)
    {
        if (!IsValidPin(sha256)) throw new ArgumentException("An exact API leaf SHA-256 pin is required.", nameof(sha256));
        _pin = Convert.FromHexString(sha256);
        _clock = clock;
    }

    public static bool IsValidPin(string? value) => value is { Length: Sha256HexLength } && value.All(Uri.IsHexDigit);

    public bool Validate(X509Certificate2? certificate, SslPolicyErrors errors)
    {
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None) return false;
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            return certificate.NotBefore.ToUniversalTime() <= now && now < certificate.NotAfter.ToUniversalTime() &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData), _pin) &&
                certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(extension =>
                    extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ServerAuthenticationEku)) &&
                certificate.Extensions.OfType<X509KeyUsageExtension>().Any(extension =>
                    (extension.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0);
        }
        catch (CryptographicException) { return false; }
    }
}
