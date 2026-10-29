namespace Avalon.Crashes;

public sealed record StoredCrashObject(string Key, string ETag, long Size, DateTimeOffset LastModified);

/// <summary>
/// A report's <c>meta.json</c>. Schema 2 (Client#459) adds what the client knew of its GPU (the Vulkan device it
/// rendered with, and its driver as the client words it, apart from the dump's <c>GpuDriverVersion</c>) and the game's
/// last state; each is optional and null when absent or malformed.
/// </summary>
public sealed record CrashMetadata(int Schema, string Exe, string Version, string GitHash, uint ExceptionCode, string ExceptionAddress, uint ThreadId, DateTimeOffset Time,
    uint? GpuVendorId = null, uint? GpuDeviceId = null, string? GpuName = null, string? VulkanDriverVersion = null, string? LastState = null);

public sealed record CrashInput(string DumpPath, CrashMetadata Metadata, string? LogTail, RuntimeIdentity Runtime, CrashQualityFlags Quality)
{
    public required IReadOnlyList<ModuleIdentity> Modules { get; init; }
}

public sealed record RuntimeIdentity(string ImageName, string ImageKey, string PdbName, string PdbKey);

public sealed record ModuleIdentity(string Name, string ImageKey, string? PdbName, string? PdbKey, string? Version, ulong BaseAddress, ulong Size)
{
    // This is a conservative name/identity prerequisite. Resolver allowlists and budgets still apply before I/O.
    public bool SymbolLookupEligible => IsSymbolName(Name) && IsSymbolName(PdbName) && PdbKey is not null;

    private static bool IsSymbolName(string? name) => name is { Length: > 0 and <= 128 }
        && name is not "." and not ".."
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

public sealed record DumpIdentity(RuntimeIdentity Runtime, string Architecture, IReadOnlyList<ModuleIdentity> Modules);

public sealed class CrashInputException(CrashFailureCode code) : Exception("Crash input rejected.")
{
    public CrashFailureCode Code { get; } = code;
}
