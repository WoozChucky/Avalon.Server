using System.Text.Json;

namespace Avalon.Crashes.UnitTests.Analysis;

internal static class AnalysisFixture
{
    internal static ProcessorIdentity Tools => new("0.27.0", "2.3.9", "v1");

    internal static ModuleIdentity Module(string name = "runtime.exe", string image = "A1", ulong address = 0x1000, ulong size = 0x1000) =>
        new(name, image, "runtime.pdb", "ABC1", "1.0", address, size);

    internal static CrashInput Input(params ModuleIdentity[] modules) =>
        new("private-dump", new(1, "runtime", "0.4.1", "abc1234", 0xc0000005, "0x0000000000001010", 7, DateTimeOffset.UnixEpoch), null,
            new("runtime.exe", "A1", "runtime.pdb", "ABC1"), CrashQualityFlags.None)
        { Modules = modules.Length == 0 ? [Module()] : modules };

    internal static object Frame(string? function = "Game::Run()", string trust = "context", string address = "0x1010", string? module = "runtime.exe", object[]? inlines = null, string? file = null) =>
        new { function, trust, offset = address, module, module_offset = "0x10", file, line = 12, inlines };

    internal static byte[] Json(object[] frames, int crash = 0, object[]? threads = null, object[]? modules = null, string? reason = "EXCEPTION_ACCESS_VIOLATION_READ") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            crash_info = new { type = reason, address = "0x0", crashing_thread = crash },
            system_info = new { os = "Windows NT", os_ver = "10.0.26100", cpu_arch = "amd64", cpu_info = "test CPU" },
            threads = threads ?? [new { thread_id = 7, frames = frames }],
            modules = modules ?? [new { filename = "runtime.exe", base_addr = "0x1000", end_addr = "0x2000", code_id = "A1", loaded_symbols = true }],
            registers = "SECRET",
            environment = "SECRET"
        });

    internal static CrashAnalysis Project(object[] frames, CrashInput? input = null) =>
        Avalon.Crashes.Analysis.StackwalkProjector.Project(Json(frames), input ?? Input(), Tools);
}
