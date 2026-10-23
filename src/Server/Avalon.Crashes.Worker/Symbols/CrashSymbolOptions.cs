namespace Avalon.Crashes.Worker.Symbols;

public sealed record CrashSymbolOptions
{
    public string CacheRoot { get; init; } = "/tmp/avalon-symbol-cache";
    public string CacheVersion { get; init; } = "dump-syms-2.3.9-inlines-cfi-v1";
    public const string ConverterVersion = "2.3.9";
    public const string StackwalkVersion = "0.27.0";
    public long LocalCacheBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long ArtifactBytes { get; init; } = 256L * 1024 * 1024;
    public long DownloadBytes { get; init; } = 1024L * 1024 * 1024;
    public bool PublicEnabled { get; init; } = true;
    public TimeSpan PublicTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public int PublicModules { get; init; } = 16;
    public Uri PublicOrigin { get; init; } = new("https://msdl.microsoft.com/download/symbols/");
    public IReadOnlyList<Uri> RedirectOrigins { get; init; } = [];
    public string OriginalBucket { get; init; } = "symbols";
    public string ConvertedBucket { get; init; } = "symbols-sym";

    internal void Validate()
    {
        if (!SymbolFiles.Segment(CacheVersion) || LocalCacheBytes is <= 0 or > 4L * 1024 * 1024 * 1024
            || ArtifactBytes is <= 0 or > 256L * 1024 * 1024 || DownloadBytes is <= 0 or > 1024L * 1024 * 1024
            || PublicModules is < 0 or > 16 || PublicTimeout <= TimeSpan.Zero || PublicTimeout > TimeSpan.FromSeconds(60)
            || PublicOrigin.Scheme != "https" || PublicOrigin.UserInfo.Length > 0 || PublicOrigin.Query.Length > 0 || PublicOrigin.Fragment.Length > 0
            || RedirectOrigins.Any(o => o.Scheme != "https" || o.UserInfo.Length > 0)
            || !SymbolFiles.Segment(OriginalBucket) || !SymbolFiles.Segment(ConvertedBucket)
            || OriginalBucket == ConvertedBucket || OriginalBucket == "crashes" || ConvertedBucket is "crashes" or "symbols")
        {
            throw new CrashSymbolException(CrashFailureCode.ConfigurationInvalid);
        }
    }
}

public sealed record SymbolCacheMetadata(string ImageName, string ImageKey, string DebugFile, string DebugId,
    string CacheVersion, string ConverterVersion, string Sha256, long Bytes);

public sealed record SymbolResolution(string BreakpadRoot, CrashQualityFlags Quality,
    ProcessorIdentity Processor, IReadOnlyList<ModuleIdentity> AvailableModules, bool CacheUploadFailed);

public sealed class CrashSymbolException(CrashFailureCode code) : Exception("Symbol resolution failed.")
{
    public CrashFailureCode Code { get; } = code;
}
