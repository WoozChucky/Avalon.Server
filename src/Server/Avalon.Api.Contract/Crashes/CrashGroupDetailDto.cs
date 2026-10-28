using System.Text.Json.Serialization;

namespace Avalon.Api.Contract.Crashes;

public sealed record CrashGroupDetailDto(
    CrashGroupDto Group, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Notes, DateTimeOffset AsOf, DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd, long RetainedReportCount, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? LatestReportId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LatestStackReceivedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CrashThreadDto? LatestStack, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IReadOnlyList<string>? LatestStackQuality,
    IReadOnlyList<CrashVersionCountDto> ByVersion, IReadOnlyList<CrashGpuDriverCountDto> ByGpuDriver,
    IReadOnlyList<CrashOsBuildCountDto> ByOsBuild, CrashReportPageDto Reports, IReadOnlyList<CrashGpuCountDto> ByGpu);

public sealed record CrashVersionCountDto(string Version, long Count);
public sealed record CrashGpuDriverCountDto(string Module, string Version, string Source, long Count);
/// <summary>The client's GPU and Vulkan driver (schema 2); "unknown" for reports without them.</summary>
public sealed record CrashGpuCountDto(string Name, string DriverVersion, long Count);
public sealed record CrashOsBuildCountDto(string Build, long Count);
public sealed record CrashReportPageDto(IReadOnlyList<CrashReportSummaryDto> Items, int Page, int PageSize, long Total);
public sealed record CrashReportSummaryDto(Guid Id, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Version, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? ExceptionCode, IReadOnlyList<string> Quality, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CrashingModule);
