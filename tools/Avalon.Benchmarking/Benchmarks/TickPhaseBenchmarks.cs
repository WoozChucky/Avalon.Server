using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>Real instance ticks with equal populations crowded together or spread across towns; includes inline sends.</summary>
[MemoryDiagnoser]
public class TickPhaseBenchmarks
{
    private ScenarioWorld _world = null!;
    private CharacterEntity[] _characters = null!;
    private int _step;

    [Params(300, 600)]
    public int Players { get; set; }

    [Params(1, 20)]
    public int Instances { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new ScenarioWorld();
        _characters = new CharacterEntity[Players];
        var maps = new MapInstance[Instances];
        for (int i = 0; i < maps.Length; i++)
            maps[i] = _world.AddInstance();
        for (int i = 0; i < Players; i++)
        {
            CharacterEntity character = _world.NewCharacter((uint)(i + 1));
            _characters[i] = character;
            _world.Join(maps[i % maps.Length], character, new Vector3(i % 4, 0, i % 3));
        }
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(15))
            TickBatch();
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
        _step ^= 1;
        for (int i = 0; i < _characters.Length; i++)
            _characters[i].Position = new Vector3(i % 4 + _step * 0.1f, 0, i % 3);
        _world.Tick();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
