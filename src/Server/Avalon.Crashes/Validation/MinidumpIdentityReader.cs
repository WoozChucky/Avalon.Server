using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Avalon.Crashes.Validation;

public sealed class MinidumpIdentityReader
{
    private static readonly UnicodeEncoding s_utf16 = new(false, false, true);
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    public DumpIdentity Read(ReadOnlySpan<byte> dump)
    {
        if (dump.Length < 32 || U32(dump, 0) != 0x504d444d || (U32(dump, 4) & 0xffff) != 0xa793)
            throw Invalid();

        uint count = U32(dump, 8);
        if (count is 0 or > 256)
            throw Invalid();
        ReadOnlySpan<byte> directory = Slice(dump, U32(dump, 12), count * 12);
        ReadOnlySpan<byte> system = default;
        ReadOnlySpan<byte> modules = default;
        var streamTypes = new HashSet<uint>();
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = directory.Slice(i * 12, 12);
            uint type = U32(entry, 0);
            // MiniDumpWriteDump reserves repeated empty UnusedStream slots in its directory.
            if (type == 0)
            {
                if (U32(entry, 4) != 0 || U32(entry, 8) != 0)
                    throw Invalid();
                continue;
            }
            if (!streamTypes.Add(type))
                throw Invalid();
            ReadOnlySpan<byte> stream = Slice(dump, U32(entry, 8), U32(entry, 4));
            if (type == 7)
                system = stream;
            if (type == 4)
                modules = stream;
        }
        if (system.Length < 56 || modules.Length < 4)
            throw Invalid();
        if (BinaryPrimitives.ReadUInt16LittleEndian(system) != 9 || U32(system, 20) != 2)
            throw new CrashInputException(CrashFailureCode.UnsupportedArchitecture);

        uint moduleCount = U32(modules, 0);
        if (moduleCount is 0 or > CrashLimits.Modules || (ulong)moduleCount * 108 + 4 != (ulong)modules.Length)
            throw Invalid();
        var identities = new List<ModuleIdentity>((int)moduleCount);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RuntimeIdentity? runtime = null;
        for (int i = 0; i < moduleCount; i++)
        {
            ReadOnlySpan<byte> module = modules.Slice(4 + i * 108, 108);
            ulong address = BinaryPrimitives.ReadUInt64LittleEndian(module);
            uint size = U32(module, 8);
            if (size == 0 || address > ulong.MaxValue - size)
                throw Invalid();
            string name = ReadName(dump, U32(module, 20));
            if (!names.Add(name))
                throw Invalid();
            string imageKey = U32(module, 16).ToString("X8", CultureInfo.InvariantCulture) + size.ToString("x", CultureInfo.InvariantCulture);
            uint cvLength = U32(module, 76);
            ReadOnlySpan<byte> cv = Slice(dump, U32(module, 80), cvLength);
            string? pdbName = null;
            string? codeViewName = null;
            string? pdbKey = null;
            if (cv.Length >= 4 && cv[..4].SequenceEqual("RSDS"u8))
            {
                if (cv.Length < 25 || cv.Length > 32768)
                    throw Invalid();
                int terminator = cv[24..].IndexOf((byte)0);
                if (terminator <= 0 || terminator != cv.Length - 25)
                    throw Invalid();
                try
                {
                    codeViewName = s_utf8.GetString(cv.Slice(24, terminator));
                    pdbName = Basename(codeViewName);
                }
                catch (DecoderFallbackException)
                {
                    throw Invalid();
                }
                Guid guid = new(cv.Slice(4, 16));
                uint age = U32(cv, 20);
                if (guid == Guid.Empty || age == 0)
                    throw Invalid();
                pdbKey = guid.ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant() + age.ToString("X", CultureInfo.InvariantCulture);
            }
            // Validate the optional miscellaneous descriptor even though it is not retained.
            _ = Slice(dump, U32(module, 88), U32(module, 84));
            string? version = null;
            if (U32(module, 24) == 0xfeef04bd)
            {
                uint ms = U32(module, 32);
                uint ls = U32(module, 36);
                version = string.Create(CultureInfo.InvariantCulture, $"{ms >> 16}.{ms & 0xffff}.{ls >> 16}.{ls & 0xffff}");
            }
            identities.Add(new ModuleIdentity(name, imageKey, pdbName, pdbKey, version, address, size));
            if (string.Equals(name, "runtime.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(codeViewName, "runtime.pdb", StringComparison.Ordinal) || pdbKey is null)
                    throw new CrashInputException(CrashFailureCode.RuntimeIdentityMismatch);
                runtime = new RuntimeIdentity("runtime.exe", imageKey, "runtime.pdb", pdbKey);
            }
        }
        ModuleIdentity[] ordered = identities.OrderBy(m => m.BaseAddress).ToArray();
        for (int i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].BaseAddress < ordered[i - 1].BaseAddress + ordered[i - 1].Size)
                throw Invalid();
        }
        if (runtime is null)
            throw new CrashInputException(CrashFailureCode.RuntimeIdentityMismatch);
        return new DumpIdentity(runtime, "x86_64", identities.AsReadOnly());
    }

    private static string ReadName(ReadOnlySpan<byte> dump, uint rva)
    {
        uint length = U32(Slice(dump, rva, 4), 0);
        if (length == 0 || length > 32768 || length % 2 != 0)
            throw Invalid();
        ReadOnlySpan<byte> bytes = Slice(dump, (ulong)rva + 4, length);
        try
        {
            return Basename(s_utf16.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static string Basename(string path)
    {
        string name = path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
        if (name.Length is 0 or > CrashLimits.ModuleCharacters || name is "." or ".." || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw Invalid();
        return name.ToLowerInvariant();
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> dump, ulong offset, ulong length)
    {
        if (offset > (ulong)dump.Length || length > (ulong)dump.Length - offset)
            throw Invalid();
        return dump.Slice((int)offset, (int)length);
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static CrashInputException Invalid() => new(CrashFailureCode.InvalidDump);
}
