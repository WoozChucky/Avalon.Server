namespace Avalon.Crashes.Persistence;

public sealed record DiscoveryCursor
{
    public const int VersionBatchSize = 100;
    public string? ContinuationToken { get; set; }
    public string? BackfillContinuationToken { get; set; }
    public DateTimeOffset? LastScanAt { get; set; }
    public DateTimeOffset? LastBackfillAt { get; set; }
    public IReadOnlyList<DiscoveryVersion> Versions { get; set; } = [];
    public IReadOnlyList<string> DiscoveredVersions { get; set; } = [];
    public IReadOnlyList<DiscoveryVersion> UpdatedVersions { get; set; } = [];
    public IReadOnlyList<DiscoveryDatePrefix> DiscoveredDates { get; set; } = [];
    public IReadOnlyList<DiscoveryDatePrefix> CompletedDates { get; set; } = [];
}

public sealed record DiscoveryVersion
{
    public required string Version { get; init; }
    public DateTimeOffset? LastVisitedAt { get; set; }
    public DateOnly? RecentDate { get; set; }
    public string? TodayToken { get; set; }
    public string? YesterdayToken { get; set; }
    public string? DatePrefixToken { get; set; }
    public bool DateInventoryReady { get; set; }
    public DateOnly? HistoricalDate { get; set; }
    public string? HistoricalToken { get; set; }
    public IReadOnlyList<DiscoveryDate> Dates { get; set; } = [];
    public int Phase { get; set; }
}

public sealed record DiscoveryDate(DateOnly Date, DateTimeOffset? LastScannedAt);
public sealed record DiscoveryDatePrefix(string Version, DateOnly Date);
