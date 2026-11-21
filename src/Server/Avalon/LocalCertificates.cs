using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.LocalDevelopment;

/// <summary>A PEM certificate a local run made and, for a leaf, the PEM file of its private key.</summary>
public sealed record LocalPem(string CertPath, string? KeyPath);

/// <summary>
/// The private Avalon CA of a local run and the three TLS leaves it issues (docs/development-setup.md), each for
/// <see cref="TlsServerName"/> (and 127.0.0.1, ::1), RSA 2048, all PEM:
/// <list type="bullet">
/// <item><see cref="Ca"/>: the CA, a self-signed certificate with no key file (<c>Hosting:Security:CaCertificatePath</c>
/// on the world, <c>Application:GameWorkloads:CaCertificatePath</c> on the API).</item>
/// <item><see cref="WorldTls"/>: the world server's own TLS leaf (server authentication) players verify against the CA
/// from the join reply (<c>Hosting:Security:CertificatePath</c>, <c>KeyPath</c> on the world).</item>
/// <item><see cref="Workload"/>: the world's client leaf on the API's game workload listener (client authentication),
/// carrying the URI SAN <c>urn:avalon:server:world-1</c> that names the server
/// (<c>World:Admission:ClientCertificatePath</c>, <c>ClientKeyPath</c>).</item>
/// <item><see cref="ApiInternal"/>: the API's game workload listener (server authentication;
/// <c>Application:GameWorkloads:CertificatePath</c>, <c>KeyPath</c>).</item>
/// </list>
/// Shared, as source, by the Aspire AppHost and tools/Avalon.LocalDev; never used outside a local run.
/// </summary>
public sealed record LocalCertificates(LocalPem Ca, LocalPem WorldTls, LocalPem Workload, LocalPem ApiInternal)
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

    private static readonly Uri s_serverIdUri = new("urn:avalon:server:" + ServerId);

    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

    /// <summary>Makes the CA and the three leaves, valid from now for <paramref name="lifetime"/>, into <paramref name="directory"/>.</summary>
    public static async Task<LocalCertificates> CreateAsync(string directory, TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset notBefore = now.AddMinutes(-5);
        DateTimeOffset notAfter = now.Add(lifetime);

        // Windows keeps every CA a chain was built through in the current user's store, and several with one subject
        // name break chain building there, so each CA gets a name of its own.
        string subject = $"CN=Avalon local CA {Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(subject, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
        using X509Certificate2 ca = caRequest.CreateSelfSigned(notBefore, notAfter);

        string caPath = System.IO.Path.Combine(directory, "ca.pem");
        await File.WriteAllTextAsync(caPath, ca.ExportCertificatePem(), cancellationToken);

        return new(
            new LocalPem(caPath, null),
            await WriteAsync(directory, "world-tls", "CN=Avalon local world TLS", ServerAuthentication, null, ca,
                notBefore, notAfter, cancellationToken),
            await WriteAsync(directory, "workload", "CN=Avalon local world workload", ClientAuthentication,
                s_serverIdUri, ca, notBefore, notAfter, cancellationToken),
            await WriteAsync(directory, "api-game-internal", "CN=Avalon local API game workload listener",
                ServerAuthentication, null, ca, notBefore, notAfter, cancellationToken));
    }

    private static async Task<LocalPem> WriteAsync(string directory, string name, string subject, string usage,
        Uri? serverId, X509Certificate2 ca, DateTimeOffset notBefore, DateTimeOffset notAfter,
        CancellationToken cancellationToken)
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
        if (serverId is not null) names.AddUri(serverId);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));

        using X509Certificate2 certificate = request.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
        string certPath = System.IO.Path.Combine(directory, name + ".pem");
        string keyPath = System.IO.Path.Combine(directory, name + ".key.pem");
        await File.WriteAllTextAsync(certPath, certificate.ExportCertificatePem(), cancellationToken);
        await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem(), cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return new(certPath, keyPath);
    }
}
