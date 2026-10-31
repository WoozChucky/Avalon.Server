using System.Globalization;
using System.Reflection;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Benchmarking.CandidateRatio;

/// <summary>
/// How many creatures a view's spatial index visits for each one in view (#982), over generated forests: for each seed,
/// the forest as production builds it (<see cref="ScenarioWorld.NewForestInstance" />: its layout, spawn table and
/// placement), and one client standing every 5 m along the main path, from the entry chunk's centre to the boss chunk's.
/// At each point its view is decided afresh (nothing tracked, so the 50 m radius decides), over the index, and the
/// creatures the index visited (its candidates) are counted against the creatures in view and the forest's creatures (a
/// full scan's candidates). The client's own character is in both counts of the index and left out here.
/// </summary>
/// <remarks>
/// The main path is the chain of adjacent chunks from the boss chunk (the deepest chunk when there is none) down to the
/// entry, each one step shallower than the last: the chunks' depths are their distances from the entry, so this is a
/// shortest way through, the main path wherever the generator did not join a branch to it.
/// </remarks>
public static class CandidateRatioHarness
{
    private const float Step = 5f;

    public static void Run(string[] args)
    {
        int first = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 1;
        int seeds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;

        MethodInfo decideView = typeof(MapInstance).GetMethod("DecideView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        PropertyInfo lastCandidates = typeof(CharacterCharacterGameState)
            .GetProperty("LastCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var ratios = new List<double>();
        long candidates = 0, visible = 0, scanned = 0, samples = 0, empty = 0, skipped = 0;
        Console.WriteLine("seed,creatures,path_chunks,samples,candidates_per_view,visible_per_view,candidate_to_visible");
        for (int seed = first; seed < first + seeds; seed++)
        {
            using var world = ScenarioWorld.CreateWithReferenceData(seed: seed);
            MapInstance forest;
            try
            {
                forest = world.NewForestInstance(seed);
            }
            catch (InvalidOperationException e)
            {
                // A seed the scenario world refuses to build (a chunk outside every depth band): not a forest to measure.
                Console.Error.WriteLine($"seed {seed} skipped: {e.Message}");
                skipped++;
                continue;
            }

            CharacterEntity watcher = world.NewCharacter(1);
            world.Join(forest, watcher, forest.EntrySpawnWorldPos);
            Action<CharacterEntity, List<IWorldObject>> decide = decideView.CreateDelegate<Action<CharacterEntity, List<IWorldObject>>>(forest);
            var noObjects = new List<IWorldObject>();
            int creatures = forest.Creatures.Count;

            List<Vector3> path = MainPath(forest.Layout);
            long seedCandidates = 0, seedVisible = 0, seedSamples = 0;
            foreach (Vector3 point in Along(path))
            {
                watcher.Position = point;
                watcher.CharacterGameState.Reset();
                decide(watcher, noObjects);
                int seen = watcher.CharacterGameState.NewObjects.Count - 1;
                int visited = (int)lastCandidates.GetValue(watcher.CharacterGameState)! - 1;
                seedCandidates += visited;
                seedVisible += seen;
                seedSamples++;
                if (seen > 0)
                    ratios.Add((double)visited / seen);
                else
                    empty++;
            }

            candidates += seedCandidates;
            visible += seedVisible;
            scanned += (long)creatures * seedSamples;
            samples += seedSamples;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{seed},{creatures},{path.Count},{seedSamples},{(double)seedCandidates / seedSamples:F1},{(double)seedVisible / seedSamples:F1},{(seedVisible == 0 ? double.NaN : (double)seedCandidates / seedVisible):F2}"));
        }

        ratios.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"seeds={seeds - skipped} (skipped {skipped}); views={samples}; views with none in view={empty}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"per view: creatures (full scan) {(double)scanned / samples:F1}, candidates {(double)candidates / samples:F1}, in view {(double)visible / samples:F1}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"candidates / in view, pooled: {(double)candidates / visible:F2}; per view (views with any in view): p10 {Percentile(ratios, 0.1):F2}, median {Percentile(ratios, 0.5):F2}, p90 {Percentile(ratios, 0.9):F2}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"candidates / full scan, pooled: {(double)candidates / scanned:F3}"));
    }

    // The centres of the chunks from the entry to the boss chunk, each adjacent to the last and one step deeper.
    private static List<Vector3> MainPath(ChunkLayout layout)
    {
        var byCell = layout.Chunks.ToDictionary(c => (c.GridX, c.GridZ));
        PlacedChunk end = layout.BossChunk ?? layout.Chunks.MaxBy(c => c.Depth)!;
        var path = new List<Vector3> { Centre(end, layout.CellSize) };
        PlacedChunk at = end;
        while (at.Depth > 0)
        {
            PlacedChunk? next = null;
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (byCell.TryGetValue(((short)(at.GridX + dx), (short)(at.GridZ + dz)), out PlacedChunk? neighbour)
                    && neighbour.Depth == at.Depth - 1)
                {
                    next = neighbour;
                    break;
                }
            }

            if (next is null)
                break;
            at = next;
            path.Add(Centre(at, layout.CellSize));
        }

        path.Reverse();
        return path;
    }

    private static Vector3 Centre(PlacedChunk chunk, float cellSize) =>
        new(chunk.WorldPos.x + cellSize / 2, chunk.WorldPos.y, chunk.WorldPos.z + cellSize / 2);

    private static IEnumerable<Vector3> Along(List<Vector3> path)
    {
        yield return path[0];
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 from = path[i - 1], to = path[i];
            float length = Vector3.Distance(from, to);
            for (float d = Step; d <= length; d += Step)
                yield return Vector3.Lerp(from, to, d / length);
        }
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Count - 1, Math.Round(p * (sorted.Count - 1)))];
}
