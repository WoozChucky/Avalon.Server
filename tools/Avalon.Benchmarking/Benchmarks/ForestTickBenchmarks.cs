using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Whole world ticks of one forest instance as production builds it (<see cref="ScenarioWorld.NewForestInstance" />:
/// seed 1's layout, its spawn table and the real AI), with three players standing at its entry, as most of a forest's
/// tick is: a few creatures fighting whoever is near, the rest waiting for their next search (#979).
/// </summary>
[MemoryDiagnoser]
public class ForestTickBenchmarks
{
    private const int Ticks = 60;
    private ScenarioWorld _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance forest = _world.NewForestInstance(1);
        // Tough enough never to die: the creatures that reach them fight for the whole run.
        for (uint i = 1; i <= 3; i++)
            _world.Join(forest, _world.NewCharacter(100 + i), forest.EntrySpawnWorldPos);

        // The fights near the entry begin, and the JIT settles, outside measurement.
        var warmup = System.Diagnostics.Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(15)) Tick();
    }

    // Per world tick: the forest's creatures, locomotion, combat, visibility and broadcast, and the send pass.
    [Benchmark(OperationsPerInvoke = Ticks)]
    public void Tick()
    {
        for (int tick = 0; tick < Ticks; tick++)
            _world.Tick();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
