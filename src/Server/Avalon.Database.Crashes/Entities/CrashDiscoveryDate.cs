namespace Avalon.Database.Crashes.Entities;

public sealed class CrashDiscoveryDate
{
    public string Version { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
}
