using System.Buffers.Binary;
using System.Text;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Avalon.Crashes.Validation;

// ZIP32 only; this format cannot need ZIP64 inside the four MiB input bound.
internal static class BoundedZipReader
{
    internal static IReadOnlyDictionary<string, byte[]> Read(byte[] bytes, CancellationToken ct)
    {
        try
        {
            return ReadCore(bytes, ct);
        }
        catch (SharpZipBaseException)
        {
            throw Invalid();
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ReadCore(byte[] bytes, CancellationToken ct)
    {
        int end = FindEnd(bytes);
        if (U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0 || U16(bytes, end + 8) != U16(bytes, end + 10))
            throw Invalid();
        int count = U16(bytes, end + 10);
        if (count is < 2 or > 3)
            throw Invalid();
        int centralSize = CheckedInt(U32(bytes, end + 12));
        int centralStart = CheckedInt(U32(bytes, end + 16));
        if ((long)centralStart + centralSize != end)
            throw Invalid();
        _ = Slice(bytes, centralStart, centralSize);
        int cursor = centralStart;
        int expanded = 0;
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var ranges = new List<(int Start, int End)>();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            _ = Slice(bytes, cursor, 46);
            if (U32(bytes, cursor) != 0x02014b50)
                throw Invalid();
            int local = CheckedInt(U32(bytes, cursor + 42));
            ushort versionNeeded = U16(bytes, cursor + 6);
            uint modified = U32(bytes, cursor + 12);
            ushort flags = U16(bytes, cursor + 8);
            ushort method = U16(bytes, cursor + 10);
            // Only UTF-8, data descriptors and the standard deflate tuning bits are supported.
            if (versionNeeded is not 10 and not 20 || (method == 8 && versionNeeded < 20))
                throw Invalid();
            if ((flags & ~0x080e) != 0 || method is not 0 and not 8 || (method == 0 && (flags & 6) != 0))
                throw Invalid();
            uint crc = U32(bytes, cursor + 16);
            int compressed = CheckedInt(U32(bytes, cursor + 20));
            int length = CheckedInt(U32(bytes, cursor + 24));
            int nameLength = U16(bytes, cursor + 28);
            int extraLength = U16(bytes, cursor + 30);
            int commentLength = U16(bytes, cursor + 32);
            uint attributes = U32(bytes, cursor + 38);
            uint unixType = (attributes >> 16) & 0xf000;
            if (U16(bytes, cursor + 34) != 0 || (attributes & 0x418) != 0 || (unixType != 0 && unixType != 0x8000))
                throw Invalid();
            ReadOnlySpan<byte> nameBytes = Slice(bytes, cursor + 46, nameLength);
            string name = Encoding.ASCII.GetString(nameBytes);
            if (name is not "dump.dmp" and not "meta.json" and not "log-tail.txt" || entries.ContainsKey(name))
                throw Invalid();
            // Exact ASCII wire names also rule out decoder replacement and embedded separators/NULs.
            if (!nameBytes.SequenceEqual(Encoding.ASCII.GetBytes(name)))
                throw Invalid();
            CheckExtras(Slice(bytes, cursor + 46 + nameLength, extraLength));
            _ = Slice(bytes, cursor + 46 + nameLength + extraLength, commentLength);
            cursor += 46 + nameLength + extraLength + commentLength;
            if (cursor > end)
                throw Invalid();
            int cap = name switch
            {
                "meta.json" => CrashLimits.MetadataBytes,
                "log-tail.txt" => CrashLimits.LogBytes,
                _ => CrashLimits.ArchiveBytes
            };
            CrashFailureCode tooLarge = name switch
            {
                "meta.json" => CrashFailureCode.MetadataTooLarge,
                "log-tail.txt" => CrashFailureCode.LogTooLarge,
                _ => CrashFailureCode.ArchiveTooLarge
            };
            if (length > cap)
                throw new CrashInputException(tooLarge);
            if (length > CrashLimits.ArchiveBytes - expanded)
                throw new CrashInputException(CrashFailureCode.ArchiveTooLarge);
            expanded += length;
            _ = Slice(bytes, local, 30);
            if (U32(bytes, local) != 0x04034b50 || U16(bytes, local + 4) != versionNeeded || U32(bytes, local + 10) != modified || U16(bytes, local + 6) != flags || U16(bytes, local + 8) != method || U16(bytes, local + 26) != nameLength)
                throw Invalid();
            int localExtraLength = U16(bytes, local + 28);
            if (!Slice(bytes, local + 30, nameLength).SequenceEqual(nameBytes))
                throw Invalid();
            CheckExtras(Slice(bytes, local + 30 + nameLength, localExtraLength));
            if ((flags & 8) == 0)
            {
                if (U32(bytes, local + 14) != crc || U32(bytes, local + 18) != compressed || U32(bytes, local + 22) != length)
                    throw Invalid();
            }
            else if ((U32(bytes, local + 14) != 0 && U32(bytes, local + 14) != crc)
                || (U32(bytes, local + 18) != 0 && U32(bytes, local + 18) != compressed)
                || (U32(bytes, local + 22) != 0 && U32(bytes, local + 22) != length))
            {
                throw Invalid();
            }
            int data = local + 30 + nameLength + localExtraLength;
            _ = Slice(bytes, data, compressed);
            long dataEnd = (long)data + compressed;
            if (dataEnd > centralStart)
                throw Invalid();
            int localEnd = (int)dataEnd;
            if ((flags & 8) != 0)
            {
                if (U32(bytes, localEnd) == 0x08074b50)
                    localEnd += 4;
                _ = Slice(bytes, localEnd, 12);
                if (U32(bytes, localEnd) != crc || U32(bytes, localEnd + 4) != compressed || U32(bytes, localEnd + 8) != length)
                    throw Invalid();
                localEnd += 12;
            }
            if (localEnd > centralStart || ranges.Any(r => local < r.End && localEnd > r.Start))
                throw Invalid();
            ranges.Add((local, localEnd));
            byte[] output = Expand(bytes, data, compressed, length, method, ct);
            if (~Crc(uint.MaxValue, output) != crc || (name == "dump.dmp" && length == 0))
                throw Invalid();
            entries.Add(name, output);
        }
        if (cursor != end || !entries.ContainsKey("dump.dmp") || !entries.ContainsKey("meta.json"))
            throw Invalid();
        // Do not accept orphan local records or executable preambles between validated entries.
        int previousEnd = 0;
        foreach ((int start, int rangeEnd) in ranges.OrderBy(r => r.Start))
        {
            if (start != previousEnd)
                throw Invalid();
            previousEnd = rangeEnd;
        }
        if (previousEnd != centralStart)
            throw Invalid();
        return entries;
    }

    private static byte[] Expand(byte[] bytes, int offset, int compressed, int length, ushort method, CancellationToken ct)
    {
        byte[] output = new byte[length];
        if (method == 0)
        {
            if (compressed != length)
                throw Invalid();
            Slice(bytes, offset, length).CopyTo(output);
            return output;
        }
        var inflater = new Inflater(noHeader: true);
        inflater.SetInput(bytes, offset, compressed);
        byte[] buffer = new byte[8192];
        int total = 0;
        while (!inflater.IsFinished)
        {
            ct.ThrowIfCancellationRequested();
            int read = inflater.Inflate(buffer, 0, Math.Min(buffer.Length, length - total + 1));
            if (read == 0)
            {
                if (!inflater.IsFinished)
                    throw Invalid();
                break;
            }
            if (read > length - total)
                throw Invalid();
            buffer.AsSpan(0, read).CopyTo(output.AsSpan(total));
            total += read;
        }
        // Completion is separate from CRC: a truncated bitstream can yield the expected bytes and checksum.
        if (total != length || !inflater.IsFinished || inflater.RemainingInput != 0)
            throw Invalid();
        return output;
    }
    private static int FindEnd(byte[] bytes)
    {
        for (int i = bytes.Length - 22; i >= Math.Max(0, bytes.Length - 22 - ushort.MaxValue); i--)
        {
            if (U32(bytes, i) == 0x06054b50 && (long)i + 22 + U16(bytes, i + 20) == bytes.Length)
                return i;
        }
        throw Invalid();
    }

    private static void CheckExtras(ReadOnlySpan<byte> extra)
    {
        while (!extra.IsEmpty)
        {
            if (extra.Length < 4)
                throw Invalid();
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            // ZIP64, Unix link metadata and encryption are not supported.
            if (id is 0x0001 or 0x000d or 0x756e or 0x9901 || length > extra.Length - 4)
                throw Invalid();
            extra = extra[(length + 4)..];
        }
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return crc;
    }

    private static ReadOnlySpan<byte> Slice(byte[] bytes, int start, int length)
    {
        if (start < 0 || length < 0 || start > bytes.Length || length > bytes.Length - start)
            throw Invalid();
        return bytes.AsSpan(start, length);
    }

    private static ushort U16(byte[] bytes, int start) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(bytes, start, 2));
    private static uint U32(byte[] bytes, int start) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start, 4));
    private static int CheckedInt(uint value) => value <= int.MaxValue ? (int)value : throw Invalid();
    private static CrashInputException Invalid() => new(CrashFailureCode.UnsafeArchive);
}
