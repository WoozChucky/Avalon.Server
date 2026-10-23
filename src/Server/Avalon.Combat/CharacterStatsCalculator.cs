using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The one stats calculation (spec #463, closes #434): a class and level's seeded ClassLevelStat
/// row, its class's ClassStatFactors row (#506, reloaded with the combat area), the combat formula's
/// class-neutral life per Stamina, base crit and attribute conversions (#1178), plus the stats of the gear worn. Pure.
/// </summary>
public static class CharacterStatsCalculator
{
    /// <summary>
    /// Where a class's power factor is fractional, the whole sum of the factored terms is truncated to a whole number once,
    /// not each product on its own. Health and crit are the same for every class (#1178): BaseHp plus Stamina times <paramref name="formula" />'s life per
    /// Stamina plus gear health, and the formula's base crit plus gear crit; only power still reads the class's factors.
    /// Defence is the same for every class (#1178): armour, evasion and ward are gear plus Strength, Agility and Intellect
    /// times <paramref name="formula" />'s conversions, each product truncated once; block and resistances are gear's alone.
    /// Weapon is the main hand's damage and attack time (the bare hand's with none) and Offence the offence block (#1178),
    /// each attribute with gear. <paramref name="auras" />, when given, is folded in after gear: the result is what the
    /// character has now. The offence block takes them through CharacterOffence alone, so they count once there.
    /// <paramref name="worn" /> is read by index, so a refresh allocates nothing here.
    /// </summary>
    public static DerivedCharacterStats Calculate(ClassLevelStat row, IReadOnlyList<ItemTemplate> worn,
        ClassStatFactors factors, CombatFormula formula, AuraStatTotals? auras = null)
    {
        var gear = GearTotals.Of(worn);

        long stamina = row.Stamina + gear.Stamina;
        long strength = row.Strength + gear.Strength;
        long agility = row.Agility + gear.Agility;
        long intellect = row.Intellect + gear.Intellect;

        long maxHealth = row.BaseHp + stamina * formula.LifePerStamina + gear.Health;
        long maxPower = factors.FixedPower is { } fixedPower
            ? fixedPower
            : row.BaseMana
              + (long)(intellect * factors.PowerPerIntellect + agility * factors.PowerPerAgility)
              + gear.Power;
        long armor = gear.Armor + (long)(strength * (double)formula.ArmorPerStrength);
        long evasion = gear.Evasion + (long)(agility * (double)formula.EvasionPerAgility);
        long ward = gear.Ward + (long)(intellect * (double)formula.WardPerIntellect);

        // #506, #1178: the main hand, what an Attack rolls; the bare hand's range with none.
        ItemTemplate? weapon = MainHandOf(worn);

        DerivedCharacterStats derived = new(
            MaxHealth: Clamp(maxHealth),
            MaxPower: Clamp(maxPower),
            Stamina: Clamp(stamina),
            Strength: Clamp(strength),
            Agility: Clamp(agility),
            Intellect: Clamp(intellect),
            Armor: Clamp(armor),
            // #1178: no class base block or dodge any more.
            BlockPct: gear.BlockPct,
            Evasion: Clamp(evasion),
            CritPct: formula.BaseCritPct + gear.CritPct,
            MovementSpeedPct: gear.MovementSpeedPct,
            MaxWard: Clamp(ward),
            Resistances: new Resistances(gear.FireResistance, gear.ColdResistance, gear.LightningResistance,
                gear.BlightResistance),
            Weapon: WeaponDamage.Of(weapon, formula),
            Offence: CharacterOffence.Of(Clamp(strength), Clamp(agility), Clamp(intellect), worn, formula, auras));

        return auras is null ? derived : AuraStats.Fold(derived, auras);
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

    /// <summary>The first worn template for the main hand, or null.</summary>
    private static ItemTemplate? MainHandOf(IReadOnlyList<ItemTemplate> worn)
    {
        for (int i = 0; i < worn.Count; i++)
        {
            if (worn[i].Slot == ItemSlotType.MainHand) return worn[i];
        }

        return null;
    }

    /// <summary>Stats are summed in long, so no amount of authored gear can wrap a uint.</summary>
    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0L, uint.MaxValue);

    private struct GearTotals
    {
        public long Stamina, Strength, Agility, Intellect, Armor, Evasion, Ward, Health, Power;
        public float BlockPct, CritPct, MovementSpeedPct;
        public float FireResistance, ColdResistance, LightningResistance, BlightResistance;

        /// <summary>Every stat pair with both a type and a value of every template, read by index: nothing is allocated.</summary>
        public static GearTotals Of(IReadOnlyList<ItemTemplate> worn)
        {
            GearTotals totals = default;
            for (int i = 0; i < worn.Count; i++)
            {
                ItemTemplate t = worn[i];
                totals.Add(t.StatType1, t.StatValue1);
                totals.Add(t.StatType2, t.StatValue2);
                totals.Add(t.StatType3, t.StatValue3);
                totals.Add(t.StatType4, t.StatValue4);
                totals.Add(t.StatType5, t.StatValue5);
                totals.Add(t.StatType6, t.StatValue6);
                totals.Add(t.StatType7, t.StatValue7);
                totals.Add(t.StatType8, t.StatValue8);
                totals.Add(t.StatType9, t.StatValue9);
                totals.Add(t.StatType10, t.StatValue10);
            }

            return totals;
        }

        private void Add(StatType? statType, uint? statValue)
        {
            if (statType is not { } type || statValue is not { } value) return;

            switch (type)
            {
                case StatType.Stamina: Stamina += value; break;
                case StatType.Strength: Strength += value; break;
                case StatType.Agility: Agility += value; break;
                case StatType.Intellect: Intellect += value; break;
                case StatType.Armor: Armor += value; break;
                case StatType.BlockPct: BlockPct += value; break;
                case StatType.Evasion: Evasion += value; break;
                case StatType.CritPct: CritPct += value; break;
                case StatType.Health: Health += value; break;
                case StatType.Power: Power += value; break;
                // #627: a percentage, summed raw; the entity applies the combat formula's bounds. Attack speed, cast speed
                // and cooldown recovery are the offence block's (#1178), CharacterOffence's to sum.
                case StatType.MovementSpeed: MovementSpeedPct += value; break;
                case StatType.Ward: Ward += value; break;
                case StatType.FireResistance: FireResistance += value; break;
                case StatType.ColdResistance: ColdResistance += value; break;
                case StatType.LightningResistance: LightningResistance += value; break;
                case StatType.BlightResistance: BlightResistance += value; break;
                default: break;
            }
        }
    }
}
