using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.LocalDevelopment;

/// <summary>A PFX file a local run made, the password it is sealed with, and the SHA-256 of its DER leaf (its pin).</summary>
public sealed record LocalCertificate(string Path, string Password, string Sha256);

/// <summary>
/// The three private TLS leaves a local run of the world needs (docs/development-setup.md), each self-signed for
/// <see cref="TlsServerName"/> (and 127.0.0.1, ::1), RSA 2048, digital signature and key encipherment:
/// <list type="bullet">
/// <item><see cref="WorldTls"/>: the world server's own TLS leaf (server authentication), which players pin from the
/// join reply (<c>Hosting:Security</c> on the world, <c>TlsCertificateSha256</c> on the API).</item>
/// <item><see cref="Workload"/>: the world's client leaf on the API's game workload listener (client authentication;
/// <c>World:Admission:ClientCertificatePath</c>, <c>ClientCertificateSha256</c> on the API).</item>
/// <item><see cref="ApiInternal"/>: the API's game workload listener (server authentication;
/// <c>Kestrel:Endpoints:GameInternal:Certificate</c>, <c>World:Admission:ApiCertificateSha256</c> on the world).</item>
/// </list>
/// Each pin is the SHA-256 of the whole DER leaf, the form the API and the world compare. Shared, as source, by the
/// Aspire AppHost and tools/Avalon.LocalDev; never used outside a local run.
/// </summary>
public sealed record LocalCertificates(LocalCertificate WorldTls, LocalCertificate Workload, LocalCertificate ApiInternal)
{
    /// <summary>The name every leaf is issued for, and the TLS server name the API gives players for the world.</summary>
    public const string TlsServerName = "localhost";

    /// <summary>The server id the API assigns the local world's workload, and the world sends as its own.</summary>
    public const string ServerId = "world-1";

    /// <summary>The local world's id: the auth migrations seed world 1, the Development world.</summary>
    public const ushort WorldId = 1;

    /// <summary>The API's game workload listener's port, the chart's, and its origin.</summary>
    public const int GameInternalPort = 9443;

    public const string GameInternalUrl = "https://localhost:9443";

    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

    /// <summary>Makes the three leaves, valid from now for <paramref name="lifetime"/>, into <paramref name="directory"/>.</summary>
    public static async Task<LocalCertificates> CreateAsync(string directory, TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        return new(
            await WriteAsync(directory, "world-tls.pfx", "CN=Avalon local world TLS", ServerAuthentication, lifetime,
                cancellationToken),
            await WriteAsync(directory, "workload.pfx", "CN=Avalon local world workload", ClientAuthentication, lifetime,
                cancellationToken),
            await WriteAsync(directory, "api-game-internal.pfx", "CN=Avalon local API game workload listener",
                ServerAuthentication, lifetime, cancellationToken));
    }

    private static async Task<LocalCertificate> WriteAsync(string directory, string fileName, string subject, string usage,
        TimeSpan lifetime, CancellationToken cancellationToken)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(TlsServerName);
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.Add(lifetime));
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string path = System.IO.Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pfx, password), cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return new(path, password, Convert.ToHexString(SHA256.HashData(certificate.RawData)));
    }
}
