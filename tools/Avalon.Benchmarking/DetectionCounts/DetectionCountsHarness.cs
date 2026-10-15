using System.Globalization;
using System.Reflection;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Benchmarking.DetectionCounts;

/// <summary>
/// How much work the creatures' range searches do in the <c>forest-combat</c> scenario, with the search filter (#982) and
/// without it (<c>MapInstance.SearchEveryDueCreature</c>): counts, not timings, which a busy machine does not change. Each
/// run builds the scenario's world, ticks its warm-up, then counts over its measured ticks the searches the range
/// detectors ran (walks of an instance's characters) and the distances they measured, and the filter's own work in the
/// spatial grids: probes, the characters' bounds bound again, the probes the bounds answered no and those the cells did,
/// cells looked at and entries walked. The fight's outcome is printed for both, which must match.
/// </summary>
/// <remarks>
/// Every count is read by name (World-side, internal): <c>CreatureRangeDetectorScript.Searches</c> and
/// <c>DistanceChecks</c> of each detector, gathered when the world is built (a killed creature loses its script, not its
/// detector's counts), and the <c>SpatialGrid</c> counts of each forest.
/// </remarks>
public static class DetectionCountsHarness
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly string[] s_detectorCounts = ["Searches", "DistanceChecks"];

    private static readonly string[] s_gridCounts = ["ProbesRun", "Bounds", "BoundSkips", "CellSkips", "ProbedCells", "ProbedEntries"];

    public static void Run(string[] args)
    {
        int warmup = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : ForestCombatScenario.WarmupTicks;
        int measured = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : ForestCombatScenario.MeasuredTicks;
        double seconds = measured * ScenarioWorld.Dt.TotalSeconds;

        Console.WriteLine($"forest-combat: {warmup} warm-up ticks, then {measured} counted ({seconds:F0} s of simulation)");
        Console.WriteLine("run," + string.Join(",", s_detectorCounts.Concat(s_gridCounts)) + ",searches_per_s,outcome");
        foreach (bool filtered in new[] { false, true })
        {
            long[] counts = Count(filtered, warmup, measured, out FightOutcome outcome);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(filtered ? "filtered" : "every search")},{string.Join(",", counts)},{counts[0] / seconds:F1},\"{outcome}\""));
        }
    }

    private static long[] Count(bool filtered, int warmup, int measured, out FightOutcome outcome)
    {
        var scenario = new ForestCombatScenario();
        using ScenarioWorld world = scenario.Build();
        var detectors = new List<CreatureRangeDetectorScript>();
        var grids = new List<object>();
        foreach (MapInstance map in world.Maps)
        {
            if (!filtered)
                typeof(MapInstance).GetMethod("SearchEveryDueCreature", Internal)!.Invoke(map, null);
            grids.Add(typeof(MapInstance).GetField("_members", Internal)!.GetValue(map)!);
            foreach (ICreature creature in map.Creatures.Values)
            {
                if (creature.Script is AggroDefendScript script)
                    detectors.Add((CreatureRangeDetectorScript)typeof(AggroDefendScript).GetField("_detector", Internal)!.GetValue(script)!);
            }
        }

        for (int tick = 0; tick < warmup; tick++)
            world.Tick();
        long[] before = Read(detectors, grids);
        for (int tick = 0; tick < measured; tick++)
            world.Tick();
        long[] after = Read(detectors, grids);

        outcome = scenario.Outcome(world);
        return after.Zip(before, (a, b) => a - b).ToArray();
    }

    private static long[] Read(List<CreatureRangeDetectorScript> detectors, List<object> grids)
    {
        long[] counts = new long[s_detectorCounts.Length + s_gridCounts.Length];
        for (int c = 0; c < s_detectorCounts.Length; c++)
        {
            PropertyInfo count = typeof(CreatureRangeDetectorScript).GetProperty(s_detectorCounts[c], Internal)!;
            foreach (CreatureRangeDetectorScript detector in detectors)
                counts[c] += (long)count.GetValue(detector)!;
        }

        Type gridType = typeof(MapInstance).GetField("_members", Internal)!.FieldType;
        for (int c = 0; c < s_gridCounts.Length; c++)
        {
            PropertyInfo count = gridType.GetProperty(s_gridCounts[c], Internal)!;
            foreach (object grid in grids)
                counts[s_detectorCounts.Length + c] += (long)count.GetValue(grid)!;
        }

        return counts;
    }
}
