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
/// spatial grids (probes, cells looked at, entries walked). The fight's outcome is printed for both, which must match.
/// </summary>
/// <remarks>
/// Every count is read by name (World-side, internal): <c>CreatureRangeDetectorScript.Searches</c> and
/// <c>DistanceChecks</c> of each detector, gathered when the world is built (a killed creature loses its script, not its
/// detector's counts), and <c>SpatialGrid.ProbesRun</c>, <c>ProbedCells</c> and <c>ProbedEntries</c> of each forest.
/// </remarks>
public static class DetectionCountsHarness
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run(string[] args)
    {
        int warmup = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : ForestCombatScenario.WarmupTicks;
        int measured = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : ForestCombatScenario.MeasuredTicks;
        double seconds = measured * ScenarioWorld.Dt.TotalSeconds;

        Console.WriteLine($"forest-combat: {warmup} warm-up ticks, then {measured} counted ({seconds:F0} s of simulation)");
        Console.WriteLine("run,searches,searches_per_s,distance_checks,distance_checks_per_s,probes,cells_probed,entries_walked,outcome");
        foreach (bool filtered in new[] { false, true })
        {
            Counts counts = Count(filtered, warmup, measured, out FightOutcome outcome);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(filtered ? "filtered" : "every search")},{counts.Searches},{counts.Searches / seconds:F1},{counts.DistanceChecks},{counts.DistanceChecks / seconds:F1},{counts.Probes},{counts.Cells},{counts.Entries},\"{outcome}\""));
        }
    }

    private static Counts Count(bool filtered, int warmup, int measured, out FightOutcome outcome)
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
        Counts before = Read(detectors, grids);
        for (int tick = 0; tick < measured; tick++)
            world.Tick();
        Counts after = Read(detectors, grids);

        outcome = scenario.Outcome(world);
        return after - before;
    }

    private static Counts Read(List<CreatureRangeDetectorScript> detectors, List<object> grids)
    {
        PropertyInfo searches = typeof(CreatureRangeDetectorScript).GetProperty("Searches", Internal)!;
        PropertyInfo distances = typeof(CreatureRangeDetectorScript).GetProperty("DistanceChecks", Internal)!;
        Type gridType = typeof(MapInstance).GetField("_members", Internal)!.FieldType;
        PropertyInfo probes = gridType.GetProperty("ProbesRun", Internal)!;
        PropertyInfo cells = gridType.GetProperty("ProbedCells", Internal)!;
        PropertyInfo entries = gridType.GetProperty("ProbedEntries", Internal)!;

        var counts = new Counts();
        foreach (CreatureRangeDetectorScript detector in detectors)
        {
            counts.Searches += (long)searches.GetValue(detector)!;
            counts.DistanceChecks += (long)distances.GetValue(detector)!;
        }

        foreach (object grid in grids)
        {
            counts.Probes += (long)probes.GetValue(grid)!;
            counts.Cells += (long)cells.GetValue(grid)!;
            counts.Entries += (long)entries.GetValue(grid)!;
        }

        return counts;
    }

    private struct Counts
    {
        public long Searches, DistanceChecks, Probes, Cells, Entries;

        public static Counts operator -(Counts a, Counts b) => new()
        {
            Searches = a.Searches - b.Searches,
            DistanceChecks = a.DistanceChecks - b.DistanceChecks,
            Probes = a.Probes - b.Probes,
            Cells = a.Cells - b.Cells,
            Entries = a.Entries - b.Entries,
        };
    }
}
