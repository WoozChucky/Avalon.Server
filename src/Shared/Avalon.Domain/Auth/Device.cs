namespace Avalon.Domain.Auth;

public class Device
{
    public int? Id { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
    public bool Trusted { get; set; }
    public DateTime TrustEnd { get; set; }
    public DateTime LastUsage { get; set; }
}
