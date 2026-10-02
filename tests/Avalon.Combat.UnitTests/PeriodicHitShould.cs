using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>An aura's damage tick: one crit draw, then armour; never dodged or blocked.</summary>
public class PeriodicHitShould
{
    private static CombatFormula Formula() => new()
    {
        Id = CombatFormula.SingletonId, ArmorBase = 50f, ArmorPerLevel = 10f, ArmorCap = 0.75f,
        CritMultiplier = 1.5f, BlockMultiplier = 0.5f, CritCap = 50f, DodgeCap = 30f, BlockCap = 50f,
        HasteCap = 50f, MoveSpeedCap = 35f, MoveSpeedFloor = -50f,
    };

    private sealed class Rolls(params double[] doubles) : ICombatRandom
    {
        private readonly Queue<double> _doubles = new(doubles);

        public int Drawn { get; private set; }

        public double NextDouble()
        {
            Drawn++;
            return _doubles.Dequeue();
        }

        public long NextInt64(long minInclusive, long maxInclusive) => throw new InvalidOperationException("no weapon roll");
    }

    /// <summary>Armour 60 against a level-1 caster: 60 / (60 + 50 + 10) = 0.5 taken; 20 x 0.5 = 10.</summary>
    [Fact]
    public void Draw_only_the_crit_and_take_armour_off()
    {
        var rng = new Rolls(0.99);

        (double damage, HitResult result) = HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 10f, 0, 0),
            new DefenderCombat(60, DodgePct: 100f, BlockPct: 100f), 20f, Formula(), rng);

        Assert.Equal((10d, HitResult.None), (damage, result));
        Assert.Equal(1, rng.Drawn);
    }

    [Fact]
    public void Crit_a_tick_by_the_casters_snapshot_chance()
    {
        (double damage, HitResult result) = HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 10f, 0, 0),
            new DefenderCombat(0, 0f, 0f), 20f, Formula(), new Rolls(0.05));

        Assert.Equal((30d, HitResult.Crit), (damage, result));
    }

    /// <summary>The tick is not floored: the fraction is the carry's to keep.</summary>
    [Fact]
    public void Leave_a_ticks_fraction_unfloored() =>
        Assert.Equal(5.875d, HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 0f, 0, 0),
            new DefenderCombat(0, 0f, 0f), 5.875f, Formula(), new Rolls(0.99)).Damage, precision: 6);

    /// <summary>
    /// A 23.5 total over 4 ticks with no armour: 5.875 a tick, so 5, 6, 6 and a final 6.5 rounded to 7, which adds up to
    /// 24, the total rounded, where flooring each tick would give 20.
    /// </summary>
    [Fact]
    public void Add_the_ticks_up_to_the_snapshotted_total()
    {
        var dealt = new List<uint>();
        double carry = 0d;
        for (int tick = 0; tick < 4; tick++)
        {
            (double damage, _) = HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 0f, 0, 0),
                new DefenderCombat(0, 0f, 0f), 23.5f / 4f, Formula(), new Rolls(0.99));
            dealt.Add(AuraRules.TakeTick(damage, ref carry, lastTick: tick == 3));
        }

        Assert.Equal([5u, 6u, 6u, 7u], dealt);
        Assert.Equal(0d, carry);
    }

    [Fact]
    public void Heal_over_time_with_the_same_crit_draw_and_no_floor()
    {
        var rng = new Rolls(0.05);

        (double heal, HitResult result) = HitResolver.ResolvePeriodicHeal(new AttackerCombat(1, 0, 0, 10f, 0, 0),
            5.25f, Formula(), rng);

        Assert.Equal((7.875d, HitResult.Crit), (heal, result));
        Assert.Equal(1, rng.Drawn);
    }

    [Fact]
    public void Floor_a_direct_heal_as_before() =>
        Assert.Equal(7u, HitResolver.ResolveHeal(new AttackerCombat(1, 0, 0, 10f, 0, 0), 5.25f, Formula(),
            new Rolls(0.05)).Heal);
}
