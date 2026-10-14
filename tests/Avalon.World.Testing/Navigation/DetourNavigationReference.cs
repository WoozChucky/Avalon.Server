using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Avalon.World.Testing.Navigation;

/// <summary>
/// <c>MapNavigator</c>'s queries as they were before #978, written against DotRecast's own full nearest-polygon search
/// (<see cref="DtNavMeshQuery.FindNearestPoly" /> in a 4 x 8 x 4 m box) and nothing of <c>MapNavigator</c>: a player's
/// grounded step, the walkable raycast, the ground search, a route and a line of sight. It is the reference the
/// navigator is compared against, in tests and in the navigation benchmarks; it never changes with the navigator.
/// </summary>
public sealed class DetourNavigationReference
{
    private const int MaxPolys = 256;
    private const int MaxSmooth = 2048;
    private const float StepSize = 0.5f;
    private const float Slop = 0.01f;
    private static readonly RcVec3f s_pickExtents = new(2, 4, 2);

    // The navigator's own query type, for its surface move (ReusingNavMeshQueryShould holds it to DotRecast's);
    // its nearest-polygon search is DotRecast's, unchanged.
    private readonly ReusingNavMeshQuery _query;
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();
    private readonly long[] _corridor = new long[MaxPolys];

    public DetourNavigationReference(DtNavMesh navMesh)
    {
        _query = new ReusingNavMeshQuery(navMesh);
    }

    /// <summary>The step from <paramref name="from" /> towards <paramref name="to" />, on the ground.</summary>
    public Vector3 MoveOnGround(Vector3 from, Vector3 to)
    {
        Vector3 clamped = RaycastWalkable(from, to, out long startRef);
        if (startRef == 0)
            return new Vector3(clamped.x, from.y, clamped.z);

        if (clamped.x == from.x && clamped.z == from.z)
        {
            var start = new RcVec3f(from.x, from.y, from.z);
            float h0 = _query.GetPolyHeight(startRef, start, out float h).Succeeded() ? h : from.y;
            return new Vector3(clamped.x, h0, clamped.z);
        }

        return new Vector3(clamped.x, GroundHeight(clamped.x, from.y, clamped.z), clamped.z);
    }

    /// <summary>What <c>MapNavigator.RaycastWalkable</c> answered before #978.</summary>
    public Vector3 RaycastWalkable(Vector3 from, Vector3 to) => RaycastWalkable(from, to, out _);

    private Vector3 RaycastWalkable(Vector3 from, Vector3 to, out long startRef)
    {
        var start = new RcVec3f(from.x, from.y, from.z);
        DtStatus status = _query.FindNearestPoly(start, s_pickExtents, _filter, out startRef, out _, out _);
        if (status.Failed() || startRef == 0)
            return from;

        Span<long> path = stackalloc long[MaxPolys];
        DtStatus hit = _query.Raycast(startRef, start, new RcVec3f(to.x, to.y, to.z), _filter, out float t, out _,
            path, out _, MaxPolys);
        if (hit.Failed())
            return from;
        if (t >= float.MaxValue)
            return to;

        return new Vector3(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t, from.z + (to.z - from.z) * t);
    }

    /// <summary>The height <c>MapNavigator.SampleGroundHeight</c> answered before #978.</summary>
    public float GroundHeight(float x, float y, float z) =>
        FindGround(new Vector3(x, y, z), out Vector3 ground) == NavmeshGroundKind.Under ? ground.y : y;

    /// <summary>What <c>MapNavigator.FindGround</c> answered before #978.</summary>
    public NavmeshGroundKind FindGround(Vector3 near, out Vector3 ground)
    {
        ground = near;
        var center = new RcVec3f(near.x, near.y, near.z);
        DtStatus status = _query.FindNearestPoly(center, s_pickExtents, _filter, out long nearestRef,
            out RcVec3f nearest, out _);
        if (status.Failed() || nearestRef == 0)
            return NavmeshGroundKind.None;

        if (_query.GetPolyHeight(nearestRef, center, out float h).Succeeded())
        {
            ground = new Vector3(near.x, h, near.z);
            return NavmeshGroundKind.Under;
        }

        ground = new Vector3(nearest.X, nearest.Y, nearest.Z);
        return NavmeshGroundKind.Nearest;
    }

    /// <summary>What <c>MapNavigator.HasVisibility</c> answered before #978.</summary>
    public bool HasVisibility(Vector3 start, Vector3 end)
    {
        var startPos = new RcVec3f(start.x, start.y, start.z);
        var endPos = new RcVec3f(end.x, end.y, end.z);
        DtStatus status = _query.FindNearestPoly(startPos, s_pickExtents, _filter, out long startRef, out _, out _);
        if (!status.Succeeded() || startRef == 0)
            return false;

        status = _query.FindNearestPoly(endPos, s_pickExtents, _filter, out long endRef, out _, out _);
        if (!status.Succeeded() || endRef == 0)
            return false;

        Span<long> path = stackalloc long[MaxPolys];
        DtStatus hit = _query.Raycast(startRef, startPos, endPos, _filter, out float t, out _, path, out _, MaxPolys);
        return !hit.Failed() && t >= 1.0f;
    }

