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
    public void Should_accept_same_basename_modules_with_distinct_identities_and_ranges()
    {
        byte[] dump = CrashFixture.DumpWithModules([
            new(@"C:\runtime.exe", "runtime.pdb", 0x10000000),
            new(@"C:\first\helper.dll", "helper.pdb", 0x20000000, 0x12345678),
            new(@"C:\second\helper.dll", "helper.pdb", 0x30000000, 0x12345679)
        ]);
        DumpIdentity identity = new MinidumpIdentityReader().Read(dump);
        ModuleIdentity[] helpers = identity.Modules.Where(m => m.Name == "helper.dll").ToArray();
        Assert.Equal(2, helpers.Length);
        Assert.NotEqual(helpers[0].ImageKey, helpers[1].ImageKey);
        Assert.NotEqual(helpers[0].PdbKey, helpers[1].PdbKey);
        Assert.NotEqual(helpers[0].BaseAddress, helpers[1].BaseAddress);
        Assert.All(helpers, helper => Assert.True(helper.SymbolLookupEligible));
    }

    [Theory]
    [InlineData("helper library")]
    [InlineData("辅助程序Ü")]
    [InlineData("helper~1")]
    [InlineData("long")]
    public void Should_preserve_legitimate_module_and_pdb_basenames(string name)
    {
        string stem = name == "long" ? new string('a', 200) : name;
        byte[] dump = CrashFixture.DumpWithModules([
            new(@"C:\runtime.exe", "runtime.pdb", 0x10000000),
            new(@"C:\private\" + stem + ".dll", @"C:\build\" + stem + ".pdb", 0x20000000)
        ]);
        DumpIdentity identity = new MinidumpIdentityReader().Read(dump);
        Assert.Equal(stem + ".dll", identity.Modules[1].Name);
        Assert.Equal(stem + ".pdb", identity.Modules[1].PdbName);
        Assert.False(identity.Modules[1].SymbolLookupEligible);
        Assert.True(identity.Modules[0].SymbolLookupEligible);
    }

    [Fact]
    public void Should_validate_all_input_modules_including_runtime_beyond_projection_cap()
    {
        CrashFixture.TestModule[] modules = Enumerable.Range(0, 513)
            .Select(i => new CrashFixture.TestModule(@"C:\system\helper.dll", "helper.pdb", 0x10000000ul + (ulong)i * 0x20000, (uint)i))
            .Append(new CrashFixture.TestModule(@"C:\runtime.exe", "runtime.pdb", 0x90000000))
            .ToArray();
        byte[] dump = CrashFixture.DumpWithModules(modules);
        DumpIdentity identity = new MinidumpIdentityReader().Read(dump);
        Assert.Equal(514, identity.Modules.Count);
        Assert.Equal("runtime.exe", identity.Modules[513].Name);
        Assert.Equal(identity.Modules[513].PdbKey, identity.Runtime.PdbKey);
    }

    [Fact]
    public void Should_decode_a_shared_long_path_once_with_bounded_allocation()
    {
        CrashFixture.TestModule[] modules = Enumerable.Range(0, 600)
            .Select(i => new CrashFixture.TestModule(
                i == 0 ? "runtime.exe" : i == 1 ? @"C:\" + new string('x', 32000) + @"\Helper Library.dll" : "helper.dll",
                i == 0 ? "runtime.pdb" : "Helper Library.pdb",
                0x10000000ul + (ulong)i * 0x20000))
            .ToArray();
        byte[] dump = CrashFixture.DumpWithModules(modules);
        uint nameRva = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(244));
        for (int i = 2; i < modules.Length; i++)
            CrashFixture.Put32(dump, 116 + i * 108 + 20, nameRva);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        DumpIdentity identity = new MinidumpIdentityReader().Read(dump);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(600, identity.Modules.Count);
        Assert.Equal("Helper Library.dll", identity.Modules[^1].Name);
        Assert.InRange(allocated, 0, 16 * 1024 * 1024);
    }

    [Fact]
    public void Should_validate_ranges_beyond_the_projection_cap()
    {
        CrashFixture.TestModule[] modules = Enumerable.Range(0, 514)
            .Select(i => new CrashFixture.TestModule(i == 0 ? "runtime.exe" : "helper.dll", i == 0 ? "runtime.pdb" : "helper.pdb", 0x10000000ul + (ulong)i * 0x20000))
            .ToArray();
        byte[] dump = CrashFixture.DumpWithModules(modules);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(dump.AsSpan(116 + 513 * 108), modules[512].BaseAddress + 1);
        CrashInputException error = Assert.Throws<CrashInputException>(() => new MinidumpIdentityReader().Read(dump));
        Assert.Equal(CrashFailureCode.InvalidDump, error.Code);
    }

    [Theory]
    [InlineData("runtime.exe", "runtime.pdb", CrashFailureCode.RuntimeIdentityMismatch)]
    [InlineData("RUNTIME.EXE", "runtime.pdb", CrashFailureCode.RuntimeIdentityMismatch)]
    [InlineData("C:\\private\\..\\helper.dll", "helper.pdb", CrashFailureCode.InvalidDump)]
    [InlineData("C:\\private\0\\helper.dll", "helper.pdb", CrashFailureCode.InvalidDump)]
    [InlineData("..", "helper.pdb", CrashFailureCode.InvalidDump)]
    [InlineData("helper.dll:stream", "helper.pdb", CrashFailureCode.InvalidDump)]
    [InlineData("helper\0.dll", "helper.pdb", CrashFailureCode.InvalidDump)]
    [InlineData("helper.dll", "helper\0.pdb", CrashFailureCode.InvalidDump)]
    public void Should_reject_ambiguous_runtime_and_unsafe_basenames(string moduleName, string pdbName, CrashFailureCode expected)
    {
        byte[] dump = CrashFixture.DumpWithModules([
            new(@"C:\runtime.exe", "runtime.pdb", 0x10000000),
            new(moduleName, pdbName, 0x20000000)
        ]);
        CrashInputException error = Assert.Throws<CrashInputException>(() => new MinidumpIdentityReader().Read(dump));
        Assert.Equal(expected, error.Code);
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
        Assert.Equal(kind == "duplicate" ? CrashFailureCode.RuntimeIdentityMismatch : CrashFailureCode.InvalidDump, error.Code);
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
