using Avalon.Common.Mathematics;
using Avalon.World.Public.Maps;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps.Navigation;

public class MapNavigator : IMapNavigator
{
    private readonly ILogger<MapNavigator> _logger;
    private DtNavMesh? _navMesh;
    private DtFindPathOption _findPathOption;
    private IDtQueryFilter _queryFilter;

    private const bool EnableRaycast = true;
    private const float StepSize = 0.5f;
    private const float Slop = 0.01f;
    private const int MaxSmooth = 2048;
    private const int MaxPolys = 256;

    private static readonly RcVec3f PolyPickExt = new(2, 4, 2);

    public object? Mesh => _navMesh;

    public MapNavigator(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MapNavigator>();
    }

    public async Task LoadAsync(string meshFilename)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Maps", meshFilename);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);
        var reader = new DtMeshSetReader();
        _navMesh = reader.Read(br, 6);
        _findPathOption = new DtFindPathOption(EnableRaycast ? DtFindPathOptions.DT_FINDPATH_ANY_ANGLE : 0, float.MaxValue);
        _queryFilter = new DtQueryDefaultFilter();
    }

    public void LoadFromNavMesh(DtNavMesh navMesh)
    {
        _navMesh = navMesh;
        _findPathOption = new DtFindPathOption(EnableRaycast ? DtFindPathOptions.DT_FINDPATH_ANY_ANGLE : 0, float.MaxValue);
        _queryFilter = new DtQueryDefaultFilter();
    }

    public List<Vector3> FindPath(Vector3 start, Vector3 end)
    {
        try
        {
            if (_navMesh == null)
            {
                throw new Exception("NavMesh is not loaded");
            }

            var query = new DtNavMeshQuery(_navMesh);

            var startPos = new RcVec3f(-start.x, start.y, start.z); // new RcVec3f(-23.1f, 100, 40.5f);
            var endPos = new RcVec3f(-end.x, end.y, end.z); // RcVec3f(-5.6f, 100, 31f);
            var path = new List<long>();

            var status = query.FindNearestPoly(startPos, PolyPickExt, _queryFilter, out var startRef, out _, out _);
            CheckStatus(status);
            if (startRef == 0)
            {
                _logger.LogWarning("Failed to find start polygon");
                return [];
            }

            status = query.FindNearestPoly(endPos, PolyPickExt, _queryFilter, out var endRef, out _, out _);
            CheckStatus(status);
            if (endRef == 0)
            {
                _logger.LogWarning("Failed to find end polygon");
                return [];
            }

            status = query.FindPath(startRef, endRef, startPos, endPos, _queryFilter, ref path, _findPathOption);
            CheckStatus(status);
            if (path.Count == 0)
            {
                _logger.LogWarning("Failed to find path");
                return [];
            }

            var pathCount = path.Count;

            query.ClosestPointOnPoly(startRef, startPos, out var iterPos, out _);
            query.ClosestPointOnPoly(path[pathCount - 1], endPos, out var targetPos, out _);

            var smoothPath = new List<RcVec3f>();
            smoothPath.Add(iterPos);

            Span<long> visited = stackalloc long[16];
            var nvisited = 0;

            while (0 < pathCount && smoothPath.Count < MaxSmooth)
            {
                // Find location to steer towards.
                if (!DtPathUtils.GetSteerTarget(query, iterPos, targetPos, Slop,
                        path, pathCount, out var steerPos, out var steerPosFlag, out var steerPosRef))
                {
                    break;
                }

                var endOfPath = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_END) != 0;
                var offMeshConnection = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;

                // Find movement delta.
                var delta = RcVec3f.Subtract(steerPos, iterPos);
                var len = MathF.Sqrt(RcVec3f.Dot(delta, delta));
                // If the steer target is end of path or off-mesh link, do not move past the location.
                if ((endOfPath || offMeshConnection) && len < StepSize)
                {
                    len = 1;
                }
                else
                {
                    len = StepSize / len;
                }

                var moveTgt = RcVec.Mad(iterPos, delta, len);

                // Move
                query.MoveAlongSurface(path[0], iterPos, moveTgt, _queryFilter, out var result, visited, out nvisited, 16);

                iterPos = result;

                pathCount = DtPathUtils.MergeCorridorStartMoved(ref path, pathCount, MaxPolys, visited, nvisited);
                pathCount = DtPathUtils.FixupShortcuts(ref path, pathCount, query);

                status = query.GetPolyHeight(path[0], result, out var h);
                if (status.Succeeded())
                {
                    iterPos.Y = h;
                }

                // Handle end of path and off-mesh links when close enough.
                if (endOfPath && DtPathUtils.InRange(iterPos, steerPos, Slop, 1.0f))
                {
                    // Reached end of path.
                    iterPos = targetPos;
                    if (smoothPath.Count < MaxSmooth)
                    {
                        smoothPath.Add(iterPos);
                    }

                    break;
                }

                if (offMeshConnection && DtPathUtils.InRange(iterPos, steerPos, Slop, 1.0f))
                {
                    // Reached off-mesh connection.
                    startPos = RcVec3f.Zero;
                    endPos = RcVec3f.Zero;

                    // Advance the path up to and over the off-mesh connection.
                    long prevRef = 0;
                    var polyRef = path[0];
                    var npos = 0;
                    while (npos < pathCount && polyRef != steerPosRef)
                    {
                        prevRef = polyRef;
                        polyRef = path[npos];
                        npos++;
                    }

                    path = path.GetRange(npos, path.Count - npos);
                    pathCount -= npos;

                    // Handle the connection.
                    var status2 = _navMesh.GetOffMeshConnectionPolyEndPoints(prevRef, polyRef, ref startPos, ref endPos);
                    if (status2.Succeeded())
                    {
                        if (smoothPath.Count < MaxSmooth)
                        {
                            smoothPath.Add(startPos);
                            // Hack to make the dotted path not visible during off-mesh connection.
                            if ((smoothPath.Count & 1) != 0)
                            {
                                smoothPath.Add(startPos);
                            }
                        }

                        // Move position at the other side of the off-mesh link.
                        iterPos = endPos;
                        query.GetPolyHeight(path[0], iterPos, out var eh);
                        iterPos.Y = eh;
                    }
                }

                // Store results.
                if (smoothPath.Count < MaxSmooth)
                {
                    smoothPath.Add(iterPos);
                }
            }

            return smoothPath.Select(v => new Vector3(-v.X, v.Y, v.Z)).ToList();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to find path");
            return [];
        }
    }

    public bool HasVisibility(Vector3 start, Vector3 end)
    {
        try
        {
            if (_navMesh == null)
            {
                throw new Exception("NavMesh is not loaded");
            }

            var query = new DtNavMeshQuery(_navMesh);

            var startPos = new RcVec3f(-start.x, start.y, start.z);
            var endPos = new RcVec3f(-end.x, end.y, end.z);

            // Find the nearest polygons to the start and end points
            var status = query.FindNearestPoly(startPos, PolyPickExt, _queryFilter, out var startRef, out _, out _);
            CheckStatus(status);
            if (startRef == 0)
            {
                _logger.LogWarning("Failed to find start polygon");
                return false;
            }

            status = query.FindNearestPoly(endPos, PolyPickExt, _queryFilter, out var endRef, out _, out _);
            CheckStatus(status);
            if (endRef == 0)
            {
                _logger.LogWarning("Failed to find end polygon");
                return false;
            }

            List<long> path = [];

            // Perform the raycast
            var hitResult = query.Raycast(startRef, startPos, endPos, _queryFilter, out var hit, out _, ref path);

            if (hitResult.Failed() || hit < 1.0f)
            {
                // There is an obstacle between start and end
                return false;
            }

            // No obstacles found, point A has visibility to point B
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Raycast failed");
            return false;
        }
    }

    public Vector3 RaycastWalkable(Vector3 from, Vector3 to)
    {
        if (_navMesh == null) return to;

        var query = new DtNavMeshQuery(_navMesh);

        var startVec = new RcVec3f(from.x, from.y, from.z);
        var status = query.FindNearestPoly(startVec, PolyPickExt, _queryFilter, out var startRef, out _, out _);
        if (status.Failed() || startRef == 0)
        {
            return from;
        }

        var endVec = new RcVec3f(to.x, to.y, to.z);
        List<long> path = [];
        var hitStatus = query.Raycast(startRef, startVec, endVec, _queryFilter, out var t, out _, ref path);
        if (hitStatus.Failed())
        {
            return from;
        }

        if (t >= 1.0f)
        {
            return to;
        }

        var clampedX = from.x + (to.x - from.x) * t;
        var clampedY = from.y + (to.y - from.y) * t;
        var clampedZ = from.z + (to.z - from.z) * t;
        return new Vector3(clampedX, clampedY, clampedZ);
    }

    public float SampleGroundHeight(float x, float z)
    {
        if (_navMesh == null) return 0f;

        var query = new DtNavMeshQuery(_navMesh);
        var center = new RcVec3f(x, 0f, z);
        var status = query.FindNearestPoly(center, PolyPickExt, _queryFilter, out var nearestRef, out _, out _);
        if (status.Failed() || nearestRef == 0)
        {
            return 0f;
        }

        var pos = new RcVec3f(x, 0f, z);
        if (query.GetPolyHeight(nearestRef, pos, out var h).Succeeded())
        {
            return h;
        }

        return 0f;
    }

    private static void CheckStatus(DtStatus status)
    {
        if (!status.Succeeded())
        {
            if (status.InProgress())
            {
                throw new Exception("In progress");
            }

            if (status.Failed())
            {
                throw new Exception("Failed");
            }

            if (status.IsEmpty())
            {
                throw new Exception("Empty");
            }

            if (status.IsPartial())
            {
                throw new Exception("Partial");
            }
        }
    }
}
