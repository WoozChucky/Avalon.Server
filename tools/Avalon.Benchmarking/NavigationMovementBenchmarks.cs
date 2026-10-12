using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking;

/// <summary>
/// The old two-query movement path and the combined operation, on the committed town navmesh.
/// A batch visits different positions so an idle case cannot benefit from cross-call caching.
/// Bake, input selection and equivalence checks are setup, outside the measured operations.
/// </summary>
[MemoryDiagnoser]
public class NavigationMovementBenchmarks
{
    private const int Inputs = 256;
    private MapNavigator _navigator = null!;
    private readonly (Vector3 From, Vector3 To)[] _inputs = new (Vector3, Vector3)[Inputs];

    [Params("Idle", "Walking", "Blocked", "Mixed")]
    public string Scenario { get; set; } = "Idle";

    [GlobalSetup]
    public void Setup()
    {
        _navigator = new MapNavigator(NullLoggerFactory.Instance);
        _navigator.LoadFromNavMesh(TownNavmesh.Shared);
        var random = new Random(896);
        int count = 0;
        for (int attempt = 0; attempt < 50000 && count < Inputs; attempt++)
        {
            var point = new Vector3((float)random.NextDouble() * 60f, 0,
                (float)random.NextDouble() * 60f);
            if (_navigator.FindGround(point, out Vector3 from) != NavmeshGroundKind.Under)
                continue;

            var to = new Vector3(from.x + 4f / 60f, from.y, from.z);
            if (Scenario == "Blocked")
            {
                // First reach an actual wall, then press into it. Keep only fully stopped inputs.
                Vector3 wall = _navigator.RaycastWalkable(from, new Vector3(from.x + 60f, from.y, from.z));
                from = new Vector3(wall.x, _navigator.SampleGroundHeight(wall.x, from.y, wall.z), wall.z);
                to = new Vector3(from.x + 4f / 60f, from.y, from.z);
                Vector3 stopped = _navigator.RaycastWalkable(from, to);
                if (stopped.x != from.x || stopped.z != from.z)
                    continue;
            }
            else
            {
                Vector3 walked = _navigator.RaycastWalkable(from, to);
                if (walked.x != to.x || walked.z != to.z)
                    continue;
                if (Scenario == "Idle" || (Scenario == "Mixed" && count % 5 < 2))
                    to = from;
            }

            _inputs[count++] = (from, to);
        }

        if (count != Inputs)
            throw new InvalidOperationException($"Only {count} inputs found for {Scenario}");
        foreach ((Vector3 from, Vector3 to) in _inputs)
        {
            Vector3 old = Separate(_navigator, from, to);
            Vector3 combined = _navigator.MoveOnGround(from, to);
            if (!old.Equals(combined))
                throw new InvalidOperationException($"Movement differed for {Scenario}: {old} vs {combined}");
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Inputs)]
    public float SeparateQueries()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _inputs)
            result += Separate(_navigator, from, to).y;
        return result;
    }

    [Benchmark(OperationsPerInvoke = Inputs)]
    public float CombinedQuery()
    {
        float result = 0;
        foreach ((Vector3 from, Vector3 to) in _inputs)
            result += _navigator.MoveOnGround(from, to).y;
        return result;
    }

    private static Vector3 Separate(MapNavigator nav, Vector3 from, Vector3 to)
    {
        Vector3 clamped = nav.RaycastWalkable(from, to);
        return new Vector3(clamped.x, nav.SampleGroundHeight(clamped.x, from.y, clamped.z), clamped.z);
    }
}
