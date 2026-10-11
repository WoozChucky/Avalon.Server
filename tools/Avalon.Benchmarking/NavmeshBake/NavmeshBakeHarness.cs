using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Toolset.Builder;

namespace Avalon.Benchmarking.NavmeshBake;

/// <summary>
/// Multi-worker bake measurements over the committed four-chunk town. Process-wide allocation counters include
/// DotRecast's tile workers. Forced collections outside the timed bake isolate retained managed data; they do not
/// measure natural GC pause improvements or the peak heap during the bake. Geometry preparation is setup cost.
/// </summary>
public static class NavmeshBakeHarness
{
    public static void Run(string[] args)
    {
        int pairs = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 8;
        if (pairs <= 0)
            throw new ArgumentOutOfRangeException(nameof(args), "The number of sample pairs must be positive");
        var fixture = new NavmeshBakeFixture();
        Console.WriteLine($"{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription} | {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"processors={Environment.ProcessorCount}; serverGC={GCSettings.IsServerGC}; latency={GCSettings.LatencyMode}; production.keepInterResults={NavmeshBuildSettings.Create().keepInterResults}");
        Console.WriteLine($"fixture=committed-town; chunks={fixture.ChunkCount}; vertices={fixture.Geometry.vertices.Length / 3}; triangles={fixture.Geometry.faces.Length / 3}; pairs={pairs}; warmup=2 per mode");
        Console.WriteLine("Timed tile rows exclude OBJ composition/loading; production rows include the real Task.Run, OBJ composition/loading and tile bake. Allocated bytes cover all managed threads. Live bytes use forced GC outside timing.");
        for (int i = 0; i < 2; i++)
        {
            Warmup(fixture, true);
            Warmup(fixture, false);
            WarmupProduction(fixture);
        }

        var samples = new List<Sample>();
        Console.WriteLine("mode,sample,elapsed_ms,allocated_B,build_live_B,navmesh_live_B,temporary_live_B,retained_intermediate_tiles,gen0,gen1,gen2");
        for (int i = 0; i < pairs; i++)
        {
            // Alternate AB/BA so warming or drift cannot always favour one setting.
            bool first = i % 2 == 0;
            Sample a = MeasureTile(fixture, first, i);
            Write(a);
            samples.Add(a);
            Sample b = MeasureTile(fixture, !first, i);
            Write(b);
            samples.Add(b);
            Sample production = MeasureProduction(fixture, i);
            Write(production);
            samples.Add(production);
        }

        Console.WriteLine("summary_mode,mean_ms,median_ms,mean_allocated_B,mean_build_live_B,mean_navmesh_live_B,mean_temporary_live_B");
        foreach (IGrouping<string, Sample> group in samples.GroupBy(s => s.Mode))
        {
            double[] elapsed = group.Select(s => s.ElapsedMs).Order().ToArray();
            double median = (elapsed[(elapsed.Length - 1) / 2] + elapsed[elapsed.Length / 2]) / 2;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{group.Key},{group.Average(s => s.ElapsedMs):F3},{median:F3},{group.Average(s => s.AllocatedBytes):F0},{group.Average(s => s.BuildLiveBytes):F0},{group.Average(s => s.NavmeshLiveBytes):F0},{group.Average(s => s.TemporaryLiveBytes):F0}"));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Warmup(NavmeshBakeFixture fixture, bool keep)
    {
        NavMeshBuildResult result = fixture.Bake(keep);
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WarmupProduction(NavmeshBakeFixture fixture)
    {
        DtNavMesh mesh = fixture.BakeProduction();
        GC.KeepAlive(mesh);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Sample MeasureTile(NavmeshBakeFixture fixture, bool keep, int iteration)
    {
        long baseline = CollectLiveBytes();
        HeldBuild held = BuildAndMeasureRetained(fixture, keep, baseline);
        // BuildAndMeasureRetained returned only NavMesh. Its RcBuilderResult graph is now unreachable.
        long navmeshLive = CollectLiveBytes() - baseline;
        GC.KeepAlive(held.Mesh);
        return new Sample(keep ? "tile-keep" : "tile-release", iteration, held.ElapsedMs, held.AllocatedBytes,
            held.BuildLiveBytes, navmeshLive, held.BuildLiveBytes - navmeshLive, held.IntermediateTiles,
            held.Gen0, held.Gen1, held.Gen2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HeldBuild BuildAndMeasureRetained(NavmeshBakeFixture fixture, bool keep, long baseline)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        NavMeshBuildResult result = fixture.Bake(keep);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        gen0 = GC.CollectionCount(0) - gen0;
        gen1 = GC.CollectionCount(1) - gen1;
        gen2 = GC.CollectionCount(2) - gen2;
        long live = CollectLiveBytes() - baseline;
        int retained = 0;
        foreach (RcBuilderResult tile in result.RecastBuilderResults)
        {
            if (tile.SolidHeightfiled is not null || tile.CompactHeightfield is not null || tile.ContourSet is not null)
                retained++;
        }

        var held = new HeldBuild(result.NavMesh, elapsed, bytes, live, retained, gen0, gen1, gen2);
        GC.KeepAlive(result);
        return held;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Sample MeasureProduction(NavmeshBakeFixture fixture, int iteration)
    {
        long baseline = CollectLiveBytes();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        DtNavMesh mesh = fixture.BakeProduction();
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        gen0 = GC.CollectionCount(0) - gen0;
        gen1 = GC.CollectionCount(1) - gen1;
        gen2 = GC.CollectionCount(2) - gen2;
        long live = CollectLiveBytes() - baseline;
        GC.KeepAlive(mesh);
        return new Sample("production", iteration, elapsed, bytes, live, live, 0, 0, gen0, gen1, gen2);
    }

    private static long CollectLiveBytes()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        return GC.GetTotalMemory(forceFullCollection: false);
    }

    private static void Write(Sample sample) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{sample.Mode},{sample.Iteration},{sample.ElapsedMs:F3},{sample.AllocatedBytes},{sample.BuildLiveBytes},{sample.NavmeshLiveBytes},{sample.TemporaryLiveBytes},{sample.IntermediateTiles},{sample.Gen0},{sample.Gen1},{sample.Gen2}"));

    private sealed record HeldBuild(DtNavMesh Mesh, double ElapsedMs, long AllocatedBytes, long BuildLiveBytes,
        int IntermediateTiles, int Gen0, int Gen1, int Gen2);

    private sealed record Sample(string Mode, int Iteration, double ElapsedMs, long AllocatedBytes, long BuildLiveBytes,
        long NavmeshLiveBytes, long TemporaryLiveBytes, int IntermediateTiles, int Gen0, int Gen1, int Gen2);
}
