using System.Security.Cryptography;
using System.Text.Json;

namespace Avalon.Crashes.Worker.Symbols;

internal static class SymbolFiles
{
    internal static bool Segment(string? value) => value is { Length: > 0 and <= 128 } && value is not "." and not ".."
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    internal static bool Eligible(ModuleIdentity identity) => identity.SymbolLookupEligible
        && identity.ImageKey.Length is >= 9 and <= 16 && identity.ImageKey.All(char.IsAsciiHexDigit)
        && identity.PdbKey is { Length: >= 33 and <= 40 } && identity.PdbKey.All(char.IsAsciiHexDigit)
        && identity.PdbName!.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
        && (identity.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || identity.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

    internal static void Require(ModuleIdentity identity)
    {
        if (!Eligible(identity) || !identity.PdbName!.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            throw new CrashSymbolException(CrashFailureCode.RuntimeIdentityMismatch);
    }

    internal static string FileName(ModuleIdentity identity) => Path.GetFileNameWithoutExtension(identity.PdbName) + ".sym";
    internal static string Relative(ModuleIdentity identity) => $"{identity.PdbName}/{identity.PdbKey}/{FileName(identity)}";
    internal static string LockIdentity(ModuleIdentity identity) => JsonSerializer.Serialize(new[] { identity.Name, identity.ImageKey, identity.PdbName, identity.PdbKey });
    internal static async Task<string> DigestAsync(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    internal static void VerifyPath(string path)
    {
        for (FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path); item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbol path contains a link.");
        }
    }

    internal static async Task CopyAsync(Stream source, Stream destination, long cap, SymbolDownloadBudget? budget, CancellationToken ct)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, cap - total + 1)), ct);
            ct.ThrowIfCancellationRequested();
            if (read == 0)
                return;
            budget?.Charge(read);
            total += read;
            if (total > cap)
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

/// <summary>One instance per report attempt, shared by all retries, private/public/cache downloads. Charge the archive before resolving symbols.</summary>
public sealed class SymbolDownloadBudget
{
    private long _limit;
    private long _used;
    private readonly object _gate = new();
    public SymbolDownloadBudget(long limit = 1024L * 1024 * 1024)
    {
        if (limit is <= 0 or > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
    }
    public long UsedBytes => Interlocked.Read(ref _used);
    public long RemainingBytes { get { lock (_gate) return Math.Max(0, _limit - _used); } }
    internal void RestrictTo(long limit)
    {
        lock (_gate)
        {
            _limit = Math.Min(_limit, limit);
            if (_used > _limit)
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
        }
    }
    public void Charge(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            if (bytes > _limit - _used)
            {
                _used = _limit + 1;
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
            }
            _used += bytes;
        }
    }
}
