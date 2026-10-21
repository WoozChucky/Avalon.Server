using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The crowd-control work on the hot paths (#770), on a character holding no aura, one holding an aura that controls
/// nothing and cannot break (Renew), and one stunned (Stunned, aura 12: 10 % to break on damage). The gates player
/// input, casts and creature AI ask every tick, <see cref="ControlGates.CanMove" /> and
/// <see cref="ControlGates.CanCast" />, are one read of the state the unit's auras cache, never a look at its auras.
/// <see cref="AuraSystem.DamageTaken" />, which every damaging hit and periodic tick reports, rolls each held aura's
/// chance to break on damage. 0 B expected on every row.
/// </summary>
/// <remarks>
/// The aura system is built here over the loaded seed data, its combat service on <see cref="CombatRandom.Steady" />:
/// every roll comes up just under 1, so the stun's 10 % is drawn on each hit and never breaks, and the stunned row
/// measures the same unit, still stunned, on every iteration, with no setup between them that would itself be timed or
/// change what is measured. Nothing is applied by anyone, so no combat forms and no diminishing step is taken, and no
/// aura pass runs, so the cached state stays as applied however long a job runs.
/// </remarks>
[MemoryDiagnoser]
public class CrowdControlBenchmarks
{
    private static readonly AuraId s_renew = new(4);
    private static readonly AuraId s_stunned = new(12);

    private CharacterEntity _free = null!;
    private CharacterEntity _renewed = null!;
    private CharacterEntity _stunned = null!;
    private AuraSystem _auras = null!;

    [GlobalSetup]
    public void Setup()
    {
        _free = Character(1);
        _renewed = Character(2);
        _stunned = Character(3);

        StaticData data = ScenarioReferenceData.Shared.Data;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        var config = new CombatConfig();
        var combat = new CombatService(config, new EncounterRegistry(config, time), time: time,
            random: CombatRandom.Steady);
        var game = new GameConfiguration();
        _auras = new AuraSystem(combat, new Dictionary<ObjectGuid, ICharacter>(), new Dictionary<ObjectGuid, ICreature>(),
            () => data.Auras, () => data, time, game.MaxAurasPerUnit, game.CrowdControlDiminishWindow,
            NullLogger.Instance);

        Require(_auras.Apply(null, _renewed, s_renew, AuraSource.None), "Renew");
        Require(_auras.Apply(null, _stunned, s_stunned, AuraSource.None), "Stunned");
    }

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

    [Benchmark]
    public void DamageTaken_NoAura() => _auras.DamageTaken(_free);

    [Benchmark]
    public void DamageTaken_OtherAura() => _auras.DamageTaken(_renewed);

    [Benchmark]
    public void DamageTaken_Stunned() => _auras.DamageTaken(_stunned);

    [GlobalCleanup]
    public void Cleanup()
    {
        // The stunned row must have measured a stunned unit throughout: a break would have measured the free path.
        if ((ControlGates.Of(_stunned) & CrowdControl.Stun) == 0)
            throw new InvalidOperationException("The stun broke during the run");
    }

    private static void Require(AuraApplyResult result, string aura)
    {
        if (result != AuraApplyResult.Applied)
            throw new InvalidOperationException($"{aura} was not applied: {result}");
    }

    private static CharacterEntity Character(uint id) =>
        new() { Guid = new ObjectGuid(ObjectType.Character, id), Health = 100, CurrentHealth = 100 };
}
