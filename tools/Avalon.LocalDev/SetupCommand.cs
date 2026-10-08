using System.Globalization;
using System.Security.Cryptography;
using Avalon.LocalDevelopment;

namespace Avalon.LocalDev;

/// <summary>
/// <c>setup</c>: what a plain <c>dotnet run</c> of the API and the world server needs and nothing commits. It makes the
/// world's three TLS leaves (<see cref="LocalCertificates"/>) into <c>certificates/local/</c>, which git ignores, and
/// writes, to each project's user-secrets (read in Development only): for the API, its listeners (http, https and the
/// game workload listener), the one world it allocates with the two pins, and its signing keys and game-auth host key
/// when it has none yet; for the world server, its TLS leaf and its admission settings. Run again, it makes new leaves
/// and rewrites those settings; the API's keys stay. Restart the API and the world server after it.
/// </summary>
public static class SetupCommand
{
    public const string ApiHttpUrl = "http://localhost:5210";
    public const string ApiHttpsUrl = "https://localhost:7166";

    private const string SigningKeySetting = "Application:Authentication:SigningKey";
    private const string HostKeySetting = "Application:GameAuth:HostKey";
    private const string LocalKeyId = "dev";

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string root = RepositoryRoot.Find();
        string api = Path.Combine(root, "src", "Server", "Avalon.Api");
        string world = Path.Combine(root, "src", "Server", "Avalon.Server.World");
        string directory = Path.Combine(root, "certificates", "local");

        LocalCertificates certificates = await LocalCertificates.CreateAsync(directory, TimeSpan.FromDays(365), cancellationToken);
        Console.Error.WriteLine($"Made the world TLS, workload and game workload listener certificates in {directory}");

        var apiSecrets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kestrel:Endpoints:PublicHttp:Url"] = ApiHttpUrl,
            ["Kestrel:Endpoints:Public:Url"] = ApiHttpsUrl,
            ["Kestrel:Endpoints:GameInternal:Url"] = LocalCertificates.GameInternalUrl,
            ["Kestrel:Endpoints:GameInternal:Certificate:Path"] = certificates.ApiInternal.Path,
            ["Kestrel:Endpoints:GameInternal:Certificate:Password"] = certificates.ApiInternal.Password,
            ["Application:GameWorkloads:Servers:0:ServerId"] = LocalCertificates.ServerId,
            ["Application:GameWorkloads:Servers:0:WorldId"] = LocalCertificates.WorldId.ToString(CultureInfo.InvariantCulture),
            ["Application:GameWorkloads:Servers:0:TlsServerName"] = LocalCertificates.TlsServerName,
            ["Application:GameWorkloads:Servers:0:TlsCertificateSha256"] = certificates.WorldTls.Sha256,
            ["Application:GameWorkloads:Servers:0:ClientCertificateSha256"] = certificates.Workload.Sha256,
        };

        IReadOnlyDictionary<string, string> existing = await Dotnet.ListSecretsAsync(api, cancellationToken);
        if (!existing.TryGetValue(SigningKeySetting, out string? signingKey) || string.IsNullOrWhiteSpace(signingKey))
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            apiSecrets[SigningKeySetting] = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
            apiSecrets["Application:Authentication:SigningKeyId"] = LocalKeyId;
            apiSecrets[$"Application:Authentication:ValidationKeys:{LocalKeyId}"] =
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            Console.Error.WriteLine($"Made the API's ES256 signing key, key id {LocalKeyId}");
        }

        if (!existing.TryGetValue(HostKeySetting, out string? hostKey) || string.IsNullOrWhiteSpace(hostKey))
        {
            apiSecrets[HostKeySetting] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            Console.Error.WriteLine("Made the API's game-auth host key");
        }

        await Dotnet.SetSecretsAsync(api, apiSecrets, cancellationToken);
        Console.Error.WriteLine("Wrote the API's user-secrets (src/Server/Avalon.Api)");

        await Dotnet.SetSecretsAsync(world, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Hosting:Security:CertificatePath"] = certificates.WorldTls.Path,
            ["Hosting:Security:CertificatePassword"] = certificates.WorldTls.Password,
            ["World:Admission:ApiUrl"] = LocalCertificates.GameInternalUrl + "/",
            ["World:Admission:ServerId"] = LocalCertificates.ServerId,
            ["World:Admission:ClientCertificatePath"] = certificates.Workload.Path,
            ["World:Admission:ClientCertificatePassword"] = certificates.Workload.Password,
            ["World:Admission:ApiCertificateSha256"] = certificates.ApiInternal.Sha256,
        }, cancellationToken);
        Console.Error.WriteLine("Wrote the world server's user-secrets (src/Server/Avalon.Server.World)");

        if (!await Dotnet.DevelopmentCertificateTrustedAsync(cancellationToken))
        {
            Console.Error.WriteLine(
                "The ASP.NET Core development certificate is missing or not trusted. The API serves https with it, and " +
                "the game client trusts only certificates this machine trusts: run `dotnet dev-certs https --trust` once.");
        }

        Console.Error.WriteLine($"Done. Restart the API and the world server. The API answers on {ApiHttpsUrl} and {ApiHttpUrl}.");
        return 0;
    }
}

/// <summary>The repository the tool was built in.</summary>
public static class RepositoryRoot
{
    /// <summary>Walks up from the build output to the folder holding Avalon.sln.</summary>
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new LocalDevException($"No Avalon.sln above {AppContext.BaseDirectory}.");
    }
}
