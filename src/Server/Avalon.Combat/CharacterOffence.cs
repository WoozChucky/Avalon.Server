using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>A character's offence block (#1178), the one rule the world's stats refresh and the API's amounts share. Pure.</summary>
public static class CharacterOffence
{
    /// <summary>
    /// <paramref name="strength" />, <paramref name="agility" /> and <paramref name="intellect" /> are the totals with
    /// gear. Strength gives increased physical attack damage, Intellect increased spell damage and increased healing,
    /// Agility attack speed, each by <paramref name="f" />'s rate; the gear worn adds its Increased… keys, crit multiplier,
    /// attack speed, cast speed and cooldown recovery flat; then <paramref name="auras" /> fold on top (a Flat adds, a More
    /// multiplies). Runs on a stats refresh, never per hit, and allocates nothing: <paramref name="worn" /> is read by index.
    /// </summary>
    public static OffenceStats Of(uint strength, uint agility, uint intellect, IReadOnlyList<ItemTemplate> worn,
        CombatFormula f, AuraStatTotals? auras = null)
    {
        OffenceStats o = default;
        o.AddIncreased(OffenceStat.PhysicalAttack, (float)(strength * (double)f.IncreasedPhysicalAttackPerStrength));
        o.AddIncreased(OffenceStat.Spell, (float)(intellect * (double)f.IncreasedSpellPerIntellect));
        o.AddIncreased(OffenceStat.Healing, (float)(intellect * (double)f.IncreasedHealingPerIntellect));
        o.AttackSpeedPct = (float)(agility * (double)f.AttackSpeedPerAgility);

        for (int i = 0; i < worn.Count; i++)
        {
            ItemTemplate t = worn[i];
            Add(ref o, t.StatType1, t.StatValue1);
            Add(ref o, t.StatType2, t.StatValue2);
            Add(ref o, t.StatType3, t.StatValue3);
            Add(ref o, t.StatType4, t.StatValue4);
            Add(ref o, t.StatType5, t.StatValue5);
            Add(ref o, t.StatType6, t.StatValue6);
            Add(ref o, t.StatType7, t.StatValue7);
            Add(ref o, t.StatType8, t.StatValue8);
            Add(ref o, t.StatType9, t.StatValue9);
            Add(ref o, t.StatType10, t.StatValue10);
        }

        return auras is null ? o : AuraStats.Fold(o, auras);
    }

    /// <summary>One gear stat pair with both a type and a value; any other stat is not offence.</summary>
    private static void Add(ref OffenceStats o, StatType? type, uint? value)
    {
        if (type is not { } statType || value is not { } amount) return;

        if (OffenceStats.KeyOf(statType) is { } key)
        {
            o.AddIncreased(key, amount);
            return;
        }

        switch (statType)
        {
            case StatType.CritMultiplier: o.CritMultiplierBonus += amount; break;
            case StatType.AttackSpeed: o.AttackSpeedPct += amount; break;
            case StatType.CastSpeed: o.CastSpeedPct += amount; break;
            case StatType.CooldownRecovery: o.CooldownRecoveryPct += amount; break;
            default: break;
        }
    }
}
