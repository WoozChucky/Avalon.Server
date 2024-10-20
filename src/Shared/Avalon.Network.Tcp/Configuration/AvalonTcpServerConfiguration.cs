namespace Avalon.Network.Tcp.Configuration;

/// <summary>
/// The configuration for the <see cref="AvalonTcpServer"/>.
/// </summary>
public class AvalonTcpServerConfiguration
{
    /// <summary>
    /// Specifies whether the server is enabled or not.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// PFX certificate file path.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// PFX certificate password.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// The port where the server will listen on.
    /// </summary>
    public int ListenPort { get; set; } = 21000;

    /// <summary>
    /// The backlog for the server.
    /// </summary>
    public int Backlog { get; set; }
}
