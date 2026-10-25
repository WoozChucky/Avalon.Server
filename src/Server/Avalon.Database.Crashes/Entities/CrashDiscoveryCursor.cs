namespace Avalon.Database.Crashes.Entities;

public sealed class CrashDiscoveryCursor
{
    public int Id { get; set; } = 1;
    public string? ContinuationToken { get; set; }
    public string? BackfillContinuationToken { get; set; }
    public DateTimeOffset? LastScanAt { get; set; }
    public DateTimeOffset? LastBackfillAt { get; set; }
}
