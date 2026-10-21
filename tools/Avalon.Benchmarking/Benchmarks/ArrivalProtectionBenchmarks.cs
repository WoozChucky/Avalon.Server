using Avalon.Common;
using Avalon.World.Auras;
using Avalon.World.Entities;
using BenchmarkDotNet.Attributes;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>The arrival eligibility check on combat/AI paths, with and without the buff (#1088).</summary>
[MemoryDiagnoser]
public class ArrivalProtectionBenchmarks
{
    private ScenarioWorld _world = null!;
    private CharacterEntity _ordinary = null!;
    private CharacterEntity _protected = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ordinary = Character(1);
        _protected = Character(2);
        _world = ScenarioWorld.CreateWithReferenceData();
        ArrivalProtection.Start(_protected, _world.AddInstance().Auras);
        ActiveAura buff = _protected.Auras.All[0];
        // Keep the measured branch active even if a benchmark job runs longer than the gameplay duration.
        // The production clock lookup remains part of each check.
        buff.Schedule = buff.Schedule with { ExpiresAt = DateTimeOffset.MaxValue };
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Baseline = true)]
    public bool WithoutBuff() => ArrivalProtection.IsProtected(_ordinary);

    [Benchmark]
    public bool WithBuff() => ArrivalProtection.IsProtected(_protected);

    private static CharacterEntity Character(uint id) =>
        new() { Guid = new ObjectGuid(ObjectType.Character, id), Health = 100, CurrentHealth = 100 };
}
