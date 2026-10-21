using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The crowd-control gates player input, casts and creature AI ask every tick (#770): <see cref="ControlGates.CanMove" />
/// and <see cref="ControlGates.CanCast" /> on a character holding no aura, one holding an aura that controls nothing
/// (Renew), and one stunned (Stunned, aura 12). Each is one read of the state the unit's auras cache, never a look at
/// its auras: 0 B expected on every row.
/// </summary>
[MemoryDiagnoser]
public class CrowdControlBenchmarks
{
    private static readonly AuraId s_renew = new(4);
    private static readonly AuraId s_stunned = new(12);

    private ScenarioWorld _world = null!;
    private CharacterEntity _free = null!;
    private CharacterEntity _renewed = null!;
    private CharacterEntity _stunned = null!;

    [GlobalSetup]
    public void Setup()
    {
        _free = Character(1);
        _renewed = Character(2);
        _stunned = Character(3);
        _world = ScenarioWorld.CreateWithReferenceData();
        AuraSystem auras = _world.AddInstance().Auras;

        // Applied by nobody, so no combat forms and no diminishing step is taken. No aura pass runs here, so the cached
        // state stays as applied however long a job runs.
        Require(auras.Apply(null, _renewed, s_renew, AuraSource.None), "Renew");
        Require(auras.Apply(null, _stunned, s_stunned, AuraSource.None), "Stunned");
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Baseline = true)]
    public bool CanMove_NoAura() => ControlGates.CanMove(_free);

    [Benchmark]
    public bool CanMove_OtherAura() => ControlGates.CanMove(_renewed);

    [Benchmark]
    public bool CanMove_Stunned() => ControlGates.CanMove(_stunned);

    [Benchmark]
    public bool CanCast_NoAura() => ControlGates.CanCast(_free, basic: false);

    [Benchmark]
    public bool CanCast_OtherAura() => ControlGates.CanCast(_renewed, basic: false);

    [Benchmark]
    public bool CanCast_Stunned() => ControlGates.CanCast(_stunned, basic: false);

    private static void Require(AuraApplyResult result, string aura)
    {
        if (result != AuraApplyResult.Applied)
            throw new InvalidOperationException($"{aura} was not applied: {result}");
    }

    private static CharacterEntity Character(uint id) =>
        new() { Guid = new ObjectGuid(ObjectType.Character, id), Health = 100, CurrentHealth = 100 };
}
