using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>A character's offence block (#1178), the one rule the world's stats refresh and the API's amounts share. Pure.</summary>
public static class CharacterOffence
{
    /// <summary>
    /// <paramref name="strength" />, <paramref name="agility" /> and <paramref name="intellect" /> are the totals with
    /// gear. Strength gives increased physical attack damage, Intellect increased spell damage and increased healing,
    /// Agility attack speed, each by <paramref name="f" />'s rate; every line of the gear worn (its template's and its
    /// modifiers', #1179) adds its Increased… keys, crit multiplier, attack speed, cast speed and cooldown recovery flat,
    /// and its added damage to attacks and to spells by type, handed out as <paramref name="addedToAttacks" /> and
    /// <paramref name="addedToSpells" />, which sit beside the block on the attacker; a line local to its item (a weapon's
    /// added damage, local increased damage or defence, local attack speed) is that item's, never the block's. Then
    /// <paramref name="auras" /> fold on top (a Flat adds, a More multiplies). The gear's lines are summed first and the
    /// attributes' terms added to those sums, as the stats calculation, which folds the lines in its own single walk of the
    /// gear (<see cref="Add" />, <see cref="Finish" />), does. <paramref name="talents" />, when given (#1181), is where the
    /// sums start: the tree's offence lines, Mores and added damage, which the gear's join; the attributes passed then
    /// include the tree's. Runs on a stats refresh, never per hit, and allocates nothing: <paramref name="worn" /> is read
    /// by index.
    /// </summary>
    public static OffenceStats Of(uint strength, uint agility, uint intellect, IReadOnlyList<WornItem> worn,
        CombatFormula f, out DamageRanges addedToAttacks, out DamageRanges addedToSpells, AuraStatTotals? auras = null,
        TalentTotals? talents = null)
    {
        OffenceStats o = talents?.Offence ?? default;
        addedToAttacks = talents?.AddedToAttacks ?? default;
        addedToSpells = talents?.AddedToSpells ?? default;
        for (int i = 0; i < worn.Count; i++)
        {
            WornItem item = worn[i];
            foreach (ItemLine line in ItemLines.Of(item))
                Add(ref o, ref addedToAttacks, ref addedToSpells, line);
        }

        return Finish(o, strength, agility, intellect, f, auras);
    }

    /// <summary>
    /// <paramref name="lines" />, the gear's lines summed by <see cref="Add" />, with the attributes' terms added and
    /// <paramref name="auras" /> folded on top.
    /// </summary>
    internal static OffenceStats Finish(OffenceStats lines, uint strength, uint agility, uint intellect, CombatFormula f,
        AuraStatTotals? auras)
    {
        OffenceStats o = lines;
        o.AddIncreased(OffenceStat.PhysicalAttack, (float)(strength * (double)f.IncreasedPhysicalAttackPerStrength));
        o.AddIncreased(OffenceStat.Spell, (float)(intellect * (double)f.IncreasedSpellPerIntellect));
        o.AddIncreased(OffenceStat.Healing, (float)(intellect * (double)f.IncreasedHealingPerIntellect));
        o.AttackSpeedPct += (float)(agility * (double)f.AttackSpeedPerAgility);
        return auras is null ? o : AuraStats.Fold(o, auras);
    }

    /// <summary>One gear line; any stat that is not offence adds nothing here.</summary>
    internal static void Add(ref OffenceStats o, ref DamageRanges toAttacks, ref DamageRanges toSpells, in ItemLine line)
    {
        StatType stat = line.Stat;
        if (OffenceStats.KeyOf(stat) is { } key)
        {
            o.AddIncreased(key, line.Min);
            return;
        }

        switch (stat)
        {
            case StatType.CritMultiplier: o.CritMultiplierBonus += line.Min; break;
            case StatType.AttackSpeed: o.AttackSpeedPct += line.Min; break;
            case StatType.CastSpeed: o.CastSpeedPct += line.Min; break;
            case StatType.CooldownRecovery: o.CooldownRecoveryPct += line.Min; break;
            case >= StatType.AddedPhysicalDamageToAttacks and <= StatType.AddedBlightDamageToAttacks:
                toAttacks.Add((DamageType)(stat - StatType.AddedPhysicalDamageToAttacks), line);
                break;
            case >= StatType.AddedFireDamageToSpells and <= StatType.AddedLightningDamageToSpells:
                toSpells.Add((DamageType)(stat - StatType.AddedFireDamageToSpells + (int)DamageType.Fire), line);
                break;
            default: break;
        }
    }
}
