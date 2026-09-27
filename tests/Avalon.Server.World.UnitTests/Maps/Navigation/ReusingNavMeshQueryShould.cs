using System.Globalization;
using System.Text;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Xunit;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

/// <summary>
/// #638: <see cref="ReusingNavMeshQuery.MoveAlongSurfaceReusing" /> is a copy of DotRecast's
/// <see cref="DtNavMeshQuery.MoveAlongSurface" /> with a reused queue. This compares the two, call by
/// call, over the real town navmesh, so a DotRecast update that changes the original fails here
/// instead of leaving the copy quietly different.
/// </summary>
public class ReusingNavMeshQueryShould
{
    /// <summary>
    /// Maps/TownLayouts/1.json, baked as ChunkLayoutNavmeshBuilder bakes it: rotation 0, cell 30.
    /// Read from the test output folder, where the world server's Maps folder is copied, rather than
    /// through the builder, which reads the working directory other tests change.
    /// </summary>
    private static readonly Lazy<DtNavMesh> TownNavMesh = new(BakeTown, isThreadSafe: true);

    private static DtNavMesh BakeTown()
    {
        (string Name, Vector3 Origin)[] chunks =
        [
            ("town_sw_01", new Vector3(0, 0, 0)), ("town_se_01", new Vector3(30, 0, 0)),
            ("town_nw_01", new Vector3(0, 0, 30)), ("town_ne_01", new Vector3(30, 0, 30)),
        ];

        var sb = new StringBuilder();
        int offset = 0;
        foreach ((string name, Vector3 origin) in chunks)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Maps", "Chunks", $"{name}.obj");
            int count = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    Vector3 w = ChunkRotation.LocalToWorld(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture), 0, 30f, origin);
                    sb.Append(CultureInfo.InvariantCulture, $"v {w.x} {w.y} {w.z}\n");
                    count++;
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    sb.Append('f');
                    for (int i = 1; i < parts.Length; i++)
                        sb.Append(' ').Append(int.Parse(parts[i].Split('/')[0], CultureInfo.InvariantCulture) + offset);
                    sb.Append('\n');
                }
            }

            offset += count;
        }

        string combined = Path.Combine(Path.GetTempPath(), $"avalon-reusing-query-{Guid.NewGuid():N}.obj");
        File.WriteAllText(combined, sb.ToString());
        try
        {
            var geom = RcSampleInputGeomProvider.LoadFile(combined);
            var result = new TileNavMeshBuilder().Build(geom, NavmeshBuildSettings.Create());
            Assert.NotNull(result?.NavMesh);
            return result!.NavMesh;
        }
        finally
        {
            File.Delete(combined);
        }
    }

    [Fact]
    public void Move_along_the_surface_exactly_as_DotRecast_does()
    {
        DtNavMesh mesh = TownNavMesh.Value;
        var original = new DtNavMeshQuery(mesh);
        var reusing = new ReusingNavMeshQuery(mesh);
        var filter = new DtQueryDefaultFilter();
        var extents = new RcVec3f(2, 4, 2);
        var rng = new Random(638);

        Span<long> visitedA = stackalloc long[16];
        Span<long> visitedB = stackalloc long[16];
        int compared = 0, moved = 0, truncated = 0;

        for (int i = 0; i < 3000; i++)
        {
            var start = new RcVec3f((float)rng.NextDouble() * 60f, 0.2f, (float)rng.NextDouble() * 60f);
            // Mostly the half-metre steps FindPath takes, with some long moves across rooms and walls.
            float reach = i % 4 == 0 ? 20f : 0.75f;
            var end = new RcVec3f(start.X + ((float)rng.NextDouble() * 2f - 1f) * reach, start.Y,
                start.Z + ((float)rng.NextDouble() * 2f - 1f) * reach);
            // Every tenth call with a tiny visited buffer, so the buffer-too-small branch is compared too.
            int maxVisited = i % 10 == 0 ? 2 : 16;

            original.FindNearestPoly(start, extents, filter, out long startRef, out RcVec3f onPoly, out _);
            if (startRef == 0)
                continue;

            visitedA.Clear();
            visitedB.Clear();
            DtStatus a = original.MoveAlongSurface(startRef, onPoly, end, filter, out RcVec3f posA, visitedA, out int countA, maxVisited);
            DtStatus b = reusing.MoveAlongSurfaceReusing(startRef, onPoly, end, filter, out RcVec3f posB, visitedB, out int countB, maxVisited);

            Assert.Equal(a.Value, b.Value);
            Assert.Equal(posA, posB);
            Assert.Equal(countA, countB);
            Assert.True(visitedA[..countA].SequenceEqual(visitedB[..countB]), $"visited differs at call {i}");

            compared++;
            if (countA > 1) moved++;
            if ((a.Value & DtStatus.DT_BUFFER_TOO_SMALL.Value) != 0) truncated++;
        }

        // The comparison must have exercised the interesting paths, not only rejected starts.
        Assert.True(compared > 1000, $"only {compared} calls compared");
        Assert.True(moved > 100, $"only {moved} calls crossed a polygon");
        Assert.True(truncated > 0, "the buffer-too-small branch was never reached");
    }
}
