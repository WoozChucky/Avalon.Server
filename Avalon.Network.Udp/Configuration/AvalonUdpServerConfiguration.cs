namespace Avalon.Network.Udp.Configuration;

/// <summary>
/// The configuration for the <see cref="AvalonUdpServer"/>.
/// </summary>
public class AvalonUdpServerConfiguration
{
    /// <summary>
    /// PEM certificate file path.
    /// </summary>
    public string? CertificatePath { get; set; }
    
    /// <summary>
    /// The port where the server will listen on.
    /// </summary>
    public int ListenPort { get; set; } = 21500;

    /// <summary>
    /// The backlog for the server.
    /// </summary>
    public int Backlog { get; set; }
}
