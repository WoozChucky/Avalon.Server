using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Avalon.World.Testing.Navigation;

/// <summary>
/// A player's grounded step as <c>MapNavigator.MoveOnGround</c> took it before #978, written against DotRecast's own
/// <see cref="DtNavMeshQuery" /> and nothing of <c>MapNavigator</c>: a full nearest-polygon search at the start for the
/// walkable raycast, and, when the step moved, a second full search at the end (at the start's height) for the ground
/// snap. It is the reference the navigator's movement is compared against, in tests and in
/// <c>NavigationMovementBenchmarks</c>; it never changes with the navigator.
/// </summary>
public sealed class DetourMovementReference
{
    private const int MaxPolys = 256;
    private static readonly RcVec3f s_pickExtents = new(2, 4, 2);

    private readonly DtNavMeshQuery _query;
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();

    public DetourMovementReference(DtNavMesh navMesh)
    {
        _query = new DtNavMeshQuery(navMesh);
    }

    /// <summary>The step from <paramref name="from" /> towards <paramref name="to" />, on the ground.</summary>
    public Vector3 MoveOnGround(Vector3 from, Vector3 to)
    {
        var start = new RcVec3f(from.x, from.y, from.z);
        DtStatus status = _query.FindNearestPoly(start, s_pickExtents, _filter, out long startRef, out _, out _);
        if (status.Failed() || startRef == 0)
            return from;

        Vector3 clamped;
        Span<long> path = stackalloc long[MaxPolys];
        DtStatus hit = _query.Raycast(startRef, start, new RcVec3f(to.x, to.y, to.z), _filter, out float t, out _,
            path, out _, MaxPolys);
        if (hit.Failed())
            clamped = from;
        else if (t >= float.MaxValue)
            clamped = to;
        else
            clamped = new Vector3(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t, from.z + (to.z - from.z) * t);

        if (clamped.x == from.x && clamped.z == from.z)
        {
            float h0 = _query.GetPolyHeight(startRef, start, out float h).Succeeded() ? h : from.y;
            return new Vector3(clamped.x, h0, clamped.z);
        }

        return new Vector3(clamped.x, GroundHeight(clamped.x, from.y, clamped.z), clamped.z);
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
