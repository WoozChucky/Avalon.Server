using System.Globalization;
using System.Reflection;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Benchmarking.HitCandidates;

/// <summary>
/// How many units a hit query reads over the spatial grid (#982), against the full scan's every member: the
/// <c>forest-combat</c> scenario's fight (four forests, a party of three in each, their casts and the creatures'), run
/// once with its forests' hit queries over the grid and once scanning every member
/// (<c>MapInstance.FindHitsByFullScan</c>), each for the scenario's length. Exact counts, not timings: they do not depend
/// on the machine's load. The two fights must be the same, kill for kill and byte for byte on every connection, or the
/// harness says so: a hit list that differed would change what follows it.
/// </summary>
public static class HitCandidatesHarness
{
    private static readonly string[] s_shapes = ["circle", "cone", "segment"];

    public static void Run(string[] args)
    {
        _ = args;
        Fight overGrid = RunFight(fullScan: false);
        Fight byScan = RunFight(fullScan: true);

        bool same = overGrid.Outcome == byScan.Outcome && overGrid.Bytes.SequenceEqual(byScan.Bytes)
                    && overGrid.Packets.SequenceEqual(byScan.Packets);
        Console.WriteLine($"grid: {overGrid.Outcome}");
        Console.WriteLine($"scan: {byScan.Outcome}");
        Console.WriteLine(same
            ? "The fights are the same: outcome, and bytes and packets sent on every connection."
            : "THE FIGHTS DIFFER");

        Console.WriteLine("measured ticks only; per query: members (what a full scan reads), entries the grid visited, units read and tested, hits");
        Console.WriteLine("shape,queries,members,visited,examined,hits,examined_by_scan");
        long[] total = new long[5];
        long scanExamined = 0;
        for (int s = 0; s < s_shapes.Length; s++)
        {
            Count grid = overGrid.Counts[s];
            Count scan = byScan.Counts[s];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{s_shapes[s]},{grid.Queries},{Per(grid.Members, grid.Queries)},{Per(grid.Visited, grid.Queries)},{Per(grid.Examined, grid.Queries)},{Per(grid.Hits, grid.Queries)},{Per(scan.Examined, scan.Queries)}"));
            total[0] += grid.Queries;
            total[1] += grid.Members;
            total[2] += grid.Visited;
            total[3] += grid.Examined;
            total[4] += grid.Hits;
            scanExamined += scan.Examined;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"all,{total[0]},{Per(total[1], total[0])},{Per(total[2], total[0])},{Per(total[3], total[0])},{Per(total[4], total[0])},{Per(scanExamined, total[0])}"));
    }

    private static Fight RunFight(bool fullScan)
    {
        var scenario = new ForestCombatScenario();
        using ScenarioWorld world = scenario.Build();
        MethodInfo scanEveryMember = typeof(MapInstance).GetMethod("FindHitsByFullScan", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (fullScan)
        {
            foreach (MapInstance map in world.Maps)
                scanEveryMember.Invoke(map, null);
        }

        FixedLength length = scenario.Length!;
        for (int t = 0; t < length.WarmupTicks; t++)
            world.Tick();
        world.MarkProgress();
        Count[] before = Counts(world);

        for (int t = 0; t < length.MeasuredTicks; t++)
            world.Tick();
        scenario.Verify(world);

        Count[] after = Counts(world);
        var measured = new Count[s_shapes.Length];
        for (int s = 0; s < measured.Length; s++)
            measured[s] = after[s] - before[s];

        return new Fight(
            scenario.Outcome(world),
            world.Connections.Select(c => c.BytesWritten).ToArray(),
            world.Connections.Select(c => c.Sent).ToArray(),
            measured);
    }

    // Every forest's queries, summed by shape.
    private static Count[] Counts(ScenarioWorld world)
    {
        PropertyInfo countsProperty = typeof(UnitHitQuery).GetProperty("Counts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var counts = new Count[s_shapes.Length];
        foreach (MapInstance map in world.Maps)
        {
            var byShape = (Array)countsProperty.GetValue((UnitHitQuery)map.Hits)!;
            for (int s = 0; s < counts.Length; s++)
            {
                object shape = byShape.GetValue(s)!;
                counts[s] += new Count(Field(shape, "Queries"), Field(shape, "Members"), Field(shape, "Visited"),
                    Field(shape, "Examined"), Field(shape, "Hits"));
            }
        }

        return counts;
    }

    private static long Field(object counts, string name) => (long)counts.GetType().GetField(name)!.GetValue(counts)!;

    private static string Per(long value, long queries) =>
        queries == 0 ? "-" : ((double)value / queries).ToString("F2", CultureInfo.InvariantCulture);

    private sealed record Fight(FightOutcome Outcome, long[] Bytes, int[] Packets, Count[] Counts);

    private readonly record struct Count(long Queries, long Members, long Visited, long Examined, long Hits)
    {
        public static Count operator +(Count a, Count b) =>
            new(a.Queries + b.Queries, a.Members + b.Members, a.Visited + b.Visited, a.Examined + b.Examined, a.Hits + b.Hits);

        public static Count operator -(Count a, Count b) =>
            new(a.Queries - b.Queries, a.Members - b.Members, a.Visited - b.Visited, a.Examined - b.Examined, a.Hits - b.Hits);
    }
}
