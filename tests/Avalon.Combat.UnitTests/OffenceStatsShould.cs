using Avalon.Network.Packets.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// The offence block's hand-written equality (#1178), which the ability amounts' flush compares after every stats
/// refresh: any one value that differs makes two blocks differ, and equal blocks hash alike.
/// </summary>
public class OffenceStatsShould
{
    private static OffenceStats Block()
    {
        OffenceStats o = default;
        o.AddIncreased(OffenceStat.PhysicalAttack, 101.5f);
        o.AddMore(OffenceStat.Fire, 20f);
        o.CritMultiplierBonus = 25f;
        o.AttackSpeedPct = 2f;
        o.CastSpeedPct = 3f;
        o.CooldownRecoveryPct = 4f;
        return o;
    }

    [Theory]
    [InlineData("identical", true)]
    [InlineData("increased", false)]
    [InlineData("another increased key", false)]
    [InlineData("more", false)]
    [InlineData("crit multiplier bonus", false)]
    [InlineData("attack speed", false)]
    [InlineData("cast speed", false)]
    [InlineData("cooldown recovery", false)]
    public void Differ_by_any_one_value_and_hash_equal_blocks_alike(string change, bool equal)
    {
        OffenceStats other = Block();
        switch (change)
        {
            case "increased": other.AddIncreased(OffenceStat.PhysicalAttack, 0.5f); break;
            case "another increased key": other.AddIncreased(OffenceStat.Healing, 10f); break;
            case "more": other.AddMore(OffenceStat.Fire, 10f); break;
            case "crit multiplier bonus": other.CritMultiplierBonus += 1f; break;
            case "attack speed": other.AttackSpeedPct += 1f; break;
            case "cast speed": other.CastSpeedPct += 1f; break;
            case "cooldown recovery": other.CooldownRecoveryPct += 1f; break;
            default: break;
        }

        Assert.Equal(equal, Block() == other);
        Assert.Equal(equal, Block().Equals((object)other));
        if (equal) Assert.Equal(Block().GetHashCode(), other.GetHashCode());
    }
}
