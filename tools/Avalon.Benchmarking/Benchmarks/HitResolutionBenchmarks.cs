using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The hit maths every damaging hit and every periodic damage tick runs (#1178), measured before the defence layers
/// change it. <see cref="ResolveCreatureHitOnArmouredCharacter" />: <see cref="HitResolver.ResolveDamage" /> for a
/// level-3 creature (the seeded level-3 natural range, 5..9, no crit chance) swinging 7 at a level-3 character with
/// armour 20, 3.25 % dodge and no block: the dodge, crit and block draws, then armour. <see cref="ResolveDamageTick" />:
/// <see cref="HitResolver.ResolvePeriodic" /> for a 12.0 tick of that creature's aura on the same character: the crit
/// draw, then armour. 0 B expected on both rows.
/// </summary>
/// <remarks>
/// Both run on the seeded formula (<see cref="CombatSeed.Formula" />) and on <see cref="CombatRandom.Steady" />, whose
/// every roll comes up just under 1, so nothing procs and every iteration takes the same path: armour takes
/// 20 / (20 + 50 + 10 × 3) = 20 %, the swing deals 5 and the tick 9.6. Setup fails the run if either differs.
/// </remarks>
[MemoryDiagnoser]
public class HitResolutionBenchmarks
{
    private const float SwingBase = 7f;
    private const float TickBase = 12f;

    private readonly AttackerCombat _creature = new(Level: 3, AttackDamage: 0, AbilityDamage: 0, CritPct: 0f,
        WeaponMin: 5, WeaponMax: 9);

    private readonly DefenderCombat _character = new(Armor: 20, DodgePct: 3.25f, BlockPct: 0f);

    private readonly ICombatRandom _random = CombatRandom.Steady;
    private CombatFormula _formula = null!;

    [GlobalSetup]
    public void Setup()
    {
        _formula = CombatSeed.Formula();

        (uint damage, HitResult result) = HitResolver.ResolveDamage(_creature, _character, SwingBase, _formula, _random);
        if (damage != 5 || result != HitResult.None)
            throw new InvalidOperationException($"The swing dealt {damage} ({result}), not 5 with nothing procced.");

        (double tick, HitResult tickResult) = HitResolver.ResolvePeriodic(_creature, _character, TickBase, _formula, _random);
        if (Math.Abs(tick - 9.6) > 1e-6 || tickResult != HitResult.None)
            throw new InvalidOperationException($"The tick dealt {tick} ({tickResult}), not 9.6 with nothing procced.");
    }

    [Benchmark(Baseline = true)]
    public uint ResolveCreatureHitOnArmouredCharacter() =>
        HitResolver.ResolveDamage(_creature, _character, SwingBase, _formula, _random).Damage;

    [Benchmark]
    public double ResolveDamageTick() =>
        HitResolver.ResolvePeriodic(_creature, _character, TickBase, _formula, _random).Damage;
}
