using System.Text.Json.Serialization;

namespace Avalon.Crashes;

[JsonConverter(typeof(StableCrashEnumConverter<CrashReportStatus>))]
public enum CrashReportStatus
{
    [JsonStringEnumMemberName("pending")] Pending = 0,
    [JsonStringEnumMemberName("processing")] Processing = 1,
    [JsonStringEnumMemberName("retry_wait")] RetryWait = 2,
    [JsonStringEnumMemberName("processed")] Processed = 3,
    [JsonStringEnumMemberName("rejected")] Rejected = 4,
    [JsonStringEnumMemberName("failed")] Failed = 5
}

[JsonConverter(typeof(StableCrashEnumConverter<CrashGroupStatus>))]
public enum CrashGroupStatus
{
    [JsonStringEnumMemberName("new")] New = 0,
    [JsonStringEnumMemberName("investigating")] Investigating = 1,
    [JsonStringEnumMemberName("fixed")] Fixed = 2,
    [JsonStringEnumMemberName("ignored")] Ignored = 3
}

[JsonConverter(typeof(StableCrashEnumConverter<CrashFailureCode>))]
public enum CrashFailureCode
{
    [JsonStringEnumMemberName("unsafe_archive")] UnsafeArchive = 0,
    [JsonStringEnumMemberName("archive_too_large")] ArchiveTooLarge = 1,
    [JsonStringEnumMemberName("metadata_too_large")] MetadataTooLarge = 2,
    [JsonStringEnumMemberName("log_too_large")] LogTooLarge = 3,
    [JsonStringEnumMemberName("invalid_metadata")] InvalidMetadata = 4,
    [JsonStringEnumMemberName("unsupported_schema")] UnsupportedSchema = 5,
    [JsonStringEnumMemberName("invalid_dump")] InvalidDump = 6,
    [JsonStringEnumMemberName("unsupported_architecture")] UnsupportedArchitecture = 7,
    [JsonStringEnumMemberName("runtime_identity_mismatch")] RuntimeIdentityMismatch = 8,
    [JsonStringEnumMemberName("object_changed")] ObjectChanged = 9,
    [JsonStringEnumMemberName("storage_unavailable")] StorageUnavailable = 10,
    [JsonStringEnumMemberName("missing_runtime_symbols")] MissingRuntimeSymbols = 11,
    [JsonStringEnumMemberName("tool_timeout")] ToolTimeout = 12,
    [JsonStringEnumMemberName("tool_failed")] ToolFailed = 13,
    [JsonStringEnumMemberName("resource_limit")] ResourceLimit = 14,
    [JsonStringEnumMemberName("invalid_analysis")] InvalidAnalysis = 15,
    [JsonStringEnumMemberName("ungroupable_analysis")] UngroupableAnalysis = 16,
    [JsonStringEnumMemberName("configuration_invalid")] ConfigurationInvalid = 17,
    [JsonStringEnumMemberName("access_denied")] AccessDenied = 18,
    [JsonStringEnumMemberName("object_missing")] ObjectMissing = 19
}

[Flags]
[JsonConverter(typeof(StableCrashEnumConverter<CrashQualityFlags>))]
public enum CrashQualityFlags
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("missingSystemSymbols")] MissingSystemSymbols = 1,
    [JsonStringEnumMemberName("scanFrames")] ScanFrames = 2,
    [JsonStringEnumMemberName("truncated")] Truncated = 4,
    [JsonStringEnumMemberName("metadataDisagreement")] MetadataDisagreement = 8,
    [JsonStringEnumMemberName("versionComparisonUnavailable")] VersionComparisonUnavailable = 16
}

public sealed class StableCrashEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false) where TEnum : struct, Enum;
