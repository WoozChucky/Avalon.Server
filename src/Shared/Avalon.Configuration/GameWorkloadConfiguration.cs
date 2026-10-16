namespace Avalon.Configuration;

/// <summary>Trusted deployment bindings. One world process per configured world in the initial admission protocol.</summary>
public sealed class GameWorkloadConfiguration
{
    /// <summary>
    /// <c>Application:GameWorkloads:MinClientVersion</c> (#949): the oldest client version game sign-in and world
    /// allocation accept, as <c>major.minor.patch</c>; a client at or above it is accepted. Nothing here supplies one:
    /// the API's <c>appsettings.json</c> holds the default, a deployment overrides it (Helm
    /// <c>gameAdmission.minClientVersion</c>), and identity refuses to start without a usable value.
    /// </summary>
    public string MinClientVersion { get; set; } = "";
    public List<GameServerDefinition> Servers { get; set; } = [];
    public void Validate()
    {
        if (Servers.Count > 256 || Servers.Any(s => s.WorldId == 0 || s.ServerId.Length is < 1 or > 64 ||
            !s.ServerId.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.') ||
            Uri.CheckHostName(s.TlsServerName) == UriHostNameType.Unknown || !Digest(s.TlsCertificateSha256) || !Digest(s.ClientCertificateSha256)) ||
            Servers.Select(s => s.ServerId).Distinct(StringComparer.Ordinal).Count() != Servers.Count ||
            Servers.Select(s => s.WorldId).Distinct().Count() != Servers.Count ||
            Servers.Select(s => s.ClientCertificateSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Servers.Count)
        {
            throw new InvalidOperationException("Invalid Application:GameWorkloads server, world, TLS name or certificate bindings.");
        }
    }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class GameServerDefinition
{
    public string ServerId { get; set; } = "";
    public ushort WorldId { get; set; }
    public string TlsServerName { get; set; } = "";
    public string TlsCertificateSha256 { get; set; } = "";
    public string ClientCertificateSha256 { get; set; } = "";
}
