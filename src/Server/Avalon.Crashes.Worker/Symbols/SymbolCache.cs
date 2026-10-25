using System.Text;
using System.Text.Json;
using Avalon.Crashes.Worker.Processing;

namespace Avalon.Crashes.Worker.Symbols;

/// <summary>Singleton local cache; all reads/copies and LRU writes share one gate. No report data belongs in this dedicated root.</summary>
public sealed class SymbolCache
{
    private readonly CrashSymbolOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;

    public SymbolCache(CrashSymbolOptions options, TimeProvider? timeProvider = null)
    {
        options.Validate();
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _root = Path.GetFullPath(options.CacheRoot);
        if (_root == Path.GetPathRoot(_root))
            throw new CrashSymbolException(CrashFailureCode.ConfigurationInvalid);
        SymbolFiles.VerifyPath(_root);
    }

    public async Task<string?> MaterializeAsync(ModuleIdentity identity, CrashAttemptWorkspace workspace, CancellationToken ct)
    {
        SymbolFiles.Require(identity);
        string scratch = Directory.GetParent(workspace.DirectoryPath)!.FullName;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(_root, scratch, comparison) || _root.StartsWith(scratch + Path.DirectorySeparatorChar, comparison)
            || scratch.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, comparison))
        {
            throw new CrashSymbolException(CrashFailureCode.ConfigurationInvalid);
        }
        await _gate.WaitAsync(ct);
        try
        {
            Evict(0);
            string path = CachePath(identity);
            SymbolFiles.VerifyPath(path);
            SymbolFiles.VerifyPath(path + ".json");
            if (!File.Exists(path) || !File.Exists(path + ".json"))
                return null;
            SymbolCacheMetadata? metadata;
            try
            {
                if (new FileInfo(path + ".json").Length > 4096)
                    return null;
                metadata = JsonSerializer.Deserialize<SymbolCacheMetadata>(await File.ReadAllTextAsync(path + ".json", ct));
                if (!await ValidateAsync(identity, metadata, path, ct))
                    return null;
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or DecoderFallbackException)
            {
                return null;
            }
            string result = await MaterializeFileAsync(identity, path, workspace, ct);
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ValidateAsync(ModuleIdentity identity, SymbolCacheMetadata? metadata, string source, CancellationToken ct)
    {
        return metadata is not null && metadata.ImageName == identity.Name && metadata.ImageKey == identity.ImageKey
            && metadata.DebugFile == identity.PdbName && metadata.DebugId == identity.PdbKey
            && metadata.CacheVersion == _options.CacheVersion && metadata.ConverterVersion == CrashSymbolOptions.ConverterVersion
            && metadata.Bytes is > 0 and <= 256L * 1024 * 1024 && new FileInfo(source).Length == metadata.Bytes
            && metadata.Sha256 == await SymbolFiles.DigestAsync(source, ct)
            && await SymbolIdentityValidator.ValidateConvertedAsync(identity, source, ct);
    }

    public async Task<SymbolCacheMetadata> PublishAsync(ModuleIdentity identity, string source, CancellationToken ct)
    {
        SymbolFiles.Require(identity);
        if (!await SymbolIdentityValidator.ValidateConvertedAsync(identity, source, ct))
            throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        await _gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            string path = CachePath(identity);
            SymbolFiles.VerifyPath(path);
            // Reserve the worst-case source plus metadata before creating any temporary bytes.
            long required = new FileInfo(source).Length + 4096;
            if (required > _options.LocalCacheBytes)
                required = new FileInfo(source).Length + 512;
            Evict(required);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var output = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
            {
                output.NewLine = "\n";
                await foreach (string line in SymbolIdentityValidator.LinesAsync(source, ct))
                {
                    string clean = line;
                    if (line.StartsWith("FILE ", StringComparison.Ordinal))
                    {
                        int index = line.IndexOf(' ', 5);
                        if (index < 0)
                            throw new InvalidDataException();
                        clean = line[..(index + 1)] + Basename(line[(index + 1)..]);
                    }
                    else if (line.StartsWith("INFO CODE_ID ", StringComparison.Ordinal))
                    {
                        int index = line.IndexOf(' ', 13);
                        if (index > 0)
                            clean = line[..(index + 1)] + Basename(line[(index + 1)..]);
                    }
                    await output.WriteLineAsync(clean.AsMemory(), ct);
                }
            }
            var metadata = new SymbolCacheMetadata(identity.Name, identity.ImageKey, identity.PdbName!, identity.PdbKey!,
                _options.CacheVersion, CrashSymbolOptions.ConverterVersion, await SymbolFiles.DigestAsync(temporary, ct), new FileInfo(temporary).Length);
            string json = JsonSerializer.Serialize(metadata);
            // Temp content already counts in the tree. Evict before writing the metadata too.
            Evict(Encoding.UTF8.GetByteCount(json), temporary);
            await File.WriteAllTextAsync(temporary + ".json", json, ct);
            File.Move(temporary, path, true);
            File.Move(temporary + ".json", path + ".json", true);
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
            return metadata;
        }
        finally
        {
            if (temporary is not null)
            {
                File.Delete(temporary);
                File.Delete(temporary + ".json");
            }
            _gate.Release();
        }
    }

    internal static async Task<string> MaterializeFileAsync(ModuleIdentity identity, string source, CrashAttemptWorkspace workspace, CancellationToken ct)
    {
        string temporary = await workspace.WriteExternalAsync(Guid.NewGuid().ToString("N") + ".sym", new FileInfo(source).Length,
            async (path, cap, token) =>
            {
                await using FileStream input = File.OpenRead(source);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                await SymbolFiles.CopyAsync(input, output, cap, null, token);
            }, ct);
        string destination = Path.Combine(workspace.DirectoryPath, "symbols", SymbolFiles.Relative(identity));
        SymbolFiles.VerifyPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(temporary, destination, true);
        return destination;
    }

    private string CachePath(ModuleIdentity identity)
    {
        SymbolFiles.Require(identity);
        return Path.Combine(_root, _options.CacheVersion, SymbolFiles.Relative(identity));
    }
    private static string Basename(string path) => path.Replace('\\', '/').Split('/')[^1];

    private void Evict(long required, string? keep = null)
    {
        SymbolFiles.VerifyPath(_root);
        Directory.CreateDirectory(_root);
        string[] files = SafeFiles(_root).ToArray();
        long total = files.Sum(p => new FileInfo(p).Length);
        IEnumerable<IGrouping<string, string>> entries = files.GroupBy(p => p.EndsWith(".sym.json", StringComparison.Ordinal) && File.Exists(p[..^5]) ? p[..^5] : p, StringComparer.Ordinal);
        foreach (IGrouping<string, string> entry in entries.OrderBy(e => File.GetLastWriteTimeUtc(e.Key)))
        {
            if (total + required <= _options.LocalCacheBytes)
                break;
            if (entry.Contains(keep, StringComparer.Ordinal))
                continue;
            foreach (string path in entry)
            {
                long bytes = new FileInfo(path).Length;
                File.Delete(path);
                total -= bytes;
            }
        }
        if (total + required > _options.LocalCacheBytes)
            throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
    }

    private static IEnumerable<string> SafeFiles(string root)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(root))
        {
            SymbolFiles.VerifyPath(path);
            if (Directory.Exists(path))
            {
                foreach (string file in SafeFiles(path))
                    yield return file;
            }
            else
            {
                yield return path;
            }
        }
    }
}
