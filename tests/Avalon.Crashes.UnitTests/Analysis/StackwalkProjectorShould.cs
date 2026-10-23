using System.Text;
using System.Text.Json;
using Avalon.Crashes.Analysis;
using Xunit;

namespace Avalon.Crashes.UnitTests.Analysis;

public sealed class StackwalkProjectorShould
{
    [Fact]
    public void Should_not_call_an_absent_dump_thread_id_a_metadata_disagreement()
    {
        object[] threads = [new { thread_id = (uint?)null, frames = new[] { AnalysisFixture.Frame() } }];
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([], threads: threads), AnalysisFixture.Input(), AnalysisFixture.Tools);
        Assert.Null(result.Crash.ThreadId);
        Assert.False(result.Quality.HasFlag(CrashQualityFlags.MetadataDisagreement));
    }

    [Fact]
    public void Should_not_copy_symbol_availability_between_same_basename_images()
    {
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module("same.dll", "A1"), AnalysisFixture.Module("same.dll", "B2", 0x3000));
        object[] modules = [new { filename = "same.dll", base_addr = "0x1000", code_id = "A1", loaded_symbols = true }];
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([AnalysisFixture.Frame(null)], modules: modules), input, AnalysisFixture.Tools);
        Assert.True(result.Modules.Single(m => m.ImageKey == "A1").SymbolsAvailable);
        Assert.False(result.Modules.Single(m => m.ImageKey == "B2").SymbolsAvailable);
    }

    [Theory]
    [InlineData("EXCEPTION_PRIV_INSTRUCTION", 0xc0000096u)]
    [InlineData("EXCEPTION_INT_OVERFLOW", 0xc0000095u)]
    [InlineData("EXCEPTION_IN_PAGE_ERROR_READ / 0xc000000e", 0xc0000006u)]
    [InlineData("STATUS_STACK_BUFFER_OVERRUN / FAST_FAIL_FATAL_APP_EXIT", 0xc0000409u)]
    [InlineData("UNHANDLED_CPP_EXCEPTION", 0xe06d7363u)]
    public void Should_preserve_known_dump_exception_codes(string reason, uint expected)
    {
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([AnalysisFixture.Frame()], reason: reason), AnalysisFixture.Input(), AnalysisFixture.Tools);
        Assert.Equal(expected, result.Crash.Code);
        Assert.Contains("code:0x" + expected.ToString("x8", System.Globalization.CultureInfo.InvariantCulture), CrashSignature.Create(result).Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_retain_windows_build_from_the_pinned_os_version_shape()
    {
        CrashAnalysis result = AnalysisFixture.Project([AnalysisFixture.Frame()]);
        Assert.Equal("10.0.26100", result.System.OsVersion);
        Assert.Equal("26100", result.System.OsBuild);
    }

    [Fact]
    public void Should_use_instruction_range_even_when_data_address_points_into_a_driver()
    {
        byte[] json = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/Crashes/stackwalk/driver-data-address.json"));
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module(), AnalysisFixture.Module("nvwgf2umx.dll", "D1", 0x3000));
        CrashAnalysis analysis = StackwalkProjector.Project(json, input, AnalysisFixture.Tools);
        Assert.Equal("0x0000000000003010", analysis.Crash.Address);
        Assert.Equal("runtime.exe", analysis.Threads[0].Frames[0].Module);
        Assert.DoesNotContain("nvwgf2umx.dll", CrashSignature.Create(analysis).Canonical, StringComparison.Ordinal);
        Assert.False(analysis.Quality.HasFlag(CrashQualityFlags.MetadataDisagreement));
    }

    [Fact]
    public void Should_accept_the_pinned_nullable_schema_and_discard_duplicate_register_view()
    {
        byte[] json = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/Crashes/stackwalk/nullable-unknown-trust.json"));
        CrashAnalysis analysis = StackwalkProjector.Project(json, AnalysisFixture.Input(), AnalysisFixture.Tools);
        Assert.Equal(0xe0000001u, analysis.Crash.Code);
        Assert.Null(analysis.Crash.ThreadId);
        Assert.Null(analysis.System.Os);
        Assert.Equal("future-unwinder", analysis.Threads[0].Frames[0].Trust);
        Assert.Equal("A1", analysis.Threads[0].Frames[0].ImageKey);
        Assert.Contains("code:0xe0000001", CrashSignature.Create(analysis).Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("registers", JsonSerializer.Serialize(analysis), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_prioritize_faulting_image_when_more_than_512_relevant_modules_exist()
    {
        ModuleIdentity[] modules = Enumerable.Range(0, 770).Select(i => AnalysisFixture.Module("same.dll", "IMAGE" + i, (ulong)(0x1000 + i * 0x1000))).ToArray();
        object[] frames = Enumerable.Range(0, 256).Select(i => AnalysisFixture.Frame(null, address: "0x" + (0x201010 + i * 0x1000).ToString("x", System.Globalization.CultureInfo.InvariantCulture), module: "same.dll")).ToArray();
        object[] otherFrames = Enumerable.Range(0, 256).Select(i => AnalysisFixture.Frame(null, address: "0x" + (0x1010 + i * 0x1000).ToString("x", System.Globalization.CultureInfo.InvariantCulture), module: "same.dll")).ToArray();
        object[] middleFrames = Enumerable.Range(0, 256).Select(i => AnalysisFixture.Frame(null, address: "0x" + (0x101010 + i * 0x1000).ToString("x", System.Globalization.CultureInfo.InvariantCulture), module: "same.dll")).ToArray();
        object[] threads = [new { thread_id = 7, frames }, new { thread_id = 8, frames = otherFrames }, new { thread_id = 9, frames = middleFrames }];
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([], threads: threads), AnalysisFixture.Input(modules), AnalysisFixture.Tools);
        Assert.Contains(result.Modules, m => m.ImageKey == "IMAGE512");
        Assert.Contains(result.Modules, m => m.ImageKey == "IMAGE767");
        Assert.Equal(512, result.Modules.Count);
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.Truncated));
    }

    [Fact]
    public void Should_not_attach_an_image_identity_from_basename_and_offset_without_a_pc()
    {
        byte[] json = AnalysisFixture.Json([new { module = "same.dll", module_offset = "0x10", trust = "context" }]);
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module("same.dll"), AnalysisFixture.Module("same.dll", "B2", 0x3000));
        CrashAnalysis result = StackwalkProjector.Project(json, input, AnalysisFixture.Tools);
        Assert.Null(result.Threads[0].Frames[0].ImageKey);
        Assert.Null(result.Threads[0].Frames[0].ModuleOffset);
        Assert.Equal(CrashFailureCode.UngroupableAnalysis, Assert.Throws<CrashInputException>(() => CrashSignature.Create(result)).Code);
    }

    [Fact]
    public void Should_preserve_case_and_flag_even_only_a_module_or_pdb_name_omission()
    {
        ModuleIdentity module = AnalysisFixture.Module("Unusual " + new string('é', 130) + ".DLL") with { PdbName = new string('P', 140) + ".pdb" };
        CrashAnalysis result = AnalysisFixture.Project([AnalysisFixture.Frame(null)], AnalysisFixture.Input(module));
        Assert.StartsWith("Unusual é", result.Modules[0].Name, StringComparison.Ordinal);
        Assert.Equal(128, result.Modules[0].Name.Length);
        Assert.Equal(128, result.Modules[0].PdbName!.Length);
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.Truncated));
    }

    [Fact]
    public void Should_mark_scan_and_missing_system_symbols_without_requiring_them()
    {
        CrashAnalysis result = AnalysisFixture.Project([AnalysisFixture.Frame(null, "cfi_scan", module: "KERNELBASE.DLL")], AnalysisFixture.Input(AnalysisFixture.Module("KERNELBASE.DLL")));
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.ScanFrames));
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.MissingSystemSymbols));
        Assert.NotEmpty(CrashSignature.Create(result).Hash);
    }

    [Fact]
    public void Should_fail_explicitly_when_even_required_identity_cannot_fit()
    {
        CrashInput input = AnalysisFixture.Input() with { LogTail = new string('x', 2 * 1024 * 1024) };
        Assert.Equal(CrashFailureCode.InvalidAnalysis, Assert.Throws<CrashInputException>(() => AnalysisFixture.Project([AnalysisFixture.Frame()], input)).Code);
    }

    [Fact]
    public void Should_refuse_stackwalk_stdout_over_the_native_cap()
    {
        byte[] json = new byte[16 * 1024 * 1024 + 1];
        Assert.Equal(CrashFailureCode.ResourceLimit, Assert.Throws<CrashInputException>(() => StackwalkProjector.Project(json, AnalysisFixture.Input(), AnalysisFixture.Tools)).Code);
    }

    [Fact]
    public void Should_keep_crashing_thread_first_when_trimming()
    {
        object[] threads = Enumerable.Range(0, 130).Select(i => (object)new { thread_id = i + 7, frames = new[] { AnalysisFixture.Frame("Thread" + i) } }).ToArray();
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([], 129, threads), AnalysisFixture.Input(), AnalysisFixture.Tools);
        Assert.Equal(128, result.Threads.Count);
        Assert.Equal(129, result.Threads[0].Index);
        Assert.Equal(0, result.Threads[1].Index);
        Assert.Equal(136u, result.Crash.ThreadId);
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.Truncated));
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.MetadataDisagreement));
    }

    [Fact]
    public void Should_cap_frames_inlines_functions_paths_and_module_names()
    {
        object[] inlines = Enumerable.Range(0, 18).Select(_ => (object)new { function = new string('x', 1050), file = "C:/private/" + new string('f', 520), line = 1 }).ToArray();
        string name = new('m', 140);
        object[] frames = Enumerable.Range(0, 260).Select(i => i == 0
            ? AnalysisFixture.Frame(new string('f', 1050), module: name, inlines: inlines, file: "C:/private/" + new string('f', 520))
            : AnalysisFixture.Frame(module: name)).ToArray();
        CrashAnalysis result = AnalysisFixture.Project(frames, AnalysisFixture.Input(AnalysisFixture.Module(name)));
        CrashFrame frame = result.Threads[0].Frames[0];
        Assert.Equal(256, result.Threads[0].Frames.Count);
        Assert.Equal(16, frame.InlineFrames.Count);
        Assert.Equal(1024, frame.Function!.Length);
        Assert.Equal(512, frame.Source!.Length);
        Assert.Equal(128, frame.Module!.Length);
        Assert.Equal(1024, frame.InlineFrames[0].Function!.Length);
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.Truncated));
    }

    [Fact]
    public void Should_preserve_relevant_modules_after_the_module_cap_and_distinct_same_basename_images()
    {
        ModuleIdentity[] modules = Enumerable.Range(0, 514).Select(i => AnalysisFixture.Module(i < 512 ? "filler" + i + ".dll" : "same.dll", "IMAGE" + i, (ulong)(0x1000 + i * 0x1000))).ToArray();
        CrashInput input = AnalysisFixture.Input(modules);
        CrashAnalysis first = AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x201010", module: "same.dll")], input);
        CrashAnalysis second = AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x202010", module: "same.dll")], input);
        Assert.Equal(512, first.Modules.Count);
        Assert.Contains(first.Modules, m => m.ImageKey == "IMAGE512");
        Assert.True(first.Quality.HasFlag(CrashQualityFlags.Truncated));
        Assert.NotEqual(CrashSignature.Create(first).Hash, CrashSignature.Create(second).Hash);
    }

    [Fact]
    public void Should_strip_absolute_paths_and_discard_sensitive_unknown_fields()
    {
        CrashAnalysis result = AnalysisFixture.Project([AnalysisFixture.Frame(file: "C:/private/client/engine/src/application/Application.cpp", inlines: [new { function = "Inlined()", file = "/home/user/main.cpp", line = 4 }])]);
        Assert.Equal("engine/src/application/Application.cpp", result.Threads[0].Frames[0].Source);
        Assert.Equal("main.cpp", result.Threads[0].Frames[0].InlineFrames[0].Source);
        string json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_accept_nullable_fields_and_preserve_unknown_trust_without_promoting_it()
    {
        CrashAnalysis result = AnalysisFixture.Project([AnalysisFixture.Frame(null, "future-trust", file: null)]);
        Assert.Equal("future-trust", result.Threads[0].Frames[0].Trust);
        Assert.Null(result.Threads[0].Frames[0].Function);
        Assert.Null(result.Threads[0].Frames[0].Source);
        Assert.Empty(result.Threads[0].Frames[0].InlineFrames);
        Assert.Contains("fallback", CrashSignature.Create(result).Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_missing_crash_thread_instead_of_inventing_one_from_metadata()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"crash_info\":{\"type\":\"EXCEPTION_ACCESS_VIOLATION_READ\",\"crashing_thread\":null},\"threads\":[{\"thread_id\":7,\"frames\":[]}]}");
        Assert.Equal(CrashFailureCode.InvalidAnalysis, Assert.Throws<CrashInputException>(() => StackwalkProjector.Project(json, AnalysisFixture.Input(), AnalysisFixture.Tools)).Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"crash_info\":{},\"threads\":\"wrong\"}")]
    public void Should_refuse_invalid_analysis_with_a_finite_code(string json) =>
        Assert.Equal(CrashFailureCode.InvalidAnalysis, Assert.Throws<CrashInputException>(() => StackwalkProjector.Project(Encoding.UTF8.GetBytes(json), AnalysisFixture.Input(), AnalysisFixture.Tools)).Code);

    [Fact]
    public void Should_trim_to_two_mebibytes_preserving_fault_identity()
    {
        object[] frames = Enumerable.Range(0, 256).Select(_ => AnalysisFixture.Frame(new string('x', 1024), inlines: Enumerable.Range(0, 16).Select(_ => (object)new { function = new string('i', 1024), file = "main.cpp", line = 1 }).ToArray())).ToArray();
        object[] threads = Enumerable.Range(0, 2).Select(i => (object)new { thread_id = i + 7, frames }).ToArray();
        CrashAnalysis result = StackwalkProjector.Project(AnalysisFixture.Json([], 1, threads), AnalysisFixture.Input(), AnalysisFixture.Tools);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(result).Length <= 2 * 1024 * 1024);
        Assert.Equal(1, result.Threads[0].Index);
        Assert.NotEmpty(result.Threads[0].Frames);
        Assert.True(result.Quality.HasFlag(CrashQualityFlags.Truncated));
    }
}
