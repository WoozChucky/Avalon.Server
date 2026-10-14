using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking;

/// <summary>
/// A player's grounded step (<see cref="MapNavigator.MoveOnGround" />) against the same step taken with DotRecast's own
/// full nearest-polygon searches (<see cref="DetourNavigationReference" />, the navigator before #978), on the committed
/// town navmesh and on a generated forest's. A batch visits different positions, so nothing carries from one input to
/// the next. Bake, input selection and the equality check (every input, both ways) are setup, outside the measured
/// operations.
/// </summary>
[MemoryDiagnoser]
public class NavigationMovementBenchmarks
{
    private const int Inputs = 256;
    private const int ForestSeed = 7;
    private const float Step = 4f / 60f;
    private MapNavigator _navigator = null!;
    private DetourNavigationReference _reference = null!;
    private readonly (Vector3 From, Vector3 To)[] _inputs = new (Vector3, Vector3)[Inputs];

    [Params("Town", "Forest")]
    public string Map { get; set; } = "Town";

    /// <summary>
    /// Idle: no horizontal step. Walking: an unobstructed step. Blocked: pressing into a real wall from the point the
    /// raycast stopped at. Mixed: two idle inputs to three walking. Beside: a step from just beside the navmesh, where
    /// no polygon is under the player (the search's slow path).
    /// </summary>
    [Params("Idle", "Walking", "Blocked", "Mixed", "Beside")]
    public string Scenario { get; set; } = "Idle";

    [GlobalSetup]
    public void Setup()
    {
        DtNavMesh mesh = Map == "Town" ? TownNavmesh.Shared : ForestLayouts.For(ForestSeed).NavMesh;
        _navigator = new MapNavigator(NullLoggerFactory.Instance);
        _navigator.LoadFromNavMesh(mesh);
        _reference = new DetourNavigationReference(mesh);
        (Vector3 min, Vector3 max) = Bounds(mesh);

        var random = new Random(896);
        int count = 0;
        for (int attempt = 0; attempt < 200000 && count < Inputs; attempt++)
        {
            var point = new Vector3(min.x + (float)random.NextDouble() * (max.x - min.x), max.y,
                min.z + (float)random.NextDouble() * (max.z - min.z));
            if (Scenario == "Beside")
            {
                point = new Vector3(point.x, (float)random.NextDouble() * (max.y - min.y) + min.y, point.z);
                if (_navigator.FindGround(point, out Vector3 beside) != NavmeshGroundKind.Nearest)
                    continue;

                // Half a metre off the mesh's edge, level with it: no polygon under the player.
                var off = new Vector3(point.x - beside.x, 0, point.z - beside.z);
                float length = MathF.Sqrt(off.x * off.x + off.z * off.z);
                if (length < 1e-3f)
                    continue;
                var from = new Vector3(beside.x + off.x / length * 0.5f, beside.y, beside.z + off.z / length * 0.5f);
                if (_navigator.FindGround(from, out _) != NavmeshGroundKind.Nearest)
                    continue;
                _inputs[count++] = (from, new Vector3(from.x - off.x / length * Step, from.y, from.z - off.z / length * Step));
                continue;
            }

            if (!TryDropOnGround(point, min.y, out Vector3 ground))
                continue;

            var to = new Vector3(ground.x + Step, ground.y, ground.z);
            if (Scenario == "Blocked")
            {
                // First reach an actual wall, then press into it. Keep only fully stopped inputs.
                Vector3 wall = _reference.MoveOnGround(ground, new Vector3(ground.x + 60f, ground.y, ground.z));
                to = new Vector3(wall.x + Step, wall.y, wall.z);
                Vector3 stopped = _reference.MoveOnGround(wall, to);
                if (stopped.x != wall.x || stopped.z != wall.z)
                    continue;
                ground = wall;
            }
            else
            {
                Vector3 walked = _reference.MoveOnGround(ground, to);
                if (walked.x != to.x || walked.z != to.z)
                    continue;
                if (Scenario == "Idle" || (Scenario == "Mixed" && count % 5 < 2))
                    to = ground;
            }

            _inputs[count++] = (ground, to);
        }

        if (count != Inputs)
            throw new InvalidOperationException($"Only {count} inputs found for {Map} {Scenario}");

        int standing = 0, landing = 0, moved = 0;
        foreach ((Vector3 from, Vector3 to) in _inputs)
        {
            Vector3 expected = _reference.MoveOnGround(from, to);
            Vector3 actual = _navigator.MoveOnGround(from, to);
            if (!expected.Equals(actual))
                throw new InvalidOperationException($"Movement differed for {Map} {Scenario}: {expected} vs {actual}");

            if (_reference.StandsOnPolygon(from))
                standing++;
            if (actual.x == from.x && actual.z == from.z)
                continue;
            moved++;
            if (_reference.StandsOnPolygon(new Vector3(actual.x, from.y, actual.z)))
                landing++;
        }

        Console.WriteLine($"// {Map} {Scenario}: start on a polygon {standing}/{Inputs}, " +
                          $"end on a polygon {landing}/{moved} of the inputs that moved");
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Inputs)]
    public float DetourReference()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _inputs)
            result += _reference.MoveOnGround(from, to).y;
        return result;
    }

    [Benchmark(OperationsPerInvoke = Inputs)]
    public float MoveOnGround()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _inputs)
            result += _navigator.MoveOnGround(from, to).y;
        return result;
    }

    /// <summary>The ground under a point, searched down from above the mesh in four-metre steps, as a spawn would be.</summary>
    private bool TryDropOnGround(Vector3 above, float bottom, out Vector3 ground)
    {
        for (float y = above.y; y >= bottom - 4f; y -= 4f)
        {
            if (_navigator.FindGround(new Vector3(above.x, y, above.z), out ground) == NavmeshGroundKind.Under)
                return true;
        }

        ground = default;
        return false;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(DtNavMesh mesh)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            DtMeshHeader? header = mesh.GetTile(i)?.data?.header;
            if (header is null)
                continue;
            min = new Vector3(MathF.Min(min.x, header.bmin.X), MathF.Min(min.y, header.bmin.Y), MathF.Min(min.z, header.bmin.Z));
            max = new Vector3(MathF.Max(max.x, header.bmax.X), MathF.Max(max.y, header.bmax.Y), MathF.Max(max.z, header.bmax.Z));
        }

        return (min, max);
    }
}
