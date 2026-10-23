using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The base of an ability, which every damaging hit, every heal and every damage-over-time aura's application computes
/// before the hit maths (#1178). <see cref="AttackBase" />: <see cref="HitResolver.AbilityBase" /> for Cleave (200) cast by
/// a level-1 starter Warrior (attack damage 58, a 4..7 main hand): <c>12 + 0.3 × 58 + 1.0 × roll</c>. <see cref="SpellBase" />:
/// the same for Spark Bolt (210) cast by a level-1 starter Wizard (ability damage 93): <c>12 + 0.25 × 93</c>, no weapon
/// term. <see cref="DamageOverTimeSnapshot" />: <see cref="AuraRules.Snapshot" /> of Bleed (aura 1, 12 s ticking every
/// 3 s) from the Warrior: <c>(12 + 0.25 × 58) / 4</c> per tick. 0 B expected on every row.
/// </summary>
/// <remarks>
/// All run on the seeded formula (<see cref="CombatSeed.Formula" />) and on <see cref="CombatRandom.Steady" />, whose
/// weapon roll comes up at its low end, so every iteration takes the same path: Cleave's base is 33.4, Spark Bolt's 35.25
/// and Bleed's tick 6.625. Setup fails the run if any differs.
/// </remarks>
[MemoryDiagnoser]
public class OffenceBenchmarks
{
    private const float Tolerance = 1e-4f;

    private readonly AttackerCombat _warrior = new(Level: 1, AttackDamage: 58, AbilityDamage: 4, CritPct: 5f,
        WeaponMin: 4, WeaponMax: 7);

    private readonly AttackerCombat _wizard = new(Level: 1, AttackDamage: 10, AbilityDamage: 93, CritPct: 1.85f,
        WeaponMin: 4, WeaponMax: 7);

    private readonly AuraTemplate _bleed = new()
    {
        Id = 1,
        Name = "Bleed",
        Icon = "bleed",
        Kind = AuraKind.Harmful,
        DurationMs = 12000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage,
        PeriodicBase = 12f,
        ScalingStat = ScalingStat.Attack,
        ScalingCoefficient = 0.25f,
        BaseDamageCoefficient = 0f,
        Stacking = AuraStacking.Stack,
        MaxStacks = 3,
    };

    private readonly ICombatRandom _random = CombatRandom.Steady;
    private CombatFormula _formula = null!;

    [GlobalSetup]
    public void Setup()
    {
        _formula = CombatSeed.Formula();

        Expect("Cleave's base", AttackBase(), 33.4f);
        Expect("Spark Bolt's base", SpellBase(), 35.25f);
        Expect("Bleed's tick", DamageOverTimeSnapshot(), 6.625f);
    }

    [Benchmark]
    public float AttackBase() =>
        HitResolver.AbilityBase(_warrior, 12f, ScalingStat.Attack, 0.3f, 1.0f, _random);

    [Benchmark]
    public float SpellBase() =>
        HitResolver.AbilityBase(_wizard, 12f, ScalingStat.Ability, 0.25f, 0f, _random);

    [Benchmark]
    public float DamageOverTimeSnapshot() =>
        AuraRules.Snapshot(_bleed, _warrior, DamageType.Physical, _random).PerTickPerStack;

    private static void Expect(string what, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > Tolerance)
            throw new InvalidOperationException($"{what} is {actual}, not {expected}.");
    }
}
