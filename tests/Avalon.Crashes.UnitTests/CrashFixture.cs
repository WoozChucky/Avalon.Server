using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Avalon.Crashes.UnitTests;

internal static class CrashFixture
{
    internal const string Version = "0.4.1-dev.10";
    internal const string PdbKey = "00112233445566778899AABBCCDDEEFF1";
    internal const string Metadata = """{"schema":1,"exe":"runtime","version":"0.4.1-dev.10","gitHash":"0123456789abcdef","exceptionCode":3221225477,"exceptionAddress":"000000001234ABCD","threadId":42,"time":"2026-10-23T12:13:14Z"}""";

    internal static byte[] Dump(int length = 512)
    {
        byte[] bytes = new byte[length];
        Put32(bytes, 0, 0x504d444d);
        Put32(bytes, 4, 0xa793);
        Put32(bytes, 8, 2);
        Put32(bytes, 12, 32);
        Put32(bytes, 32, 7);
        Put32(bytes, 36, 56);
        Put32(bytes, 40, 56);
        bytes[56] = 9;
        Put32(bytes, 76, 2);
        Put32(bytes, 44, 4);
        Put32(bytes, 48, 112);
        Put32(bytes, 52, 112);
        Put32(bytes, 112, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(116), 0x12340000);
        Put32(bytes, 124, 0x1a000);
        Put32(bytes, 132, 0x12345678);
        Put32(bytes, 136, 224);
        Put32(bytes, 192, 36);
        Put32(bytes, 196, 320);
        byte[] name = Encoding.Unicode.GetBytes(@"C:\private\runtime.exe");
        Put32(bytes, 224, (uint)name.Length);
        name.CopyTo(bytes, 228);
        "RSDS"u8.CopyTo(bytes.AsSpan(320));
        new Guid("00112233-4455-6677-8899-aabbccddeeff").ToByteArray().CopyTo(bytes, 324);
        Put32(bytes, 340, 1);
        "runtime.pdb\0"u8.CopyTo(bytes.AsSpan(344));
        return bytes;
    }

    internal static byte[] TwoModuleDump()
    {
        byte[] original = Dump();
        byte[] bytes = new byte[1024];
        original.CopyTo(bytes, 0);
        Put32(bytes, 48, 220);
        Put32(bytes, 112, 2);
        original.AsSpan(116, 108).CopyTo(bytes.AsSpan(224));
        Put32(bytes, 136, 512);
        Put32(bytes, 196, 640);
        original.AsSpan(224, 48).CopyTo(bytes.AsSpan(512));
        original.AsSpan(320, 36).CopyTo(bytes.AsSpan(640));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(224), 0x20000000);
        Put32(bytes, 244, 576);
        Put32(bytes, 304, 700);
        byte[] name = Encoding.Unicode.GetBytes(@"C:\system\driver.dll");
        Put32(bytes, 576, (uint)name.Length);
        name.CopyTo(bytes, 580);
        original.AsSpan(320, 36).CopyTo(bytes.AsSpan(700));
        bytes[704] ^= 1;
        "driver.pdb\0"u8.CopyTo(bytes.AsSpan(724));
        Put32(bytes, 300, 35);
        return bytes;
    }
    internal sealed record TestModule(string Name, string PdbName, ulong BaseAddress, uint Timestamp = 0x12345678);

    internal static byte[] DumpWithModules(IReadOnlyList<TestModule> modules)
    {
        byte[] template = Dump();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(template, 0, 112);
        writer.Write(modules.Count);
        writer.Write(new byte[108 * modules.Count]);
        uint[] names = new uint[modules.Count];
        uint[] codeViews = new uint[modules.Count];
        uint[] codeViewLengths = new uint[modules.Count];
        for (int i = 0; i < modules.Count; i++)
        {
            names[i] = (uint)stream.Position;
            byte[] name = Encoding.Unicode.GetBytes(modules[i].Name);
            writer.Write(name.Length);
            writer.Write(name);
            writer.Write((ushort)0);
            codeViews[i] = (uint)stream.Position;
            writer.Write("RSDS"u8);
            byte[] guid = new Guid("00112233-4455-6677-8899-aabbccddeeff").ToByteArray();
            Put32(guid, 0, (uint)i + 1);
            writer.Write(guid);
            writer.Write(1u);
            writer.Write(Encoding.UTF8.GetBytes(modules[i].PdbName));
            writer.Write((byte)0);
            codeViewLengths[i] = (uint)stream.Position - codeViews[i];
        }
        byte[] bytes = stream.ToArray();
        Put32(bytes, 48, (uint)(4 + 108 * modules.Count));
        for (int i = 0; i < modules.Count; i++)
        {
            int offset = 116 + 108 * i;
            template.AsSpan(116, 108).CopyTo(bytes.AsSpan(offset));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), modules[i].BaseAddress);
            Put32(bytes, offset + 8, 0x10000);
            Put32(bytes, offset + 16, modules[i].Timestamp);
            Put32(bytes, offset + 20, names[i]);
            Put32(bytes, offset + 76, codeViewLengths[i]);
            Put32(bytes, offset + 80, codeViews[i]);
        }
        return bytes;
    }

    internal static byte[] Archive(byte[]? dump = null, string? metadata = null, byte[]? log = null, string? extraName = null, CompressionLevel compression = CompressionLevel.NoCompression)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "dump.dmp", dump ?? Dump(), compression);
            Add(archive, "meta.json", Encoding.UTF8.GetBytes(metadata ?? Metadata), compression);
            if (log is not null)
                Add(archive, "log-tail.txt", log, compression);
            if (extraName is not null)
                Add(archive, extraName, [1], compression);
        }
        return stream.ToArray();
    }

    internal static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void Add(ZipArchive archive, string name, byte[] bytes, CompressionLevel compression)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, compression);
        using Stream output = entry.Open();
        output.Write(bytes);
    }
}
