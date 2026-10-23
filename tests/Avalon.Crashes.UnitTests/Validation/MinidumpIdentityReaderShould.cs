using Avalon.Crashes.Validation;
using Xunit;

namespace Avalon.Crashes.UnitTests.Validation;

public sealed class MinidumpIdentityReaderShould
{
    [Fact]
    public void Should_read_sanitized_runtime_and_private_pdb_identity()
    {
        DumpIdentity identity = new MinidumpIdentityReader().Read(CrashFixture.Dump());
        Assert.Equal("x86_64", identity.Architecture);
        Assert.Equal(CrashFixture.PdbKey, identity.Runtime.PdbKey);
        Assert.Equal("123456781a000", identity.Runtime.ImageKey);
        Assert.Equal("runtime.pdb", identity.Runtime.PdbName);
    }

    [Fact]
    public void Should_keep_unique_nonoverlapping_module_identities()
    {
        DumpIdentity identity = new MinidumpIdentityReader().Read(CrashFixture.TwoModuleDump());
        Assert.Equal(new[] { "runtime.exe", "driver.dll" }, identity.Modules.Select(m => m.Name));
    }

    [Fact]
    public void Should_accept_repeated_empty_unused_stream_slots()
    {
        byte[] dump = CrashFixture.Dump(1024);
        dump.AsSpan(32, 24).CopyTo(dump.AsSpan(800));
        CrashFixture.Put32(dump, 8, 4);
        CrashFixture.Put32(dump, 12, 800);
        DumpIdentity identity = new MinidumpIdentityReader().Read(dump);
        Assert.Equal(CrashFixture.PdbKey, identity.Runtime.PdbKey);
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    [InlineData("overflow")]
    [InlineData("duplicateStream")]
    public void Should_reject_ambiguous_module_ranges_and_directories(string kind)
    {
        byte[] dump = CrashFixture.TwoModuleDump();
        if (kind == "duplicate")
            CrashFixture.Put32(dump, 244, 512);
        if (kind == "overlap")
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(dump.AsSpan(224), 0x12340001);
        if (kind == "overflow")
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(dump.AsSpan(224), ulong.MaxValue);
        if (kind == "duplicateStream")
            CrashFixture.Put32(dump, 44, 7);
        CrashInputException error = Assert.Throws<CrashInputException>(() => new MinidumpIdentityReader().Read(dump));
        Assert.Equal(CrashFailureCode.InvalidDump, error.Code);
    }
    [Theory]
    [InlineData("signature", CrashFailureCode.InvalidDump)]
    [InlineData("directory", CrashFailureCode.InvalidDump)]
    [InlineData("stream", CrashFailureCode.InvalidDump)]
    [InlineData("count", CrashFailureCode.InvalidDump)]
    [InlineData("name", CrashFailureCode.InvalidDump)]
    [InlineData("cv", CrashFailureCode.InvalidDump)]
    [InlineData("range", CrashFailureCode.InvalidDump)]
    [InlineData("runtime", CrashFailureCode.RuntimeIdentityMismatch)]
    [InlineData("pdb", CrashFailureCode.RuntimeIdentityMismatch)]
    [InlineData("pdbPath", CrashFailureCode.RuntimeIdentityMismatch)]
    [InlineData("architecture", CrashFailureCode.UnsupportedArchitecture)]
    [InlineData("platform", CrashFailureCode.UnsupportedArchitecture)]
    public void Should_reject_mismatched_or_out_of_bounds_runtime_identity(string kind, CrashFailureCode expected)
    {
        byte[] dump = CrashFixture.Dump();
        switch (kind)
        {
            case "signature": dump[0] = 0; break;
            case "directory": CrashFixture.Put32(dump, 12, uint.MaxValue); break;
            case "stream": CrashFixture.Put32(dump, 40, uint.MaxValue); break;
            case "count": CrashFixture.Put32(dump, 112, uint.MaxValue); break;
            case "name": CrashFixture.Put32(dump, 224, uint.MaxValue); break;
            case "cv": CrashFixture.Put32(dump, 196, uint.MaxValue); break;
            case "range": CrashFixture.Put32(dump, 124, 0); break;
            case "runtime": dump[250] = (byte)'x'; break;
            case "pdbPath":
                byte[] path = System.Text.Encoding.UTF8.GetBytes(@"C:\private\runtime.pdb" + "\0");
                CrashFixture.Put32(dump, 192, (uint)(24 + path.Length));
                path.CopyTo(dump, 344);
                break;
            case "pdb": dump[344] = (byte)'x'; break;
            case "architecture": dump[56] = 0; break;
            case "platform": dump[76] = 0; break;
        }
        CrashInputException error = Assert.Throws<CrashInputException>(() => new MinidumpIdentityReader().Read(dump));
        Assert.Equal(expected, error.Code);
    }
}
