using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Avalon.World.Maps.Navigation;

/// <summary>
/// The polygon <see cref="DtNavMeshQuery.FindNearestPoly" /> picks, found with less work (#978). Every movement input
/// asked for it twice, once where the player stands (the walkable raycast's start) and once where the step ends (the
/// ground snap), and each search measured every polygon in a 4 x 8 x 4 m box around the point. A route and a line of
/// sight search at both ends, a projectile's step at its start.
/// </summary>
/// <remarks>
/// <para>
/// DotRecast scores each polygon in the box: zero when the point is over it and within the tile's climb height of its
/// surface (or exactly on its edge), otherwise a positive squared distance, and keeps the first polygon with the
/// lowest score. A player on the ground scores zero on the polygon under them, and nothing scores below zero, so the
/// answer is then the first polygon, in the search's order, that scores zero.
/// </para>
/// <para>
/// So the search first looks in a narrow box: <see cref="Margin" /> to each side (0.2 m across) and the climb height
/// plus the margin up and down. Every polygon that can score zero reaches into it (the point is over the polygon or on
/// its edge, and its surface there is within the climb), the narrow box is inside the full one, and DotRecast walks the
/// tiles and each tile's bounding-volume tree in the same order for both, so the first zero in the narrow box is the
/// first zero in the full one. That needs the trees: a tile without one is searched by its polygons' corners, which
/// leave out a detail surface raised above them, so a mesh with such a tile always takes the full box. Polygons whose
/// corners all lie more than the margin to one side of the point cannot score zero and are not measured.
/// When nothing in the narrow box scores zero (a player beside the mesh, a point exactly on a wall's edge that the
/// polygon test leaves out, a step off a ledge), the full box is searched as before. On overlapping floors the floor
/// within the climb scores zero and the other does not, so the narrow box finds the same floor; a point between floors
/// with neither within the climb goes to the full search.
/// </para>
/// <para>
/// Either pass stops measuring once a polygon has scored zero, which cannot change the answer. Nothing is kept between
/// calls: the answer is the full search's for the point given, whatever the player did before. Nothing here assumes
/// the point is where someone stands (a route's end, a projectile flying flat over a slope): a point that scores zero
/// nowhere goes to the full search, so standing decides only how often the narrow box answers, never the answer.
/// </para>
/// </remarks>
internal readonly struct StandingPolygonSearch
{
    /// <summary>
    /// How far the narrow box reaches to each side of the point (0.2 m across), and past the climb height up and down.
    /// </summary>
    internal const float Margin = 0.1f;

    private readonly RcVec3f _pickExtents;
    private readonly RcVec3f _narrowExtents;
    private readonly bool _narrow;

    private StandingPolygonSearch(RcVec3f pickExtents, RcVec3f narrowExtents, bool narrow)
    {
        _pickExtents = pickExtents;
        _narrowExtents = narrowExtents;
        _narrow = narrow;
    }

    /// <summary>
    /// The search in <paramref name="pickExtents" /> (the full box's half extents) on <paramref name="navMesh" /> as it
    /// is now: the narrow box covers the highest climb of its tiles, and is not used at all when a tile has no
    /// bounding-volume tree. Read once, when the navigator loads the mesh; a tile added to the mesh afterwards is not
    /// supported (nothing does that: an instance's navigator loads a finished bake).
    /// </summary>
    public static StandingPolygonSearch For(DtNavMesh navMesh, RcVec3f pickExtents)
    {
        float climb = 0;
        bool everyTileHasATree = true;
        for (int i = 0; i < navMesh.GetMaxTiles(); i++)
        {
            DtMeshData? data = navMesh.GetTile(i)?.data;
            if (data?.header is null)
                continue;

            climb = MathF.Max(climb, data.header.walkableClimb);
            // DotRecast keeps an empty tree as none.
            everyTileHasATree &= data.bvTree is not null;
        }

        // Up and down, the climb itself: a polygon whose surface is the climb away from the point still scores zero.
        // GroundedMovementShould.Reach_every_polygon_within_the_climb_up_and_down fails with any less.
        var narrow = new RcVec3f(Margin, climb + Margin, Margin);
        bool inside = narrow.X <= pickExtents.X && narrow.Y <= pickExtents.Y && narrow.Z <= pickExtents.Z;
        return new StandingPolygonSearch(pickExtents, narrow, inside && everyTileHasATree);
    }

    /// <summary>
    /// What <see cref="DtNavMeshQuery.FindNearestPoly" /> answers for <paramref name="center" /> in the full box: the
    /// same polygon, the same nearest point, whether the point is over it (the nearest point then holds the polygon's
    /// height under it) and a status that fails exactly when its does.
    /// </summary>
    public DtStatus FindNearestPoly(DtNavMeshQuery query, RcVec3f center, IDtQueryFilter filter, out long nearestRef,
        out RcVec3f nearestPt, out bool isOverPoly)
    {
        if (_narrow)
        {
            var standing = new NearestPolygonQuery(query, center, narrow: true);
            query.QueryPolygons(center, _narrowExtents, filter, ref standing);
            if (standing.StandsOn)
            {
                nearestRef = standing.NearestRef;
                nearestPt = standing.NearestPoint;
                isOverPoly = standing.OverPoly;
                return DtStatus.DT_SUCCESS;
            }
        }

        var nearest = new NearestPolygonQuery(query, center, narrow: false);
        DtStatus status = query.QueryPolygons(center, _pickExtents, filter, ref nearest);
        if (status.Failed())
        {
            nearestRef = 0;
            nearestPt = center;
            isOverPoly = false;
            return status;
        }

        nearestRef = nearest.NearestRef;
        nearestPt = nearest.NearestPoint;
        isOverPoly = nearest.OverPoly;
        return DtStatus.DT_SUCCESS;
    }

    /// <summary>
    /// DotRecast's <see cref="DtFindNearestPolyQuery" />, scoring every polygon the same way, that stops measuring once a
    /// polygon scores zero. In the narrow pass it also skips polygons that cannot score zero.
    /// </summary>
    private struct NearestPolygonQuery : IDtPolyQuery
    {
        private readonly DtNavMeshQuery _query;
        private readonly RcVec3f _center;
        private readonly bool _narrow;
        private float _nearestDistanceSqr;

        public NearestPolygonQuery(DtNavMeshQuery query, RcVec3f center, bool narrow)
        {
            _query = query;
            _center = center;
            _narrow = narrow;
            _nearestDistanceSqr = float.MaxValue;
            NearestRef = 0;
            NearestPoint = center;
            OverPoly = false;
        }

        public long NearestRef { get; private set; }

        public RcVec3f NearestPoint { get; private set; }

        public bool OverPoly { get; private set; }

        /// <summary>Whether the narrow pass found the polygon the point stands on, which is then the full search's answer.</summary>
        public readonly bool StandsOn => NearestRef != 0 && _nearestDistanceSqr == 0;

        public void Process(DtMeshTile tile, ReadOnlySpan<int> polys, ReadOnlySpan<long> polyRefs, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                // Nothing scores below zero, so the first zero is the answer.
                if (_nearestDistanceSqr == 0)
                    return;

                if (_narrow && FarAcross(tile, tile.data.polys[polys[i]], _center))
                    continue;

                long polyRef = polyRefs[i];
                float d;

                // DtFindNearestPolyQuery.Process, unchanged from here.
                _query.ClosestPointOnPoly(polyRef, _center, out RcVec3f closestPtPoly, out bool posOverPoly);
                var diff = RcVec3f.Subtract(_center, closestPtPoly);
                if (posOverPoly)
                {
                    d = MathF.Abs(diff.Y) - tile.data.header.walkableClimb;
                    d = d > 0 ? d * d : 0;
                }
                else
                {
                    d = diff.LengthSquared();
                }

                if (d < _nearestDistanceSqr)
                {
                    NearestPoint = closestPtPoly;
                    _nearestDistanceSqr = d;
                    NearestRef = polyRef;
                    OverPoly = posOverPoly;
                }
            }
        }

        /// <summary>
        /// Whether the point lies more than <see cref="Margin" /> to one side of all the polygon's corners: then it is
        /// not over the polygon, and its nearest point (on the polygon's edges) is at least that far away, so it scores
        /// above zero.
        /// </summary>
        private static bool FarAcross(DtMeshTile tile, DtPoly poly, RcVec3f center)
        {
            float[] verts = tile.data.verts;
            int v = poly.verts[0] * 3;
            float minX = verts[v], maxX = minX, minZ = verts[v + 2], maxZ = minZ;
            for (int j = 1; j < poly.vertCount; j++)
            {
                v = poly.verts[j] * 3;
                minX = MathF.Min(minX, verts[v]);
                maxX = MathF.Max(maxX, verts[v]);
                minZ = MathF.Min(minZ, verts[v + 2]);
                maxZ = MathF.Max(maxZ, verts[v + 2]);
            }

            return center.X < minX - Margin || center.X > maxX + Margin
                || center.Z < minZ - Margin || center.Z > maxZ + Margin;
        }
    }
}
