using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>Replication under repeated motion assignments: idle, mixed, and all moving, with inline sends.</summary>
[MemoryDiagnoser]
public class CharacterDeltaBenchmarks
{
    private ScenarioWorld _world = null!;
    private CharacterEntity[] _characters = null!;
    private int _step;

    [Params(0, 60, 100)]
    public int MovingPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new ScenarioWorld();
        _characters = new CharacterEntity[600];
        var maps = new MapInstance[20];
        for (int i = 0; i < maps.Length; i++)
            maps[i] = _world.AddInstance();
        for (int i = 0; i < _characters.Length; i++)
        {
            CharacterEntity character = _world.NewCharacter((uint)(i + 1));
            _characters[i] = character;
            _world.Join(maps[i % maps.Length], character, new Vector3(i % 4, 0, i % 3));
        }
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(15))
            TickBatch();

        long bytes = _world.Connections.Sum(c => c.BytesWritten);
        int packets = _world.Connections.Sum(c => c.Sent);
        TickBatch();
        Console.WriteLine($"42-tick wire sample: {_world.Connections.Sum(c => c.BytesWritten) - bytes} bytes, {_world.Connections.Sum(c => c.Sent) - packets} packets");
    }

    // Six whole broadcast cycles. Direct assignments isolate replication, excluding input ACKs/navmesh queries.
    [Benchmark(OperationsPerInvoke = 42)]
    public void TickBatch()
    {
        for (int tick = 0; tick < 42; tick++)
        {
            _step ^= 1;
            for (int i = 0; i < _characters.Length; i++)
            {
                // Interleave within each instance as well as across instances.
                bool moving = i / 20 % 10 < MovingPercent / 10;
                _characters[i].Position = new Vector3(i % 4 + (moving ? _step * 0.1f : 0f), 0, i % 3);
                _characters[i].Velocity = moving ? new Vector3(_step == 1 ? 6f : -6f, 0, 0) : Vector3.zero;
                _characters[i].Orientation = Vector3.zero;
            }
            _world.Tick();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
