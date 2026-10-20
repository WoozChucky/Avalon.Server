using Avalon.Common.Mathematics;
using Avalon.World.Testing.Scenarios;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Avalon.World.Testing.Navigation;

/// <summary>A long reachable route and an exhaustive unsuccessful search on the same finished, tiled mesh.</summary>
public sealed class NavigationStorageFixture
{
    public DtNavMesh Mesh { get; }
    public long StartRef { get; }
    public long EndRef { get; }
    public RcVec3f Start { get; }
    public RcVec3f End { get; }
    public IDtQueryFilter Filter { get; } = new DtQueryDefaultFilter();
    public IDtQueryFilter UnreachableFilter { get; }
    public int PolygonCount { get; }

    public NavigationStorageFixture(string map)
    {
        Mesh = map == "Town" ? TownNavmesh.Shared : ForestLayouts.For(int.Parse(map.AsSpan(6))).NavMesh;
        Vector3 entry = map == "Town" ? TownNavmesh.Layout().EntrySpawnWorldPos
            : ForestLayouts.For(int.Parse(map.AsSpan(6))).Layout.EntrySpawnWorldPos;
        var query = new DtNavMeshQuery(Mesh);
        query.FindNearestPoly(new RcVec3f(entry.x, entry.y, entry.z), new RcVec3f(2, 4, 2), Filter,
            out long startRef, out RcVec3f start, out _);
        if (startRef == 0)
            throw new InvalidOperationException("The navigation fixture's entry has no polygon.");
        StartRef = startRef;
        Start = start;

        var candidates = new List<(long Ref, RcVec3f Point)>();
        for (int i = 0; i < Mesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = Mesh.GetTile(i);
            if (tile?.data?.header is null)
                continue;
            PolygonCount += tile.data.header.polyCount;
            foreach (DtPoly poly in tile.data.polys)
            {
                if (poly.GetPolyType() != DtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                RcVec3f point = RcVec3f.Zero;
                for (int v = 0; v < poly.vertCount; v++)
                {
                    int offset = poly.verts[v] * 3;
                    point += new RcVec3f(tile.data.verts[offset], tile.data.verts[offset + 1], tile.data.verts[offset + 2]);
                }
                point *= 1f / poly.vertCount;
                candidates.Add((Mesh.GetPolyRefBase(tile) | (long)poly.index, point));
            }
        }

        // Select a genuinely reachable far end, rather than accidentally benchmarking a nearby island.
        candidates.Sort((a, b) => RcVec3f.DistanceSquared(start, b.Point).CompareTo(RcVec3f.DistanceSquared(start, a.Point)));
        long[] corridor = new long[PolygonCount * 4];
        foreach ((long refs, RcVec3f point) in candidates)
        {
            DtStatus status = query.FindPath(StartRef, refs, Start, point, Filter, corridor, out int count, corridor.Length);
            if (!status.Succeeded() || count < 10 || (status.Value & DtStatus.DT_PARTIAL_RESULT.Value) != 0)
                continue;
            EndRef = refs;
            End = point;
            UnreachableFilter = new ExcludePolygonFilter(refs);
            return;
        }
        throw new InvalidOperationException("The navigation fixture has no long reachable route.");
    }

    // Makes the valid goal unreachable without mutating the shared mesh; the search must exhaust its component.
    private sealed class ExcludePolygonFilter(long excluded) : IDtQueryFilter
    {
        private readonly DtQueryDefaultFilter _inner = new();

        public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly) =>
            refs != excluded && _inner.PassFilter(refs, tile, poly);

        public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
            long curRef, DtMeshTile curTile, DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly) =>
            _inner.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
    }
}
