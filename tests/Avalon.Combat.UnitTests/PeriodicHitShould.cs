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

        (uint damage, HitResult result) = HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 10f, 0, 0),
            new DefenderCombat(60, DodgePct: 100f, BlockPct: 100f), 20f, Formula(), rng);

        Assert.Equal((10u, HitResult.None), (damage, result));
        Assert.Equal(1, rng.Drawn);
    }

    [Fact]
    public void Crit_a_tick_by_the_casters_snapshot_chance()
    {
        (uint damage, HitResult result) = HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 10f, 0, 0),
            new DefenderCombat(0, 0f, 0f), 20f, Formula(), new Rolls(0.05));

        Assert.Equal((30u, HitResult.Crit), (damage, result));
    }

    [Fact]
    public void Deal_at_least_one_from_a_tick() =>
        Assert.Equal(1u, HitResolver.ResolvePeriodic(new AttackerCombat(1, 0, 0, 0f, 0, 0), new DefenderCombat(0, 0f, 0f),
            0.2f, Formula(), new Rolls(0.99)).Damage);
}
