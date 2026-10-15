using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Real instance ticks of one population, laid out three ways: crowded into one town, spread across 20 towns, or in
/// forests of three (#981); includes inline sends.
/// </summary>
/// <remarks>
/// <para>
/// The towns hold players only, every one in view of every other in its town and moving every tick, so visibility,
/// tracking and the state broadcast grow with players per town squared.
/// </para>
/// <para>
/// A forest is one instance as production builds it (<see cref="ScenarioWorld.NewForestInstance" />: seed 1's layout,
/// its spawn table, about 200 creatures with their real AI), with three players standing at its entry, too tough to
/// die, as <c>ForestTickBenchmarks</c> has one: each player checks every creature for visibility and sees the few near
/// the entry, some of which fight it. Its tick is mostly the creatures', the visibility scan and the fights near the
/// entry.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TickPhaseBenchmarks
{
    private const string OneTown = "town-1";
    private const string TwentyTowns = "town-20";
    private const string Forests = "forest-3";
    private const int ForestParty = 3;

    private ScenarioWorld _world = null!;
    private CharacterEntity[] _characters = null!;
    private int _step;

    [Params(300, 600)]
    public int Players { get; set; }

    [Params(OneTown, TwentyTowns, Forests)]
    public string Layout { get; set; } = OneTown;

    [GlobalSetup]
    public void Setup()
    {
        _characters = new CharacterEntity[Players];
        if (Layout == Forests)
            SetUpForests();
        else
            SetUpTowns(Layout == OneTown ? 1 : 20);

        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(15))
            TickBatch();
    }

    private void SetUpTowns(int towns)
    {
        _world = new ScenarioWorld();
        var maps = new MapInstance[towns];
        for (int i = 0; i < maps.Length; i++)
            maps[i] = _world.AddInstance();
        for (int i = 0; i < Players; i++)
        {
            CharacterEntity character = _world.NewCharacter((uint)(i + 1));
            _characters[i] = character;
            _world.Join(maps[i % maps.Length], character, new Vector3(i % 4, 0, i % 3));
        }
    }

    private void SetUpForests()
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance forest = null!;
        for (int i = 0; i < Players; i++)
        {
            if (i % ForestParty == 0)
                forest = _world.NewForestInstance(1);

            // Tough enough never to die: the creatures that reach them fight for the whole run.
            CharacterEntity character = _world.NewCharacter((uint)(i + 1));
            _characters[i] = character;
            _world.Join(forest, character, forest.EntrySpawnWorldPos);
        }
    }

    // Each instance broadcasts every six ticks (#975: the 100 ms interval with its remainder carried; ScenarioWorld.Dt is
    // 4 TimeSpan ticks under 1/60 s, so a seventh is needed once in about 40,000 intervals), each in its own slot.
    // Seven complete intervals keep broadcast work identical in every measurement.
    [Benchmark(OperationsPerInvoke = 42)]
    public void TickBatch()
    {
        for (int i = 0; i < 42; i++)
            Tick();
    }

    private void Tick()
    {
        // The towns' players move every tick; the forests' stand at the entry and fight what comes.
        if (Layout != Forests)
        {
            _step ^= 1;
            for (int i = 0; i < _characters.Length; i++)
                _characters[i].Position = new Vector3(i % 4 + _step * 0.1f, 0, i % 3);
        }

        _world.Tick();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
