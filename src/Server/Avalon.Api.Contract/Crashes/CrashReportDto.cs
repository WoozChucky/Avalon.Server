using System.Text.Json.Serialization;

namespace Avalon.Api.Contract.Crashes;

public sealed record CrashReportDto(
    Guid Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Reason, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? ProcessedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? GroupId, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Version, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GitHash, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? ClientCrashedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? ExceptionCode, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExceptionAddress, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CrashingModule, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OsVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OsBuild,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Cpu, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GpuDriverModule, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GpuDriverVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GpuDriverSource,
    IReadOnlyList<string> Quality, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CrashStackDto? Analysis, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LogTail);
