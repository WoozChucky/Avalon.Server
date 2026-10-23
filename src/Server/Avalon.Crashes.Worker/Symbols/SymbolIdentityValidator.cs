using System.Buffers.Binary;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Avalon.Crashes.Worker.Symbols;

public static class SymbolIdentityValidator
{
    public static async Task<bool> ValidatePairAsync(ModuleIdentity identity, string imagePath, string pdbPath, CancellationToken ct)
    {
        try
        {
            SymbolFiles.Require(identity);
            if (new FileInfo(imagePath).Length > 256L * 1024 * 1024 || new FileInfo(pdbPath).Length > 256L * 1024 * 1024)
                return false;
            ct.ThrowIfCancellationRequested();
            await using var image = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pe = new PEReader(image);
            PEHeaders headers = pe.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 || headers.PEHeader is null)
                return false;
            string imageId = unchecked((uint)headers.CoffHeader.TimeDateStamp).ToString("X8", CultureInfo.InvariantCulture)
                + headers.PEHeader.SizeOfImage.ToString("x", CultureInfo.InvariantCulture);
            DebugDirectoryEntry[] entries = pe.ReadDebugDirectory().Where(e => e.Type == DebugDirectoryEntryType.CodeView).ToArray();
            if (entries.Length != 1 || imageId != identity.ImageKey)
                return false;
            CodeViewDebugDirectoryData cv = pe.ReadCodeViewDebugDirectoryData(entries[0]);
            string debugId = cv.Guid.ToString("N").ToUpperInvariant() + cv.Age.ToString("X", CultureInfo.InvariantCulture);
            string name = cv.Path.Replace('\\', '/').Split('/')[^1];
            return debugId == identity.PdbKey && string.Equals(name, identity.PdbName, StringComparison.OrdinalIgnoreCase)
                && await ReadPdbIdentityAsync(pdbPath) == debugId;
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or ArgumentException or OverflowException or CrashSymbolException)
        {
            return false;
        }
    }

    private static async Task<string> ReadPdbIdentityAsync(string path)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] header = new byte[56];
        file.ReadExactly(header);
        if (!header.AsSpan(0, 32).SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\x1a\x44\x53\0\0\0"u8))
            throw new InvalidDataException();
        int blockSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(32)));
        int directorySize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44)));
        uint blockMap = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(52));
        if (blockSize is < 512 or > 65536 || (blockSize & (blockSize - 1)) != 0 || directorySize is < 12 or > 16 * 1024 * 1024)
            throw new InvalidDataException();
        int count = checked((directorySize + blockSize - 1) / blockSize);
        if (count * 4 > blockSize)
            throw new InvalidDataException();
        byte[] blocks = ReadAt(file, (long)blockMap * blockSize, count * 4);
        byte[] directory = new byte[directorySize];
        for (int i = 0; i < count; i++)
        {
            uint block = BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(i * 4));
            byte[] data = ReadAt(file, (long)block * blockSize, Math.Min(blockSize, directorySize - i * blockSize));
            data.CopyTo(directory, i * blockSize);
        }
        uint streamCount = BinaryPrimitives.ReadUInt32LittleEndian(directory);
        if (streamCount < 2 || streamCount > (directorySize - 4) / 4)
            throw new InvalidDataException();
        uint firstSize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(4));
        uint infoSize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(8));
        long index = 4 + streamCount * 4L + (firstSize == uint.MaxValue ? 0 : ((firstSize + (long)blockSize - 1) / blockSize) * 4);
        if (infoSize < 28 || index > directory.Length - 4)
            throw new InvalidDataException();
        uint infoBlock = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan((int)index));
        byte[] info = ReadAt(file, (long)infoBlock * blockSize, 28);
        return new Guid(info.AsSpan(12, 16)).ToString("N").ToUpperInvariant()
            + BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(8)).ToString("X", CultureInfo.InvariantCulture);
    }

    private static byte[] ReadAt(FileStream file, long offset, int size)
    {
        if (offset < 0 || size > file.Length - offset)
            throw new InvalidDataException();
        file.Position = offset;
        byte[] bytes = new byte[size];
        file.ReadExactly(bytes);
        return bytes;
    }

    public static async Task<bool> ValidateConvertedAsync(ModuleIdentity identity, string path, CancellationToken ct)
    {
        bool function = false, unwind = false, header = false;
        if (!SymbolFiles.Eligible(identity) || new FileInfo(path).Length > 256L * 1024 * 1024)
            return false;
        await foreach (string line in LinesAsync(path, ct))
        {
            if (!header)
            {
                if (line != $"MODULE windows x86_64 {identity.PdbKey} {identity.PdbName}")
                    return false;
                header = true;
            }
            else if (line.StartsWith("MODULE ", StringComparison.Ordinal))
            {
                return false;
            }
            if (line.StartsWith("INFO CODE_ID ", StringComparison.Ordinal))
            {
                string[] fields = line.Split(' ', 4);
                if (fields.Length != 4 || !string.Equals(fields[2], identity.ImageKey, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(fields[3].Replace('\\', '/').Split('/')[^1], identity.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            if (line.StartsWith("FUNC ", StringComparison.Ordinal))
            {
                string value = line.StartsWith("FUNC m ", StringComparison.Ordinal) ? line[7..] : line[5..];
                string[] fields = value.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 4 || !Hex(fields[0], out _) || !Hex(fields[1], out ulong size) || !Hex(fields[2], out _) || string.IsNullOrWhiteSpace(fields[3]))
                    return false;
                function |= size > 0;
            }
            if (line.StartsWith("STACK CFI INIT ", StringComparison.Ordinal))
            {
                string[] fields = line[15..].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 3 || !Hex(fields[0], out _) || !Hex(fields[1], out ulong size) || !fields[2].Contains(".cfa:", StringComparison.Ordinal))
                    return false;
                unwind |= size > 0;
            }
        }
        return header && function && unwind;
    }

    private static bool Hex(string value, out ulong result) => ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);

    // A line cap avoids allocating an artifact-sized string for malformed native/cache content.
    internal static async IAsyncEnumerable<string> LinesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        char[] buffer = new char[8192];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    yield return line.ToString().TrimEnd('\r');
                    line.Clear();
                }
                else
                {
                    if (line.Length >= 65536)
                        throw new InvalidDataException("Symbol line exceeds limit.");
                    line.Append(buffer[i]);
                }
            }
        }
        if (line.Length > 0)
            yield return line.ToString();
    }
}
