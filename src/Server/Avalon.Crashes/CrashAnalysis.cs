namespace Avalon.Crashes;

public sealed record ProcessorIdentity(string StackwalkVersion, string ConverterVersion, string CacheVersion);

public sealed record CrashAnalysis(
    int SchemaVersion,
    CrashIdentity Crash,
    CrashSystem System,
    IReadOnlyList<CrashThread> Threads,
    IReadOnlyList<CrashModule> Modules,
    CrashMetadata Metadata,
    string? LogTail,
    CrashQualityFlags Quality,
    ProcessorIdentity Processor);

public sealed record CrashIdentity(string? Reason, uint? Code, string? Address, int? CrashingThreadIndex, uint? ThreadId);

public sealed record CrashSystem(string? Os, string? OsVersion, string? OsBuild, string? CpuArchitecture, string? CpuInfo);

public sealed record CrashThread(int Index, uint? ThreadId, IReadOnlyList<CrashFrame> Frames);

public sealed record CrashFrame(
    string? Module,
    string? ModuleOffset,
    string? Function,
    string? Source,
    int? SourceLine,
    IReadOnlyList<CrashInlineFrame> InlineFrames,
    string? Trust)
{
    // Captured by validated address range before the upstream instruction address is discarded.
    // Basenames alone cannot distinguish two loaded images with the same name.
    public string? ImageKey { get; init; }
}

public sealed record CrashInlineFrame(string? Function, string? Source, int? SourceLine);

public sealed record CrashModule(
    string Name,
    string? ImageKey,
    string? PdbName,
    string? PdbKey,
    string? Version,
    string? BaseAddress,
    string? Size,
    bool SymbolsAvailable);
