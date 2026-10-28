using System.Text.Json.Serialization;

namespace Avalon.Api.Contract.Crashes;

public sealed record CrashStackDto(int SchemaVersion, CrashIdentityDto Crash, CrashSystemDto System,
    IReadOnlyList<CrashThreadDto> Threads, IReadOnlyList<CrashModuleDto> Modules, CrashMetadataDto Metadata,
    IReadOnlyList<string> Quality, CrashProcessorDto Processor);
public sealed record CrashIdentityDto([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Reason, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? Code, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Address, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? CrashingThreadIndex, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? ThreadId);
public sealed record CrashSystemDto([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Os, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OsVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OsBuild, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CpuArchitecture, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CpuInfo);
public sealed record CrashThreadDto(int Index, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? ThreadId, IReadOnlyList<CrashFrameDto> Frames);
public sealed record CrashFrameDto([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Module, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ModuleOffset, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Function, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? SourceLine, IReadOnlyList<CrashInlineFrameDto> InlineFrames, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Trust, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ImageKey);
public sealed record CrashInlineFrameDto([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Function, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Source, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? SourceLine);
public sealed record CrashModuleDto(string Name, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ImageKey, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PdbName, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PdbKey,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Version, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? BaseAddress, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Size, bool SymbolsAvailable);
public sealed record CrashMetadataDto(int Schema, string Exe, string Version, string GitHash, uint ExceptionCode,
    string ExceptionAddress, uint ThreadId, DateTimeOffset Time,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? GpuVendorId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] uint? GpuDeviceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GpuName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? VulkanDriverVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LastState);
public sealed record CrashProcessorDto(string StackwalkVersion, string ConverterVersion, string CacheVersion);
