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
    /// Every worn item gives its template's lines and its modifiers' (#1179); a defence line is its own item's: each item's
    /// armour is <c>round(its Armor lines x max(0, 1 + its LocalIncreasedArmor / 100))</c>, half away from zero, and the
    /// same for evasion and ward, so increased armour on one piece never scales another's. Weapon is the main hand's damage
    /// and attack time (the bare hand's with none) with its own local lines and Offence the offence block (#1178), each
    /// attribute with gear. <paramref name="auras" />, when given, is folded in after gear: the result is what the
    /// character has now. The offence block takes them through CharacterOffence alone, so they count once there.
    /// <paramref name="worn" /> is read by index, so a refresh allocates nothing here.
    /// </summary>
    public static DerivedCharacterStats Calculate(ClassLevelStat row, IReadOnlyList<WornItem> worn,
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
        WornItem? weapon = MainHandOf(worn);

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

    /// <summary>The first worn item for the main hand, or null.</summary>
    private static WornItem? MainHandOf(IReadOnlyList<WornItem> worn)
    {
        for (int i = 0; i < worn.Count; i++)
        {
            WornItem item = worn[i];
            if (item.Template.Slot == ItemSlotType.MainHand) return item;
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

        /// <summary>
        /// Every line of every worn item, read by index and through <see cref="ItemLines" />: nothing is allocated. Each
        /// item's defence lines are summed apart and scaled by its own local increased defence before they join the total.
        /// </summary>
        public static GearTotals Of(IReadOnlyList<WornItem> worn)
        {
            GearTotals totals = default;
            for (int i = 0; i < worn.Count; i++)
            {
                WornItem item = worn[i];
                ItemDefence defence = default;
                foreach (ItemLine line in ItemLines.Of(item))
                {
                    if (!defence.Add(line)) totals.Add(line.Stat, line.Min);
                }

                totals.Armor += defence.Armor;
                totals.Evasion += defence.Evasion;
                totals.Ward += defence.Ward;
            }

            return totals;
        }

        private void Add(StatType type, int value)
        {
            switch (type)
            {
                case StatType.Stamina: Stamina += value; break;
                case StatType.Strength: Strength += value; break;
                case StatType.Agility: Agility += value; break;
                case StatType.Intellect: Intellect += value; break;
                case StatType.BlockPct: BlockPct += value; break;
                case StatType.CritPct: CritPct += value; break;
                case StatType.Health: Health += value; break;
                case StatType.Power: Power += value; break;
                // #627: a percentage, summed raw; the entity applies the combat formula's bounds. Attack speed, cast speed
                // and cooldown recovery are the offence block's (#1178), CharacterOffence's to sum.
                case StatType.MovementSpeed: MovementSpeedPct += value; break;
                case StatType.FireResistance: FireResistance += value; break;
                case StatType.ColdResistance: ColdResistance += value; break;
                case StatType.LightningResistance: LightningResistance += value; break;
                case StatType.BlightResistance: BlightResistance += value; break;
                default: break;
            }
        }
    }

    /// <summary>One item's own defence (#1179, A-R13): its flat armour, evasion and ward lines and its local increases.</summary>
    private struct ItemDefence
    {
        private long _armor, _evasion, _ward, _increasedArmor, _increasedEvasion, _increasedWard;

        public readonly long Armor => Scaled(_armor, _increasedArmor);

        public readonly long Evasion => Scaled(_evasion, _increasedEvasion);

        public readonly long Ward => Scaled(_ward, _increasedWard);

        /// <summary>True when <paramref name="line" /> is a defence line, which this item keeps for itself.</summary>
        public bool Add(in ItemLine line)
        {
            switch (line.Stat)
            {
                case StatType.Armor: _armor += line.Min; return true;
                case StatType.Evasion: _evasion += line.Min; return true;
                case StatType.Ward: _ward += line.Min; return true;
                case StatType.LocalIncreasedArmor: _increasedArmor += line.Min; return true;
                case StatType.LocalIncreasedEvasion: _increasedEvasion += line.Min; return true;
                case StatType.LocalIncreasedWard: _increasedWard += line.Min; return true;
                default: return false;
            }
        }

        private static long Scaled(long flat, long increasedPct) =>
            (long)Math.Round(flat * Math.Max(0d, 1d + (increasedPct / 100d)), MidpointRounding.AwayFromZero);
    }
}
