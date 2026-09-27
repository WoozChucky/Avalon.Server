using System.ComponentModel.DataAnnotations;

namespace Avalon.Server.Auth.Configuration;

public class HostingSecurity
{
    /// <summary>The TLS certificate the auth server loads at start; the password may be absent.</summary>
    [Required]
    public string CertificatePath { get; set; } = string.Empty;
    public string? CertificatePassword { get; set; }
}
