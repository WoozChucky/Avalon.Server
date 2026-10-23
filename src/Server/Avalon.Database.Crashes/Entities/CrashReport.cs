using Avalon.Crashes;

namespace Avalon.Database.Crashes.Entities;

public sealed class CrashReport
{
    public CrashReportId Id { get; set; } = new(Guid.NewGuid());
    public string ObjectKey { get; set; } = string.Empty;
    public string SourceETag { get; set; } = string.Empty;
    public long SourceSize { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? Version { get; set; }
    public string? GitHash { get; set; }
    public string? Meta { get; set; }
    public DateTimeOffset? ClientCrashedAt { get; set; }
    public CrashReportStatus Status { get; set; }
    public CrashFailureCode? ReasonCode { get; set; }
    public string? ReasonDetail { get; set; }
    public long AttemptCount { get; set; }
    public DateTimeOffset? FirstAttemptAt { get; set; }
    public DateTimeOffset? RetryCampaignStartedAt { get; set; }
    public long ConsecutiveToolFailures { get; set; }
    public DateTimeOffset? LastManualRetryAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? CompletionToken { get; set; }
    public CrashGroupId? GroupId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int? SignatureVersion { get; set; }
    public long? ExceptionCode { get; set; }
    public string? ExceptionAddress { get; set; }
    public string? CrashingModule { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public string? Cpu { get; set; }
    public string? GpuDriverModule { get; set; }
    public string? GpuDriverVersion { get; set; }
    public string? GpuDriverSource { get; set; }
    public string? Stack { get; set; }
    public int? StackSchemaVersion { get; set; }
    public CrashQualityFlags QualityFlags { get; set; }
    public string? LogTail { get; set; }
    public string? ProcessorVersion { get; set; }
    public string? ConverterVersion { get; set; }
    public string? CacheVersion { get; set; }
}
