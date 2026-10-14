using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking;

/// <summary>
/// The navigator's other nearest-polygon users (#978) against the same queries with DotRecast's own full searches
/// (<see cref="DetourNavigationReference" />), on the committed town navmesh and on a generated forest's. Path: a
/// creature's route to a point up to 20 m away (<see cref="MapNavigator.FindPath(Vector3, Vector3, List{Vector3})" />
/// into a reused list). Visibility: a creature's line of sight to a point up to 20 m away
/// (<see cref="MapNavigator.HasVisibility" />). Projectile: one tick's step of a projectile at 20 m/s, flying flat at
/// its caster's height (<see cref="MapNavigator.RaycastWalkable" />). A batch visits 256 different queries; the input
/// selection and the check that both give the same answer for every query are setup.
/// </summary>
[MemoryDiagnoser]
public class NavigationQueryBenchmarks
{
    private const int Queries = 256;
    private const int ForestSeed = 7;
    private MapNavigator _navigator = null!;
    private DetourNavigationReference _reference = null!;
    private readonly (Vector3 From, Vector3 To)[] _queries = new (Vector3, Vector3)[Queries];
    private readonly List<Vector3> _path = new(2048);
    private readonly List<Vector3> _referencePath = new(2048);

    [Params("Town", "Forest")]
    public string Map { get; set; } = "Town";

    [Params("Path", "Visibility", "Projectile")]
    public string Query { get; set; } = "Path";

    [GlobalSetup]
    public void Setup()
    {
        DtNavMesh mesh = Map == "Town" ? TownNavmesh.Shared : ForestLayouts.For(ForestSeed).NavMesh;
        _navigator = new MapNavigator(NullLoggerFactory.Instance);
        _navigator.LoadFromNavMesh(mesh);
        _reference = new DetourNavigationReference(mesh);
        (Vector3 min, Vector3 max) = Bounds(mesh);

        var random = new Random(978);
        int count = 0;
        while (count < Queries)
        {
            if (!TryGround(random, min, max, out Vector3 from))
                continue;

            float angle = (float)(random.NextDouble() * Math.Tau);
            var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            if (Query == "Projectile")
            {
                // A flight from the caster's feet, flat at their height, up to 20 m: each tick's step is a query.
                Vector3 at = from;
                for (int tick = 0; tick < 60 && count < Queries; tick++)
                {
                    Vector3 wanted = at + direction * (20f / 60f);
                    _queries[count++] = (at, wanted);
                    Vector3 end = _reference.RaycastWalkable(at, wanted);
                    if (end.x != wanted.x || end.z != wanted.z)
                        break;
                    at = end;
                }

                continue;
            }

            Vector3 near = from + direction * (float)(random.NextDouble() * 20);
            if (_navigator.FindGround(new Vector3(near.x, from.y, near.z), out Vector3 to) != NavmeshGroundKind.Under)
                continue;
            _queries[count++] = (from, to);
        }

        int standing = 0, answered = 0;
        foreach ((Vector3 from, Vector3 to) in _queries)
        {
            if (_reference.StandsOnPolygon(from))
                standing++;
            switch (Query)
            {
                case "Path":
                    _reference.FindPath(from, to, _referencePath);
                    _navigator.FindPath(from, to, _path);
                    if (!_path.SequenceEqual(_referencePath))
                        throw new InvalidOperationException($"Route differed for {Map} from {from} to {to}");
                    answered += _path.Count > 0 ? 1 : 0;
                    break;
                case "Visibility":
                    bool seen = _reference.HasVisibility(from, to);
                    if (seen != _navigator.HasVisibility(from, to))
                        throw new InvalidOperationException($"Sight differed for {Map} from {from} to {to}");
                    answered += seen ? 1 : 0;
                    break;
                default:
                    Vector3 expected = _reference.RaycastWalkable(from, to);
                    if (!expected.Equals(_navigator.RaycastWalkable(from, to)))
                        throw new InvalidOperationException($"Step differed for {Map} from {from} to {to}");
                    answered += expected.x == to.x && expected.z == to.z ? 1 : 0;
                    break;
            }
        }

        Console.WriteLine($"// {Map} {Query}: start on a polygon {standing}/{Queries}, routed, seen or unblocked {answered}");
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Queries)]
    public float DetourReference()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _queries)
        {
            switch (Query)
            {
                case "Path":
                    _reference.FindPath(from, to, _referencePath);
                    result += _referencePath.Count;
                    break;
                case "Visibility":
                    result += _reference.HasVisibility(from, to) ? 1 : 0;
                    break;
                default:
                    result += _reference.RaycastWalkable(from, to).x;
                    break;
            }
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Queries)]
    public float Navigator()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _queries)
        {
            switch (Query)
            {
                case "Path":
                    _navigator.FindPath(from, to, _path);
                    result += _path.Count;
                    break;
                case "Visibility":
                    result += _navigator.HasVisibility(from, to) ? 1 : 0;
                    break;
                default:
                    result += _navigator.RaycastWalkable(from, to).x;
                    break;
            }
        }

        return result;
    }

    /// <summary>A random point on the navmesh's ground, searched down from above the mesh in four-metre steps.</summary>
    private bool TryGround(Random random, Vector3 min, Vector3 max, out Vector3 ground)
    {
        float x = min.x + (float)random.NextDouble() * (max.x - min.x);
        float z = min.z + (float)random.NextDouble() * (max.z - min.z);
        for (float y = max.y; y >= min.y - 4f; y -= 4f)
        {
            if (_navigator.FindGround(new Vector3(x, y, z), out ground) == NavmeshGroundKind.Under)
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
