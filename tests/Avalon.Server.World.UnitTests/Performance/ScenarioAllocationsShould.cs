using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// Scenarios measure the tick thread's allocations, so they run alone in this assembly: a test allocating on another
/// thread does not count, but one competing for the CPU would stretch the wall-clock warm-up. Other test assemblies
/// still run in parallel processes under a solution-wide <c>dotnet test</c>; the minimum of five windows absorbs that.
/// </summary>
[CollectionDefinition(nameof(ScenarioAllocations), DisableParallelization = true)]
public sealed class ScenarioAllocations;

/// <summary>
/// Gates every scenario's <see cref="ScenarioReport.BytesPerWindow" /> against the committed baseline
/// (<c>perf/scenario-allocations.json</c>) by <see cref="AllocationBaseline.Compare" />.
/// </summary>
/// <remarks>
/// The gate decides in Release, which CI builds; the committed figures are CI's Linux Release run. The figure depends
/// on the build and the platform by more than the 5% band (the per-send <c>Encrypt</c> delegate, 64 B per walking
/// player per tick, is on the heap in Debug and on Linux but kept on the stack by the Windows Release JIT), so a
/// Debug build reports the comparison instead of failing on it. Every scenario still runs in Debug, so a scenario
/// that stopped doing its work still fails its own check.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public sealed class ScenarioAllocationsShould(ITestOutputHelper output)
{
    private const string Regenerate =
        "dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-allocations perf/scenario-allocations.json";

    public static TheoryData<string> Names => new(Scenarios.All.Select(s => s.Name));

    [Theory]
    [MemberData(nameof(Names))]
    public void Allocate_no_more_than_the_committed_baseline(string name)
    {
        AllocationBaseline.File committed = AllocationBaseline.Read(RepositoryFile("perf/scenario-allocations.json"));
        Assert.True(committed.Scenarios.TryGetValue(name, out AllocationBaseline.Entry? entry),
            $"{name} has no committed baseline; run: {Regenerate}");

        ScenarioReport report = ScenarioMeasurement.Run(Scenarios.Get(name), TimeSpan.FromSeconds(5), measureTicks: 0);

        AllocationBaseline.Verdict verdict = AllocationBaseline.Compare(report.BytesPerWindow, entry!.BytesPerWindow);
        double change = (report.BytesPerWindow - entry.BytesPerWindow) * 100.0 / entry.BytesPerWindow;
        output.WriteLine($"{name}: {report.BytesPerWindow:N0} B per {ScenarioMeasurement.WindowTicks}-tick window, " +
                         $"committed {entry.BytesPerWindow:N0} B ({change:+0.00;-0.00;0.00}%), {verdict}.");
#if DEBUG
        output.WriteLine("Debug build: reported only; the allocation gate decides in Release, which CI builds.");
#endif

        switch (verdict)
        {
#if !DEBUG
            case AllocationBaseline.Verdict.Regressed:
                Assert.Fail($"{name} allocates {report.BytesPerWindow:N0} B per {ScenarioMeasurement.WindowTicks}-tick window, " +
                            $"committed {entry.BytesPerWindow:N0} B (more than 5% and 256 B over). " +
                            $"If the increase is intended, regenerate the baseline and commit it: {Regenerate}");
                break;
#endif
            case AllocationBaseline.Verdict.Improved:
                output.WriteLine($"{name} improved: {report.BytesPerWindow:N0} B per window, committed {entry.BytesPerWindow:N0} B. " +
                                 $"Lower the committed baseline: {Regenerate}");
                break;
        }
    }

    private static string RepositoryFile(string relativePath) => Path.Combine(TownNavmesh.RepositoryRoot, relativePath);
}
