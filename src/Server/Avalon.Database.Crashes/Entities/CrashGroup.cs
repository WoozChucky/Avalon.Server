using Avalon.Crashes;

namespace Avalon.Database.Crashes.Entities;

public sealed class CrashGroup
{
    public CrashGroupId Id { get; set; } = new(Guid.NewGuid());
    public int SignatureVersion { get; set; }
    public string SignatureHash { get; set; } = string.Empty;
    public string CanonicalSignature { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public long ReportCount { get; set; }
    public string FirstVersion { get; set; } = string.Empty;
    public string LastVersion { get; set; } = string.Empty;
    public Guid FirstReportId { get; set; }
    public Guid LastReportId { get; set; }
    public CrashGroupStatus Status { get; set; }
    public string? FixedInVersion { get; set; }
    public string? IssueUrl { get; set; }
    public string? Notes { get; set; }
    public bool IsRegression { get; set; }
    public long RegressionCount { get; set; }
    public DateTimeOffset? LastRegressionAt { get; set; }
    public long TriageRevision { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    public DateTimeOffset? FixedAt { get; set; }
}
