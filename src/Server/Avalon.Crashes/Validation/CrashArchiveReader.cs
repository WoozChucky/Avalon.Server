using System.Text;

namespace Avalon.Crashes.Validation;

public sealed class CrashArchiveReader
{
    public async Task<CrashInput> ReadAsync(Stream zip, string expectedVersion, string attemptDirectory, CancellationToken ct)
    {
        byte[] bytes = await ReadBoundedAsync(zip, ct).ConfigureAwait(false);
        IReadOnlyDictionary<string, byte[]> entries = BoundedZipReader.Read(bytes, ct);
        CrashMetadata metadata = CrashMetadataReader.Read(entries["meta.json"], expectedVersion);
        DumpIdentity identity = new MinidumpIdentityReader().Read(entries["dump.dmp"]);
        string? log = null;
        CrashQualityFlags quality = CrashQualityFlags.None;
        if (entries.TryGetValue("log-tail.txt", out byte[]? logBytes))
        {
            log = CrashText.Sanitize(Encoding.UTF8.GetString(logBytes), ref quality);
            if (Encoding.UTF8.GetByteCount(log) > CrashLimits.LogBytes)
            {
                var output = new StringBuilder(log.Length);
                int size = 0;
                foreach (Rune rune in log.EnumerateRunes())
                {
                    if (size + rune.Utf8SequenceLength > CrashLimits.LogBytes)
                        break;
                    output.Append(rune.ToString());
                    size += rune.Utf8SequenceLength;
                }
                log = output.ToString();
                quality |= CrashQualityFlags.Truncated;
            }
        }
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(attemptDirectory);
        // Inputs never choose a path. The caller owns a fresh random attempt directory.
        string dumpPath = Path.Combine(attemptDirectory, "dump.dmp");
        bool created = false;
        try
        {
            await using (var output = new FileStream(dumpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                created = true;
                await output.WriteAsync(entries["dump.dmp"], ct).ConfigureAwait(false);
            }
            return new CrashInput(dumpPath, metadata, log, identity.Runtime, quality) { Modules = identity.Modules };
        }
        catch
        {
            if (created)
                File.Delete(dumpPath);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek && stream.Length - stream.Position > CrashLimits.ArchiveBytes)
            throw new CrashInputException(CrashFailureCode.ArchiveTooLarge);
        long? declared = stream.CanSeek ? stream.Length - stream.Position : null;
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, CrashLimits.ArchiveBytes + 1 - (int)output.Length)), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > CrashLimits.ArchiveBytes)
                throw new CrashInputException(CrashFailureCode.ArchiveTooLarge);
            output.Write(buffer, 0, read);
        }
        if (declared.HasValue && declared.Value != output.Length)
            throw new CrashInputException(CrashFailureCode.UnsafeArchive);
        return output.ToArray();
    }
}
