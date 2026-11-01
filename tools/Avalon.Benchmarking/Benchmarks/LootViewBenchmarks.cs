using System.Collections;
using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One client's loot view decision (#1269, <c>MapInstance.DecideLootView</c>, bound by name), steady: the view already
/// holds every drop in reach, so each call finds nothing entered and nothing left, as on almost every broadcast tick. The
/// instance's layout is <see cref="InterestVisibilityBenchmarks" />'s, 30 m cells from 0 to 1,020 m on X and Z, with the
/// watcher at its centre. <see cref="Layout" /> "near" lays the drops on a disc 45 m round the watcher, all in view (a
/// cleared fight's ground); "spread" scatters them over the whole layout, seeded, so most lie in cells the decision never
/// visits. <see cref="Drops" /> 0 is the early return of an instance with nothing on the ground.
/// </summary>
[MemoryDiagnoser]
public class LootViewBenchmarks
{
    private static readonly Vector3 s_centre = new(495f, 0f, 495f);

    [Params(0, 20, 500)]
    public int Drops { get; set; }

    [Params("near", "spread")]
    public string Layout { get; set; } = "near";

    private ScenarioWorld _world = null!;
    private CharacterEntity _watcher = null!;
    private Action<CharacterEntity> _decideLootView = null!;
    private Func<int> _held = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Reference data: a view is decided only in an instance that has some to describe its drops by.
        _world = ScenarioWorld.CreateWithReferenceData();
        PlacedChunk first = new(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        PlacedChunk last = new(new ChunkTemplateId(1), 33, 33, 0, new Vector3(990f, 0f, 990f));
        MapInstance map = _world.AddInstance(layout: new ChunkLayout(0, [first, last], first, null, [], Vector3.zero, 30f));

        var random = new Random(1_269);
        for (int i = 0; i < Drops; i++)
        {
            Vector3 position;
            if (Layout == "near")
            {
                // A sunflower disc: even spacing out to 45 m.
                float radius = 45f * MathF.Sqrt((i + 0.5f) / Drops);
                float angle = i * 2.39996323f;
                position = new Vector3(s_centre.x + radius * MathF.Cos(angle), 0f, s_centre.z + radius * MathF.Sin(angle));
            }
            else
            {
                position = new Vector3((float)(random.NextDouble() * 1020), 0f, (float)(random.NextDouble() * 1020));
            }

            map.Drops.Add(new GroundLoot
            {
                Guid = new ObjectGuid(ObjectType.Loot, (uint)(1 + i)),
                Position = position,
                Gold = 1,
                FreeForAllAt = DateTime.UnixEpoch,
            });
        }

        _watcher = _world.NewCharacter(1);
        _world.Join(map, _watcher, s_centre);

        // The instance's own decision, private, so bound by name; and the view it decides, World-side, read the same way.
        _decideLootView = typeof(MapInstance).GetMethod("DecideLootView", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<CharacterEntity>>(map);
        object view = typeof(CharacterCharacterGameState).GetProperty("Loot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_watcher.CharacterGameState)!;
        _held = view.GetType().GetProperty("Count")!.GetMethod!.CreateDelegate<Func<int>>(view);

        Decide();
        Decide();
        var entered = (ICollection)view.GetType().GetProperty("Entered")!.GetValue(view)!;
        var left = (ICollection)view.GetType().GetProperty("Left")!.GetValue(view)!;
        if (entered.Count != 0 || left.Count != 0)
            throw new InvalidOperationException("Expected a steady loot view after two decisions.");
        if (Layout == "near" && _held() != Drops)
            throw new InvalidOperationException($"The view holds {_held()} of the {Drops} drops round the watcher.");
    }

    [Benchmark]
    public int Decide()
    {
        _decideLootView(_watcher);
        return _held();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
