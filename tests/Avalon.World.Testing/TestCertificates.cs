using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.World.Testing;

/// <summary>A throwaway test CA and the leaves it issues. Self-contained (BCL only) so other test projects can link-compile it.</summary>
public static class TestCertificates
{
    private const string ServerAuthEku = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthEku = "1.3.6.1.5.5.7.3.2";

    public static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        return X509CertificateLoader.LoadPkcs12(temporary.Export(X509ContentType.Pfx), null);
    }

    public static X509Certificate2 CreateLeaf(X509Certificate2 ca, string dnsName, bool serverAuth, string? serverIdUri = null, DateTimeOffset? notAfter = null, string? secondServerIdUri = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        if (serverIdUri is not null) names.AddUri(new Uri(serverIdUri));
        if (secondServerIdUri is not null) names.AddUri(new Uri(secondServerIdUri));
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(serverAuth ? ServerAuthEku : ClientAuthEku) }, false));
        DateTimeOffset expires = notAfter ?? DateTimeOffset.UtcNow.AddDays(30);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(-1);
        if (start >= expires) start = expires.AddDays(-1);
        byte[] serial = RandomNumberGenerator.GetBytes(8);
        serial[0] &= 0x7F;
        using X509Certificate2 issued = request.Create(ca, start, expires, serial);
        using X509Certificate2 withKey = issued.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>Writes the certificate and its key as PEM files, the way cert-manager's Secret volume does.</summary>
    public static (string CertPath, string KeyPath) WritePem(X509Certificate2 cert, string directory, string name)
    {
        string certPath = Path.Combine(directory, name + ".crt");
        string keyPath = Path.Combine(directory, name + ".key");
        File.WriteAllText(certPath, cert.ExportCertificatePem());
        using ECDsa key = cert.GetECDsaPrivateKey() ?? throw new InvalidOperationException("The certificate has no EC private key.");
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        return (certPath, keyPath);
    }
}
