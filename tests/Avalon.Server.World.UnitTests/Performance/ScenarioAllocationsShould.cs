using System.Globalization;
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
/// The gate decides in Release, which CI builds; a Debug build reports the comparison instead of failing on it. Every
/// scenario still runs in Debug, so a scenario that stopped doing its work still fails its own check. Since #854 the
/// sends no longer create a delegate per packet, whose place (stack or heap) depended on how far the JIT had got by the
/// end of the warm-up, so a Debug build, CI's runner and a developer machine in Release read the same figures within
/// run-to-run noise.
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
        output.WriteLine($"{name}: {report.BytesPerWindow:N0} B per {ScenarioMeasurement.WindowTicks}-tick window, " +
                         $"committed {entry.BytesPerWindow:N0} B ({Change(report.BytesPerWindow, entry.BytesPerWindow)}), {verdict}.");
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

    /// <summary>The change in percent, signed; from a committed 0 (<c>town-idle</c>) there is no percentage.</summary>
    private static string Change(long current, long committed)
    {
        if (committed == 0)
            return current == 0 ? "0.00%" : "from 0";

        double percent = (current - committed) * 100.0 / committed;
        return percent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
    }

    private static string RepositoryFile(string relativePath) => Path.Combine(TownNavmesh.RepositoryRoot, relativePath);
}
