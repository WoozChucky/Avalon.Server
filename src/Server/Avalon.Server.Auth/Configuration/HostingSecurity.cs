namespace Avalon.Server.Auth.Configuration;

public class HostingSecurity
{
    public string CertificatePath { get; set; } = string.Empty;
    public string? CertificatePassword { get; set; }
}
