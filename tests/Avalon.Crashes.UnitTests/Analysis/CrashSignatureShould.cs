using Avalon.Crashes.Analysis;
using Xunit;

namespace Avalon.Crashes.UnitTests.Analysis;

public sealed class CrashSignatureShould
{
    [Theory]
    [InlineData("abort()")]
    [InlineData("abort(void)")]
    [InlineData("terminate()")]
    [InlineData("terminate(void)")]
    [InlineData("std::terminate()")]
    [InlineData("std::terminate(void)")]
    [InlineData("__std_terminate()")]
    [InlineData("__std_terminate(void)")]
    [InlineData("_purecall()")]
    [InlineData("_purecall(void)")]
    [InlineData("__scrt_unhandled_exception_filter(_EXCEPTION_POINTERS* const)")]
    public void Should_skip_exact_crt_termination_plumbing_linked_into_runtime(string function)
    {
        object[] frames = Enumerable.Repeat(AnalysisFixture.Frame(function), 6).Append(AnalysisFixture.Frame("Game::RealFault()", "cfi")).ToArray();
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project(frames));
        Assert.DoesNotContain(function, signature.Canonical, StringComparison.Ordinal);
        Assert.Contains("runtime.exe!Game::RealFault()", signature.Canonical, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Game::abort()")]
    [InlineData("Game::terminate()")]
    [InlineData("Game::std::terminate()")]
    [InlineData("abortRecovery()")]
    [InlineData("terminateWorld()")]
    [InlineData("abort(int)")]
    public void Should_keep_application_functions_that_resemble_crt_noise(string function) =>
        Assert.Contains(function, CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(function)])).Canonical, StringComparison.Ordinal);

    [Fact]
    public void Should_keep_a_third_party_abort_function_outside_runtime_and_system_modules()
    {
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module("application-plugin.dll"));
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("abort()", module: "application-plugin.dll")], input));
        Assert.Contains("application-plugin.dll!abort()", signature.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_not_infer_a_faulting_driver_from_a_basename_without_instruction_identity()
    {
        byte[] json = AnalysisFixture.Json([new { module = "nvwgf2umx.dll", module_offset = "0x10", trust = "context" }, AnalysisFixture.Frame("Game::Run()", "cfi")]);
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module(), AnalysisFixture.Module("nvwgf2umx.dll", "D1", 0x3000));
        CrashAnalysis analysis = StackwalkProjector.Project(json, input, AnalysisFixture.Tools);
        Assert.DoesNotContain("nvwgf2umx.dll", CrashSignature.Create(analysis).Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_exclude_driver_versions_from_driver_plus_runtime_groups()
    {
        CrashInput first = AnalysisFixture.Input(AnalysisFixture.Module(), AnalysisFixture.Module("nvwgf2umx.dll", "D1", 0x3000));
        CrashInput changed = AnalysisFixture.Input(AnalysisFixture.Module(), AnalysisFixture.Module("nvwgf2umx.dll", "D2", 0x3000) with { Version = "99.0" });
        object[] frames = [AnalysisFixture.Frame(null, module: "nvwgf2umx.dll", address: "0x3010"), AnalysisFixture.Frame("Game::Render()", "cfi")];
        Assert.Equal(CrashSignature.Create(AnalysisFixture.Project(frames, first)).Hash, CrashSignature.Create(AnalysisFixture.Project(frames, changed)).Hash);
    }

    [Fact]
    public void Should_keep_exception_categories_distinct_and_ignore_metadata_logs_and_thread_ids()
    {
        CrashAnalysis access = AnalysisFixture.Project([AnalysisFixture.Frame()]);
        CrashAnalysis terminate = access with { Crash = access.Crash with { Reason = "EXCEPTION_UNKNOWN", Code = 0xe0000001 } };
        CrashAnalysis other = access with { Metadata = access.Metadata with { Version = "99.0.0", ThreadId = 99 }, LogTail = "private log", Crash = access.Crash with { ThreadId = 99 } };
        Assert.NotEqual(CrashSignature.Create(access).Hash, CrashSignature.Create(terminate).Hash);
        Assert.Equal(CrashSignature.Create(access).Hash, CrashSignature.Create(other).Hash);
    }

    [Fact]
    public void Should_not_merge_driver_fault_offsets_without_a_runtime_frame()
    {
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module("nvwgf2umx.dll", "D1"));
        SignatureIdentity first = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(null, module: "nvwgf2umx.dll")], input));
        SignatureIdentity other = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x1020", module: "nvwgf2umx.dll")], input));
        Assert.NotEqual(first.Hash, other.Hash);
    }

    [Fact]
    public void Should_keep_overloads_and_unambiguous_symbol_boundaries()
    {
        SignatureIdentity integer = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("Game::Run(int)")]));
        SignatureIdentity floating = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("Game::Run(float)")]));
        SignatureIdentity separator = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("A | runtime.exe!B")]));
        SignatureIdentity twoFrames = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("A"), AnalysisFixture.Frame("B", "cfi")]));
        Assert.NotEqual(integer.Hash, floating.Hash);
        Assert.NotEqual(separator.Hash, twoFrames.Hash);
    }

    [Fact]
    public void Should_group_equal_frames_across_aslr()
    {
        CrashAnalysis first = AnalysisFixture.Project([AnalysisFixture.Frame(null)]);
        CrashAnalysis moved = AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x9010")], AnalysisFixture.Input(AnalysisFixture.Module(address: 0x9000)));
        Assert.NotEmpty(CrashSignature.Create(first).Hash);
        Assert.Equal(CrashSignature.Create(first).Hash, CrashSignature.Create(moved).Hash);
    }

    [Fact]
    public void Should_distinguish_images_for_unresolved_offsets()
    {
        CrashAnalysis first = AnalysisFixture.Project([AnalysisFixture.Frame(null)]);
        CrashAnalysis other = AnalysisFixture.Project([AnalysisFixture.Frame(null)], AnalysisFixture.Input(AnalysisFixture.Module(image: "B2")));
        Assert.NotEqual(CrashSignature.Create(first).Hash, CrashSignature.Create(other).Hash);
    }

    [Theory]
    [InlineData("Game::Box<std::vector<int>>::operator<<(Thing<float> const&)", "Game::Box::operator<<(Thing const&)")]
    [InlineData("Game::Box<int>::operator<(Thing const&)", "Game::Box::operator<(Thing const&)")]
    [InlineData("Game::operator<=>(Game const&)", "Game::operator<=>(Game const&)")]
    [InlineData("Game::operator()<int>(Thing<int>)", "Game::operator()(Thing)")]
    [InlineData("  Game::Run( int,   float ) ", "Game::Run( int, float )")]
    [InlineData("Game::Broken<int", "Game::Broken<int")]
    public void Should_keep_operators_while_removing_templates(string original, string expected) =>
        Assert.Contains("runtime.exe!" + expected, CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(original)])).Canonical, StringComparison.Ordinal);

    [Fact]
    public void Should_only_use_the_faulting_gpu_driver()
    {
        CrashInput input = AnalysisFixture.Input(AnalysisFixture.Module(), AnalysisFixture.Module("nvwgf2umx.dll", "D1", 0x3000));
        SignatureIdentity runtime = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame()], input));
        SignatureIdentity driver = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x3010", module: "nvwgf2umx.dll"), AnalysisFixture.Frame("Game::Run()", "cfi")], input));
        Assert.DoesNotContain("nvwgf2umx.dll", runtime.Canonical, StringComparison.Ordinal);
        Assert.Contains("nvwgf2umx.dll", driver.Canonical, StringComparison.Ordinal);
        Assert.Contains("runtime.exe!Game::Run()", driver.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_prefer_trusted_actionable_frames_and_separate_scan_fallback()
    {
        SignatureIdentity trusted = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("Misleading()", "scan"), AnalysisFixture.Frame("Real()", "cfi")]));
        SignatureIdentity scanned = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("Real()", "scan")]));
        Assert.DoesNotContain("Misleading", trusted.Canonical, StringComparison.Ordinal);
        Assert.NotEqual(trusted.Hash, scanned.Hash);
        Assert.Contains("scan", scanned.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_skip_exact_noise_without_dropping_similar_game_functions()
    {
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project([
            AnalysisFixture.Frame("Avalon::Engine::CrashHandler::(anonymous namespace)::Synthetic(unsigned long, unsigned long, bool)"),
            AnalysisFixture.Frame("Game::CrashAbortRecovery()", "cfi")]));
        Assert.DoesNotContain("Synthetic", signature.Canonical, StringComparison.Ordinal);
        Assert.Contains("Game::CrashAbortRecovery()", signature.Canonical, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Capture(_EXCEPTION_POINTERS*, unsigned long)")]
    [InlineData("Unhandled(_EXCEPTION_POINTERS*)")]
    [InlineData("Synthetic(unsigned long, unsigned long, bool)")]
    [InlineData("Terminated()")]
    [InlineData("PureCall()")]
    [InlineData("InvalidParameter(wchar_t const*, wchar_t const*, wchar_t const*, unsigned int, unsigned long long)")]
    [InlineData("Writer(void*)")]
    [InlineData("CaptureCaughtException()")]
    public void Should_skip_each_exact_client_capture_function(string function)
    {
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project([
            AnalysisFixture.Frame("Avalon::Engine::CrashHandler::(anonymous namespace)::" + function),
            AnalysisFixture.Frame("Game::RealFault()", "cfi")]));
        Assert.DoesNotContain("CrashHandler", signature.Canonical, StringComparison.Ordinal);
        Assert.Contains("Game::RealFault()", signature.Canonical, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Avalon::Engine::CrashHandler::Trigger(char const*)")]
    [InlineData("Avalon::Engine::CrashHandler::(anonymous namespace)::Overflow(unsigned int*)")]
    [InlineData("Avalon::Engine::CrashHandler::SyntheticWork()")]
    public void Should_keep_fault_sites_and_non_noise_functions(string function) =>
        Assert.Contains(function, CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame(function)])).Canonical, StringComparison.Ordinal);

    [Fact]
    public void Should_fallback_for_all_noise_and_fail_when_identity_is_unavailable()
    {
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project([AnalysisFixture.Frame("Avalon::Engine::CrashHandler::CaptureCaughtException()")]));
        Assert.Contains("runtime.exe@A1+0x0000000000000010", signature.Canonical, StringComparison.Ordinal);
        CrashAnalysis empty = AnalysisFixture.Project([AnalysisFixture.Frame(null, address: "0x999999", module: null)]);
        Assert.Equal(CrashFailureCode.UngroupableAnalysis, Assert.Throws<CrashInputException>(() => CrashSignature.Create(empty)).Code);
    }

    [Fact]
    public void Should_use_five_physical_frames_and_ignore_inline_and_source_changes()
    {
        object[] frames = Enumerable.Range(0, 6).Select(i => AnalysisFixture.Frame("Function" + i + "()", "cfi", inlines: [new { function = "Inline()", file = "different.cpp", line = 99 }])).ToArray();
        SignatureIdentity signature = CrashSignature.Create(AnalysisFixture.Project(frames));
        Assert.Contains("Function4()", signature.Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("Function5()", signature.Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("Inline", signature.Canonical, StringComparison.Ordinal);
        Assert.Equal(signature.Hash, CrashSignature.Create(AnalysisFixture.Project(frames.Take(5).Select((_, i) => AnalysisFixture.Frame("Function" + i + "()", "cfi")).ToArray())).Hash);
    }

    [Fact]
    public void Should_hash_complete_canonical_tokens_before_shortening_display()
    {
        object[] frames = Enumerable.Range(0, 5).Select(i => AnalysisFixture.Frame(new string('a', 1000) + i, "cfi")).ToArray();
        SignatureIdentity first = CrashSignature.Create(AnalysisFixture.Project(frames));
        frames[4] = AnalysisFixture.Frame(new string('a', 1000) + "changed", "cfi");
        SignatureIdentity changed = CrashSignature.Create(AnalysisFixture.Project(frames));
        Assert.True(first.Display.Length <= 4096);
        Assert.True(first.Canonical.Length > 4096);
        Assert.NotEqual(first.Hash, changed.Hash);
    }
}
