namespace Avalon.Crashes.Worker.Symbols;

/// <summary>Bound streaming before writing. Originals are read-only; only completed converted files may be uploaded.</summary>
public interface ICrashSymbolStore
{
    Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct);
    Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct);
    Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct);
}
