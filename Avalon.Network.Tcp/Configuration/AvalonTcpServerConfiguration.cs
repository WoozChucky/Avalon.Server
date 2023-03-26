namespace Avalon.Network.Tcp.Configuration;

/// <summary>
/// The configuration for the <see cref="AvalonTcpServer"/>.
/// </summary>
public class AvalonTcpServerConfiguration
{
    /// <summary>
    /// Indicates whether the server should use SSL.
    /// </summary>
    public bool Ssl { get; set; } = false;
    
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
    public uint ListenPort { get; set; } = 21000;
}