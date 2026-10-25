namespace Avalon.Database.Crashes.Entities;

public sealed class CrashDiscoveryVersion
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? LastVisitedAt { get; set; }
    public DateOnly? RecentDate { get; set; }
    public string? TodayToken { get; set; }
    public string? YesterdayToken { get; set; }
    public string? DatePrefixToken { get; set; }
    public bool DateInventoryReady { get; set; }
    public DateOnly? HistoricalDate { get; set; }
    public string? HistoricalToken { get; set; }
    public int Phase { get; set; }
}
