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
    /// <summary>
    /// <c>Application:GameWorkloads:CertificatePath</c> and <c>KeyPath</c>: the PEM server certificate (and its key) the
    /// workload listener presents, issued by the Avalon CA and renewed in place by cert-manager.
    /// </summary>
    public string CertificatePath { get; set; } = "";
    public string KeyPath { get; set; } = "";
    /// <summary><c>Application:GameWorkloads:CaCertificatePath</c>: the PEM Avalon CA a world's client certificate must chain to.</summary>
    public string CaCertificatePath { get; set; } = "";
    public List<GameServerDefinition> Servers { get; set; } = [];
    public void Validate()
    {
        if (Servers.Count > 256 || Servers.Any(s => s.WorldId == 0 || s.ServerId.Length is < 1 or > 64 ||
            !s.ServerId.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.') ||
            Uri.CheckHostName(s.TlsServerName) == UriHostNameType.Unknown) ||
            Servers.Select(s => s.ServerId).Distinct(StringComparer.Ordinal).Count() != Servers.Count ||
            Servers.Select(s => s.WorldId).Distinct().Count() != Servers.Count)
        {
            throw new InvalidOperationException("Invalid Application:GameWorkloads server, world or TLS name bindings.");
        }
        if (Servers.Count > 0 && (string.IsNullOrWhiteSpace(CertificatePath) || string.IsNullOrWhiteSpace(KeyPath) || string.IsNullOrWhiteSpace(CaCertificatePath)))
        {
            throw new InvalidOperationException(
                "Application:GameWorkloads:CertificatePath, KeyPath and CaCertificatePath are required when game servers are configured.");
        }
    }
}

public sealed class GameServerDefinition
{
    public string ServerId { get; set; } = "";
    public ushort WorldId { get; set; }
    public string TlsServerName { get; set; } = "";
}