    /// <summary>The route <c>MapNavigator.FindPath</c> wrote before #978 (none on any failure).</summary>
    public void FindPath(Vector3 start, Vector3 end, List<Vector3> smoothPath)
    {
        smoothPath.Clear();
        try
        {
            FindPathCore(start, end, smoothPath);
        }
        catch (Exception)
        {
            smoothPath.Clear();
        }
    }

    private void FindPathCore(Vector3 start, Vector3 end, List<Vector3> smoothPath)
    {
        var startPos = new RcVec3f(start.x, start.y, start.z);
        var endPos = new RcVec3f(end.x, end.y, end.z);
        Span<long> path = _corridor;

        DtStatus status = _query.FindNearestPoly(startPos, s_pickExtents, _filter, out long startRef, out _, out _);
        if (!status.Succeeded())
            throw new InvalidOperationException("start search");
        if (startRef == 0)
            return;

        status = _query.FindNearestPoly(endPos, s_pickExtents, _filter, out long endRef, out _, out _);
        if (!status.Succeeded())
            throw new InvalidOperationException("end search");
        if (endRef == 0)
            return;

        status = _query.FindPath(startRef, endRef, startPos, endPos, _filter, path, out int pathCount, MaxPolys);
        if (!status.Succeeded())
            throw new InvalidOperationException("path");
        if (pathCount == 0)
            return;

        _query.ClosestPointOnPoly(startRef, startPos, out RcVec3f iterPos, out _);
        _query.ClosestPointOnPoly(path[pathCount - 1], endPos, out RcVec3f targetPos, out _);
        smoothPath.Add(new Vector3(iterPos.X, iterPos.Y, iterPos.Z));

        Span<long> visited = stackalloc long[16];
        while (0 < pathCount && smoothPath.Count < MaxSmooth)
        {
            if (!DtPathUtils.GetSteerTarget(_query, iterPos, targetPos, Slop, path, pathCount,
                    out RcVec3f steerPos, out int steerPosFlag, out _))
            {
                break;
            }

            bool endOfPath = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_END) != 0;
            bool offMeshConnection = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;
            var delta = RcVec3f.Subtract(steerPos, iterPos);
            float len = MathF.Sqrt(RcVec3f.Dot(delta, delta));
            len = (endOfPath || offMeshConnection) && len < StepSize ? 1 : StepSize / len;
            RcVec3f moveTgt = RcVec.Mad(iterPos, delta, len);

            _query.MoveAlongSurfaceReusing(path[0], iterPos, moveTgt, _filter, out RcVec3f result, visited,
                out int nvisited, 16);
            iterPos = result;
            pathCount = DtPathUtils.MergeCorridorStartMoved(path, pathCount, MaxPolys, visited, nvisited);
            pathCount = DtPathUtils.FixupShortcuts(path, pathCount, _query);
            if (_query.GetPolyHeight(path[0], result, out float h).Succeeded())
                iterPos.Y = h;

            if (endOfPath && DtPathUtils.InRange(iterPos, steerPos, Slop, 1.0f))
            {
                iterPos = targetPos;
                if (smoothPath.Count < MaxSmooth)
                    smoothPath.Add(new Vector3(iterPos.X, iterPos.Y, iterPos.Z));
                break;
            }

            if (offMeshConnection && DtPathUtils.InRange(iterPos, steerPos, Slop, 1.0f))
            {
                // The maps have no off-mesh connections; the navigator's branch is not reproduced.
                throw new NotSupportedException("off-mesh connection");
            }

            if (smoothPath.Count < MaxSmooth)
                smoothPath.Add(new Vector3(iterPos.X, iterPos.Y, iterPos.Z));
        }
    }

    /// <summary>
    /// Whether DotRecast's full search for <paramref name="center" /> settles on a polygon the point stands on (over
    /// it, within the tile's climb, or exactly on its edge): the case #978's narrow search answers on its own.
    /// </summary>
    public bool StandsOnPolygon(Vector3 center)
    {
        var c = new RcVec3f(center.x, center.y, center.z);
        _query.FindNearestPoly(c, s_pickExtents, _filter, out long nearestRef, out RcVec3f nearest, out bool over);
        if (nearestRef == 0)
            return false;

        _query.GetAttachedNavMesh().GetTileAndPolyByRefUnsafe(nearestRef, out DtMeshTile tile, out _);
        return over ? MathF.Abs(c.Y - nearest.Y) - tile.data.header.walkableClimb <= 0 : nearest == c;
    }
}
