using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The hit maths every damaging hit and every periodic damage tick runs (#1178). <see cref="ResolveCreatureHitOnArmouredCharacter" />:
/// <see cref="HitResolver.ResolveDamage" /> for a level-3 creature (the seeded level-3 natural range, 5..9, no crit
/// chance) swinging a physical 7 at a level-3 character with armour 20, evasion 23 and no block: the evasion, crit and
/// block draws, then armour. <see cref="ResolveDamageTick" />: <see cref="HitResolver.ResolvePeriodic(in AuraSnapshot, in DefenderCombat, double, CombatFormula, ICombatRandom)" />,
/// as a ticking aura resolves from its snapshot (the caster's level, crit and crit multiplier bonus), for a physical
/// 12.0 tick of that creature's aura on the same character: the crit draw, then armour. <see cref="ResolveMixedHit" />:
/// the same swing split into physical 7, fire 5 and blight 3 against a character that also resists fire by 25 %: the
/// three draws, then each part's own mitigation and the ward's share. 0 B expected on every row.
/// </summary>
/// <remarks>
/// All run on the seeded formula (<see cref="CombatSeed.Formula" />) and on <see cref="CombatRandom.Steady" />, whose
/// every roll comes up just under 1, so nothing procs and every iteration takes the same path: armour takes
/// 20 / (20 + 50 + 10 × 3) = 20 %, the swing deals 5 and the tick 9.6; the mixed hit deals
/// floor(5.6 + 3.75 + 3) = 12, of which 12 − round(12 × 3 / 12.35) = 9 a ward may take. Setup fails the run if any
/// differs.
/// </remarks>
[MemoryDiagnoser]
public class HitResolutionBenchmarks
{
    private const float SwingBase = 7f;
    private const float TickBase = 12f;

    private readonly AttackerCombat _creature = new(Level: 3, CritPct: 0f,
        Weapon: new WeaponDamage(5, 9, null, 0, 0, null, 2250));

    private readonly AuraSnapshot _creaturesAura = new(PerTickPerStack: TickBase, CritPct: 0f, CasterLevel: 3);

    private readonly DefenderCombat _character = new(Armor: 20, Evasion: 23, BlockPct: 0f,
        Resistances: new Resistances(Fire: 25f, Cold: 0f, Lightning: 0f, Blight: 0f));

    private readonly DamageParts _swing = DamageParts.Of(DamageType.Physical, SwingBase);

    private readonly ICombatRandom _random = CombatRandom.Steady;
    private CombatFormula _formula = null!;
    private DamageParts _mixed;

    [GlobalSetup]
    public void Setup()
    {
        _formula = CombatSeed.Formula();
        _mixed = DamageParts.Of(DamageType.Physical, SwingBase);
        _mixed.Set(DamageType.Fire, 5);
        _mixed.Set(DamageType.Blight, 3);

        HitDamage swing = HitResolver.ResolveDamage(_creature, _character, _swing, _formula, _random);
        if (swing.Total != 5 || swing.Result != HitResult.None)
            throw new InvalidOperationException($"The swing dealt {swing.Total} ({swing.Result}), not 5 with nothing procced.");

        (double tick, HitResult tickResult) =
            HitResolver.ResolvePeriodic(_creaturesAura, _character, TickBase, _formula, _random);
        if (Math.Abs(tick - 9.6) > 1e-6 || tickResult != HitResult.None)
            throw new InvalidOperationException($"The tick dealt {tick} ({tickResult}), not 9.6 with nothing procced.");

        HitDamage mixed = HitResolver.ResolveDamage(_creature, _character, _mixed, _formula, _random);
        if (mixed != new HitDamage(12, HitResult.None, DamageType.Physical,
                DamageTypeMask.Physical | DamageTypeMask.Fire | DamageTypeMask.Blight, 9))
        {
            throw new InvalidOperationException($"The mixed hit resolved to {mixed}, not 12 physical with 9 absorbable.");
        }
    }

    [Benchmark]
    public uint ResolveCreatureHitOnArmouredCharacter() =>
        HitResolver.ResolveDamage(_creature, _character, _swing, _formula, _random).Total;

    [Benchmark]
    public double ResolveDamageTick() =>
        HitResolver.ResolvePeriodic(_creaturesAura, _character, TickBase, _formula, _random).Damage;

    [Benchmark]
    public uint ResolveMixedHit() =>
        HitResolver.ResolveDamage(_creature, _character, _mixed, _formula, _random).Total;
}
