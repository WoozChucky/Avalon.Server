namespace Avalon.Crashes;

public sealed record StoredCrashObject(string Key, string ETag, long Size, DateTimeOffset LastModified);

public sealed record CrashMetadata(int Schema, string Exe, string Version, string GitHash, uint ExceptionCode, string ExceptionAddress, uint ThreadId, DateTimeOffset Time);

public sealed record CrashInput(string DumpPath, CrashMetadata Metadata, string? LogTail, RuntimeIdentity Runtime, CrashQualityFlags Quality)
{
    public required IReadOnlyList<ModuleIdentity> Modules { get; init; }
}

public sealed record RuntimeIdentity(string ImageName, string ImageKey, string PdbName, string PdbKey);

public sealed record ModuleIdentity(string Name, string ImageKey, string? PdbName, string? PdbKey, string? Version, ulong BaseAddress, ulong Size);

public sealed record DumpIdentity(RuntimeIdentity Runtime, string Architecture, IReadOnlyList<ModuleIdentity> Modules);

public sealed class CrashInputException(CrashFailureCode code) : Exception("Crash input rejected.")
{
    public CrashFailureCode Code { get; } = code;
}
