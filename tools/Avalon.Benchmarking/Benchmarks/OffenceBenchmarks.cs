using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The base of an ability, which every damaging hit, every heal and every damage-over-time aura's application computes
/// before the hit maths (#1178). <see cref="AttackBase" />: <see cref="OffenceMath.AttackBase" /> for Cleave (200, an
/// Attack at 315 % effectiveness) cast by a level-1 starter Warrior (Strength 29, so 101.5 % increased physical attack
/// damage, the 4..7 Ironwood Sword, item 32): <c>roll × 3.15 × 2.015</c>. <see cref="ConvertedAttackBase" />: the same
/// for Crippling Shot (223, an Attack at 495 % effectiveness converting 50 % of its physical damage to cold) cast by a
/// level-1 starter Hunter (Strength 21, so 73.5 % increased physical attack damage, the 4..7 Hunter's Shortbow, item
/// 34): the physical half <c>roll / 2 × 4.95 × 1.735</c> and the cold half <c>roll / 2 × 4.95</c>, which Strength never
/// boosts.
/// <see cref="SpellBase" />: <see cref="OffenceMath.SpellBase" /> for Spark Bolt (210, a 14..20 lightning spell base)
/// cast by a level-1 starter Wizard (Intellect 31, so 108.5 % increased spell damage): <c>roll × 2.085</c>, no weapon.
/// <see cref="DamageOverTimeSnapshot" />: <see cref="AuraRules.Snapshot" /> of Bleed (aura 1, 12 s ticking every 3 s,
/// PeriodicBase 12, 21 % of its source's roll) from Rend (203, an Attack) cast by the Warrior:
/// <c>(12 + 0.21 × roll) × 2.015 / 4</c> per tick. 0 B expected on every row.
/// </summary>
/// <remarks>
/// The abilities, the aura and the attackers carry the seeded values (abilities and aura as seeded, attackers as the
/// stats calculator builds them at level 1 with starter gear); nothing reads the database or the formula row. All run
/// on <see cref="CombatRandom.Steady" />, whose rolls come up at their low end, so every iteration takes the same path:
/// Cleave's base is 25.389, Crippling Shot's 17.1765 physical and 9.9 cold, Spark Bolt's 29.19 and Bleed's tick 6.468.
/// Setup fails the run if any differs.
/// </remarks>
[MemoryDiagnoser]
public class OffenceBenchmarks
{
    private const double Tolerance = 1e-3;

    private static readonly WeaponDamage s_starterSword = new(4, 7, DamageType.Physical, 0, 0, null, 792);

    private readonly AttackerCombat _warrior = new(Level: 1, CritPct: 5f, Weapon: s_starterSword,
        Offence: Increased(OffenceStat.PhysicalAttack, 101.5f));

    private readonly AttackerCombat _hunter = new(Level: 1, CritPct: 5f,
        Weapon: new WeaponDamage(4, 7, DamageType.Physical, 0, 0, null, 801),
        Offence: Increased(OffenceStat.PhysicalAttack, 73.5f));

    private readonly AttackerCombat _wizard = new(Level: 1, CritPct: 5f, Weapon: s_starterSword,
        Offence: Increased(OffenceStat.Spell, 108.5f));

    private readonly AbilityMetadata _cleave = new()
    {
        Name = "Cleave",
        Tags = AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area,
        DamageType = DamageType.Physical,
        DamageEffectiveness = 315f,
    };

    private readonly AbilityMetadata _cripplingShot = new()
    {
        Name = "Crippling Shot",
        Tags = AbilityTags.Attack | AbilityTags.Projectile,
        DamageType = DamageType.Physical,
        DamageEffectiveness = 495f,
        ConvertToType = DamageType.Cold,
        ConvertPct = 50f,
    };

    private readonly AbilityMetadata _sparkBolt = new()
    {
        Name = "Spark Bolt",
        Tags = AbilityTags.Spell | AbilityTags.Projectile,
        DamageType = DamageType.Lightning,
        SpellBaseMin = 14,
        SpellBaseMax = 20,
        SpellBasePerLevelPct = 0.7f,
    };

    private readonly AbilityMetadata _rend = new()
    {
        Name = "Rend",
        Tags = AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area,
        DamageType = DamageType.Physical,
        DamageEffectiveness = 202f,
    };

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
        PeriodicEffectiveness = 21f,
        Stacking = AuraStacking.Stack,
        MaxStacks = 3,
    };

    private readonly ICombatRandom _random = CombatRandom.Steady;

    [GlobalSetup]
    public void Setup()
    {
        Expect("Cleave's base", AttackBase(), 25.389);
        DamageParts crippling = OffenceMath.AttackBase(_hunter, _cripplingShot, _random);
        Expect("Crippling Shot's physical part", crippling.Get(DamageType.Physical), 17.1765);
        Expect("Crippling Shot's cold part", crippling.Get(DamageType.Cold), 9.9);
        Expect("Crippling Shot's base", ConvertedAttackBase(), 27.0765);
        Expect("Spark Bolt's base", SpellBase(), 29.19);
        Expect("Bleed's tick", DamageOverTimeSnapshot(), 6.468);
    }

    [Benchmark]
    public double AttackBase() => OffenceMath.AttackBase(_warrior, _cleave, _random).Total;

    [Benchmark]
    public double ConvertedAttackBase() => OffenceMath.AttackBase(_hunter, _cripplingShot, _random).Total;

    [Benchmark]
    public double SpellBase() => OffenceMath.SpellBase(_wizard, _sparkBolt, _random).Total;

    [Benchmark]
    public float DamageOverTimeSnapshot() =>
        AuraRules.Snapshot(_bleed, _warrior, _rend, DamageType.Physical, _random).PerTickPerStack;

    private static OffenceStats Increased(OffenceStat stat, float points)
    {
        OffenceStats offence = default;
        offence.AddIncreased(stat, points);
        return offence;
    }

    private static void Expect(string what, double actual, double expected)
    {
        if (Math.Abs(actual - expected) > Tolerance)
            throw new InvalidOperationException($"{what} is {actual}, not {expected}.");
    }
}
