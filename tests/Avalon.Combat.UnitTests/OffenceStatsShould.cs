using Avalon.Network.Packets.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// The offence block's hand-written equality (#1178), which the ability amounts' flush compares, inside the attacker,
/// after every stats refresh: any one value that differs, the added damage ranges beside it too (#1179), makes two
/// attackers differ, and equal ones hash alike.
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

    private static AttackerCombat Attacker() => Attacker(Block(), Added(DamageType.Fire, 1, 3), default);

    private static AttackerCombat Attacker(OffenceStats offence, DamageRanges toAttacks, DamageRanges toSpells) =>
        new(1, 0f, Offence: offence, AddedToAttacks: toAttacks, AddedToSpells: toSpells);

    [Theory]
    [InlineData("identical", true)]
    [InlineData("increased", false)]
    [InlineData("another increased key", false)]
    [InlineData("more", false)]
    [InlineData("crit multiplier bonus", false)]
    [InlineData("attack speed", false)]
    [InlineData("cast speed", false)]
    [InlineData("cooldown recovery", false)]
    [InlineData("added to attacks", false)]
    [InlineData("added to spells", false)]
    public void Differ_by_any_one_value_and_hash_equal_blocks_alike(string change, bool equal)
    {
        OffenceStats block = Block();
        DamageRanges toAttacks = Added(DamageType.Fire, 1, 3);
        DamageRanges toSpells = default;
        switch (change)
        {
            case "increased": block.AddIncreased(OffenceStat.PhysicalAttack, 0.5f); break;
            case "another increased key": block.AddIncreased(OffenceStat.Healing, 10f); break;
            case "more": block.AddMore(OffenceStat.Fire, 10f); break;
            case "crit multiplier bonus": block.CritMultiplierBonus += 1f; break;
            case "attack speed": block.AttackSpeedPct += 1f; break;
            case "cast speed": block.CastSpeedPct += 1f; break;
            case "cooldown recovery": block.CooldownRecoveryPct += 1f; break;
            case "added to attacks": toAttacks = Added(DamageType.Fire, 1, 4); break;
            case "added to spells": toSpells = Added(DamageType.Cold, 1, 1); break;
            default: break;
        }

        AttackerCombat other = Attacker(block, toAttacks, toSpells);

        Assert.Equal(equal, Attacker() == other);
        Assert.Equal(equal, Attacker().Equals((object)other));
        if (equal) Assert.Equal(Attacker().GetHashCode(), other.GetHashCode());
    }

    private static DamageRanges Added(DamageType type, uint min, uint max)
    {
        DamageRanges ranges = default;
        ranges.Add(type, min, max);
        return ranges;
    }
}
