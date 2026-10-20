using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Avalon.Benchmarking.NavigationStorage;

/// <summary>
/// Construction and first-use costs that a steady-state benchmark's warmup hides (#1116).
/// Mesh construction, output buffers and code rehearsal are outside every reported query measurement.
/// </summary>
public static class NavigationStorageHarness
{
    private const int Batch = 64;

    public static void Run()
    {
        Console.WriteLine($"Runtime {Environment.Version}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        foreach (string map in new[] { "Town", "Forest33" })
        {
            var fixture = new NavigationStorageFixture(map);
            long[] path = new long[fixture.PolygonCount * 4];
            var rehearsal = new ReusingNavMeshQuery(fixture.Mesh);
            for (int i = 0; i < 100; i++)
            {
                Search(rehearsal, fixture, path, "Path");
                Search(rehearsal, fixture, path, "Unreachable");
                Search(rehearsal, fixture, path, "Surface");
            }
            for (int i = 0; i < 10; i++)
                GC.KeepAlive(new ReusingNavMeshQuery(fixture.Mesh));

            var queries = new ReusingNavMeshQuery[Batch];
            Console.WriteLine($"{map}: {fixture.PolygonCount} polygons");
            for (int sample = 0; sample < 5; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Fill(queries, fixture.Mesh);
                double microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds / Batch;
                long allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / Batch;
                Console.WriteLine($"  construct {sample + 1}: {microseconds:F3} us/query, {allocated} B/query");
            }
            Array.Clear(queries);
            long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
            Fill(queries, fixture.Mesh);
            long retained = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;
            Console.WriteLine($"  retained before use: {retained / Batch} B/query (whole-heap delta across {Batch} live queries)");
            GC.KeepAlive(queries);

            foreach (string operation in new[] { "Path", "Unreachable", "Surface" })
            {
                Fill(queries, fixture.Mesh);
                for (int pass = 0; pass < 2; pass++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    int result = SearchBatch(queries, fixture, path, operation);
                    double microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds / Batch;
                    long allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / Batch;
                    Console.WriteLine($"  {operation}, pass {pass + 1}: {microseconds:F3} us/query, {allocated} B/query, result {result / Batch}");
                }
            }
            GC.KeepAlive(queries);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fill(ReusingNavMeshQuery[] queries, DtNavMesh mesh)
    {
        for (int i = 0; i < queries.Length; i++)
            queries[i] = new ReusingNavMeshQuery(mesh);
    }

    private static int SearchBatch(ReusingNavMeshQuery[] queries, NavigationStorageFixture fixture, long[] path, string operation)
    {
        int result = 0;
        foreach (ReusingNavMeshQuery query in queries)
            result += Search(query, fixture, path, operation);
        return result;
    }

    private static int Search(ReusingNavMeshQuery query, NavigationStorageFixture fixture, long[] path, string operation)
    {
        DtStatus status;
        int count;
        if (operation == "Surface")
        {
            status = query.MoveAlongSurfaceReusing(fixture.StartRef, fixture.Start, fixture.End, fixture.Filter,
                out RcVec3f _, path, out count, path.Length);
        }
        else
        {
            status = query.FindPath(fixture.StartRef, fixture.EndRef, fixture.Start, fixture.End,
                operation == "Path" ? fixture.Filter : fixture.UnreachableFilter, path, out count, path.Length);
        }
        if (status.Failed() || count == 0)
            throw new InvalidOperationException($"The {operation} storage probe failed.");
        return count;
    }
}
