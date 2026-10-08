using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// Scenarios measure the tick thread's allocations, so they run alone: a test allocating on another thread does not
/// count, but one competing for the CPU would stretch the wall-clock warm-up.
/// </summary>
[CollectionDefinition(nameof(ScenarioAllocations), DisableParallelization = true)]
public sealed class ScenarioAllocations;

/// <summary>
/// Gates every scenario's <see cref="ScenarioReport.BytesPerWindow" /> against the committed baseline
/// (<c>perf/scenario-allocations.json</c>) by <see cref="AllocationBaseline.Compare" />.
/// </summary>
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

        switch (AllocationBaseline.Compare(report.BytesPerWindow, entry!.BytesPerWindow))
        {
            case AllocationBaseline.Verdict.Regressed:
                Assert.Fail($"{name} allocates {report.BytesPerWindow:N0} B per {ScenarioMeasurement.WindowTicks}-tick window, " +
                            $"committed {entry.BytesPerWindow:N0} B (more than 5% and 256 B over). " +
                            $"If the increase is intended, regenerate the baseline and commit it: {Regenerate}");
                break;
            case AllocationBaseline.Verdict.Improved:
                output.WriteLine($"{name} improved: {report.BytesPerWindow:N0} B per window, committed {entry.BytesPerWindow:N0} B. " +
                                 $"Lower the committed baseline: {Regenerate}");
                break;
        }
    }

    private static string RepositoryFile(string relativePath) => Path.Combine(TownNavmesh.RepositoryRoot, relativePath);
}
