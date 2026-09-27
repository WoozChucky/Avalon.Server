using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.World.Characters;

/// <summary>
/// The one stats calculation (spec #463, closes #434): a class and level's seeded ClassLevelStat
/// row, its class's ClassStatFactors row (#506, reloaded with the combat area), plus the stats of
/// the gear worn. Pure.
/// </summary>
public static class CharacterStatsCalculator
{
    /// <summary>
    /// Where a class factor is fractional, the whole sum of the factored terms is truncated to a
    /// whole number once, not each product on its own: a level-1 Hunter (Strength 21, Agility 23)
    /// attacks for <c>(long)(0.5 x 21 + 1.5 x 23) = 45</c>, where truncating each product would give 44.
    /// </summary>
    public static DerivedCharacterStats Calculate(ClassLevelStat row, IEnumerable<ItemTemplate> worn,
        ClassStatFactors factors)
    {
        GearTotals gear = GearTotals.Of(worn);

        long stamina = row.Stamina + gear.Stamina;
        long strength = row.Strength + gear.Strength;
        long agility = row.Agility + gear.Agility;
        long intellect = row.Intellect + gear.Intellect;

        long maxHealth = row.BaseHp + stamina * factors.HpPerStamina + gear.Health;
        long maxPower = factors.FixedPower is { } fixedPower
            ? fixedPower
            : row.BaseMana
              + (long)(intellect * factors.PowerPerIntellect + agility * factors.PowerPerAgility)
              + gear.Power;
        long attack = (long)(strength * factors.AttackPerStrength + agility * factors.AttackPerAgility) + gear.AttackDamage;
        long ability = (long)(intellect * factors.AbilityPerIntellect) + gear.AbilityDamage;

        return new DerivedCharacterStats(
            MaxHealth: Clamp(maxHealth),
            MaxPower: Clamp(maxPower),
            Stamina: Clamp(stamina),
            Strength: Clamp(strength),
            Agility: Clamp(agility),
            Intellect: Clamp(intellect),
            Armor: Clamp(gear.Armor),
            BlockPct: factors.BaseBlock + gear.BlockPct,
            DodgePct: factors.BaseDodge + gear.DodgePct,
            CritPct: factors.BaseCrit + gear.CritPct,
            AttackDamage: Clamp(attack),
            AbilityDamage: Clamp(ability));
    }

    /// <summary>
    /// The same share of a new maximum, rounded to the nearest point, for a gear change. A full
    /// pool stays full, an empty one stays empty, and one with anything in it keeps at least 1, so
    /// a gear change can neither kill nor revive. A maximum of 0 holds nothing.
    /// </summary>
    public static uint KeepShare(uint current, uint oldMax, uint newMax)
    {
        if (newMax == 0)
            return 0;

        if (oldMax == 0 || current >= oldMax)
            return newMax;

        if (current == 0)
            return 0;

        ulong scaled = ((ulong)current * newMax + oldMax / 2) / oldMax;
        return (uint)Math.Clamp(scaled, 1UL, newMax);
    }

    /// <summary>Stats are summed in long, so no amount of authored gear can wrap a uint.</summary>
    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0L, uint.MaxValue);

    private struct GearTotals
    {
        public long Stamina, Strength, Agility, Intellect, Armor, AttackDamage, AbilityDamage, Health, Power;
        public float BlockPct, DodgePct, CritPct;

        public static GearTotals Of(IEnumerable<ItemTemplate> worn)
        {
            GearTotals totals = default;
            foreach (ItemTemplate template in worn)
            {
                foreach ((StatType type, uint value) in StatsOf(template))
                    totals.Add(type, value);
            }

            return totals;
        }

        private void Add(StatType type, uint value)
        {
            switch (type)
            {
                case StatType.Stamina: Stamina += value; break;
                case StatType.Strength: Strength += value; break;
                case StatType.Agility: Agility += value; break;
                case StatType.Intellect: Intellect += value; break;
                case StatType.Armor: Armor += value; break;
                case StatType.BlockPct: BlockPct += value; break;
                case StatType.DodgePct: DodgePct += value; break;
                case StatType.CritPct: CritPct += value; break;
                case StatType.AttackDamage: AttackDamage += value; break;
                case StatType.AbilityDamage: AbilityDamage += value; break;
                case StatType.Health: Health += value; break;
                case StatType.Power: Power += value; break;
                // AttackSpeed and MovementSpeed feed nothing yet.
                default: break;
            }
        }

        /// <summary>The template's stat pairs that have both a type and a value.</summary>
        private static IEnumerable<(StatType Type, uint Value)> StatsOf(ItemTemplate t)
        {
            (StatType? Type, uint? Value)[] pairs =
            [
                (t.StatType1, t.StatValue1), (t.StatType2, t.StatValue2), (t.StatType3, t.StatValue3),
                (t.StatType4, t.StatValue4), (t.StatType5, t.StatValue5), (t.StatType6, t.StatValue6),
                (t.StatType7, t.StatValue7), (t.StatType8, t.StatValue8), (t.StatType9, t.StatValue9),
                (t.StatType10, t.StatValue10),
            ];

            foreach ((StatType? type, uint? value) in pairs)
            {
                if (type is { } statType && value is { } statValue)
                    yield return (statType, statValue);
            }
        }
    }
}
