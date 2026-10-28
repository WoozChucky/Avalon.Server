using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The one stats calculation (spec #463, closes #434): a class and level's seeded ClassLevelStat
/// row, the combat formula's class-neutral life per Stamina, mana terms (#1181), base crit and attribute conversions
/// (#1178), plus the stats of the gear worn. Pure.
/// </summary>
public static class CharacterStatsCalculator
{
    /// <summary>
    /// Health and crit are the same for every class (#1178): BaseHp plus Stamina times <paramref name="formula" />'s life per
    /// Stamina plus gear health, scaled by gear's increased maximum life (#1179, <c>max(0, 1 + x / 100)</c>, whole numbers, half
    /// away from zero), and the formula's base crit plus gear crit. Mana is the same for every class too (#1181), in the
    /// same shape as life: the formula's base, its gain per level past the first and Intellect times its mana per
    /// Intellect (truncated once), plus gear's flat power, scaled by gear's increased maximum mana. Gear's increased mana
    /// regeneration is summed as it is, for the regeneration rates.
    /// Defence is the same for every class (#1178): armour, evasion and ward are gear plus Strength, Agility and Intellect
    /// times <paramref name="formula" />'s conversions, each product truncated once, the whole sum scaled by the global
    /// increased armour, evasion or ward of gear and the tree (#1181); block and resistances are gear's and the tree's
    /// alone.
    /// Every worn item gives its template's lines, its copy's base stats and its modifiers' lines (#1179); a defence line is
    /// its own item's: each item's armour is <c>round((its base Armour + its Armor lines) x max(0, 1 + its
    /// LocalIncreasedArmor / 100))</c>, half away from zero, and the same for evasion and ward, so increased armour on one
    /// piece never scales another's. A copy's base block chance adds to block unscaled. Weapon is the main hand's damage
    /// and attack time (the bare hand's with none) with its own local lines and Offence the offence block (#1178), each
    /// attribute with gear. <paramref name="auras" />, when given, is folded in after gear: the result is what the
    /// character has now. The offence block takes them through CharacterOffence alone, so they count once there.
    /// <paramref name="worn" /> is read by index, so a refresh allocates nothing here. Pure but for
    /// <paramref name="resonance" />: when given, bound by the caller to its catalog's table, it is cleared and filled with
    /// each Resonance theme's count across the worn items (#1180, <see cref="ResonanceCounts.AddItem" />) in the same walk;
    /// null counts nothing. <paramref name="talents" />, when given (#1181), folds exactly as gear's lines do, before the
    /// auras: its attributes join gear's before the conversions; its flat life, mana, armour, evasion and ward join gear's;
    /// its increased maximum life and mana and its increased armour, evasion and ward join gear's global ones, each
    /// increase scaling the whole rating (gear, attributes and flat lines together); its offence lines and added damage
    /// join gear's sums before <see cref="CharacterOffence.Finish" />; and its More on life or mana multiplies the scaled
    /// maximum last (<see cref="LocalScaling.More" />). Null, or empty, folds nothing. A tree holding Steady Hand
    /// (<see cref="TalentTotals.NoCrits" />, #1181 Part D) sets the crit chance to 0 after the auras, so no aura restores it.
    /// </summary>
    public static DerivedCharacterStats Calculate(ClassLevelStat row, IReadOnlyList<WornItem> worn,
        CombatFormula formula, AuraStatTotals? auras = null, ResonanceCounts? resonance = null,
        TalentTotals? talents = null)
    {
        // The tree's offence lines and added damage are where the sums start, so gear's join them and Finish folds both;
        // then one walk of every worn item's lines feeds the gear totals, the offence block's line sums (#1179) and the
        // Resonance counts (#1180), and the tree's other sums join the gear totals.
        TalentTotals? tree = talents is { IsEmpty: false } ? talents : null;
        OffenceStats offenceLines = tree?.Offence ?? default;
        DamageRanges addedToAttacks = tree?.AddedToAttacks ?? default;
        DamageRanges addedToSpells = tree?.AddedToSpells ?? default;
        resonance?.Clear();
        var gear = GearTotals.Of(worn, ref offenceLines, ref addedToAttacks, ref addedToSpells, resonance);
        if (tree is not null)
            gear.Add(tree);

        long stamina = row.Stamina + gear.Stamina;
        long strength = row.Strength + gear.Strength;
        long agility = row.Agility + gear.Agility;
        long intellect = row.Intellect + gear.Intellect;

        long flatLife = LocalScaling.Saturate((Int128)row.BaseHp + ((Int128)stamina * formula.LifePerStamina) + gear.Health);
        long maxHealth = LocalScaling.More(LocalScaling.Increase(flatLife, gear.IncreasedMaximumLife),
            tree?.MoreLife ?? 1d);
        // Intellect's share is held to the long's range before the sum, so a huge conversion caps the maximum
        // rather than wrapping it, as life's does.
        long flatMana = LocalScaling.Saturate((Int128)formula.ManaBase
            + ((Int128)formula.ManaPerLevel * Math.Max(0, row.Level - 1))
            + (Int128)Math.Clamp(intellect * (double)formula.ManaPerIntellect, long.MinValue, long.MaxValue)
            + gear.Power);
        long maxPower = LocalScaling.More(LocalScaling.Increase(flatMana, gear.IncreasedMaximumMana),
            tree?.MoreMana ?? 1d);
        // A global increase scales the whole rating: every item's own (locally scaled) defence, the tree's flat lines and
        // the attribute's share (#1181).
        long armor = LocalScaling.Increase(
            LocalScaling.Add(gear.Armor, (long)(strength * (double)formula.ArmorPerStrength)), gear.IncreasedArmor);
        long evasion = LocalScaling.Increase(
            LocalScaling.Add(gear.Evasion, (long)(agility * (double)formula.EvasionPerAgility)), gear.IncreasedEvasion);
        long ward = LocalScaling.Increase(
            LocalScaling.Add(gear.Ward, (long)(intellect * (double)formula.WardPerIntellect)), gear.IncreasedWard);

        // #506, #1178: the main hand, what an Attack rolls; the bare hand's range with none.
        WornItem? weapon = MainHandOf(worn);
        OffenceStats offence = CharacterOffence.Finish(offenceLines, Clamp(strength), Clamp(agility), Clamp(intellect),
            formula, auras);

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
            Offence: offence,
            AddedToAttacks: addedToAttacks,
            AddedToSpells: addedToSpells,
            IncreasedManaRegenPct: gear.IncreasedManaRegenPct);

        DerivedCharacterStats folded = auras is null ? derived : AuraStats.Fold(derived, auras);

        // Steady Hand (#1181 Part D): after the aura fold, so no aura gives the crit chance back. Hits, heals and aura
        // snapshots all read this, so none needs a check of its own.
        return tree is { NoCrits: true } ? folded with { CritPct = 0f } : folded;
    }

    /// <summary>
    /// Each Resonance theme's count across <paramref name="worn" /> into <paramref name="into" /> (#1180), which the caller
    /// has bound to its catalog's table: cleared, then the same per-item step <see cref="Calculate" />'s walk takes
    /// (<see cref="ResonanceCounts.AddItem" />). For a reader with no class row to calculate with, the API's sheet; the
    /// worn list is the caller's, holding only items in a slot that accepts them.
    /// </summary>
    public static void CountResonance(IReadOnlyList<WornItem> worn, ResonanceCounts into)
    {
        into.Clear();
        for (int i = 0; i < worn.Count; i++)
            into.AddItem(worn[i].Modifiers);
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

    /// <summary>
    /// The first worn item for the main hand, or null: the weapon the calculation rolls, and the one the worlds API words an
    /// ability's amount with, so both read the same item.
    /// </summary>
    public static WornItem? MainHandOf(IReadOnlyList<WornItem> worn)
    {
        for (int i = 0; i < worn.Count; i++)
        {
            WornItem item = worn[i];
            if (item.Template.Slot == ItemSlotType.MainHand) return item;
        }

        return null;
    }

    /// <summary>
    /// Stats are summed in long, and a product or an item's scaled defence that could pass a long is held to its range
    /// (<see cref="LocalScaling" />), so no amount of authored gear can wrap a uint.
    /// </summary>
    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0L, uint.MaxValue);

    private struct GearTotals
    {
        public long Stamina, Strength, Agility, Intellect, Armor, Evasion, Ward, Health, IncreasedMaximumLife, Power,
            IncreasedMaximumMana, IncreasedArmor, IncreasedEvasion, IncreasedWard;
        public float BlockPct, CritPct, MovementSpeedPct, IncreasedManaRegenPct;
        public float FireResistance, ColdResistance, LightningResistance, BlightResistance;

        /// <summary>
        /// Every line of every worn item, read by index and through <see cref="ItemLines" />, and its copy's base stats, read
        /// in place: nothing is allocated. Each item's base defence and defence lines are summed apart and scaled by its own
        /// local increased defence before they join the total; its base block chance joins block as it is; every other line
        /// also goes to the offence block's sums (<see cref="CharacterOffence.Add" />), and each item's modifiers to the
        /// Resonance counts when there are any (<see cref="ResonanceCounts.AddItem" />), so the walk is the only one.
        /// </summary>
        public static GearTotals Of(IReadOnlyList<WornItem> worn, ref OffenceStats offence, ref DamageRanges toAttacks,
            ref DamageRanges toSpells, ResonanceCounts? resonance)
        {
            GearTotals totals = default;
            for (int i = 0; i < worn.Count; i++)
            {
                WornItem item = worn[i];
                resonance?.AddItem(item.Modifiers);
                var defence = ItemDefence.Of(item.BaseStats);
                totals.BlockPct += item.BaseStats.BlockChance.GetValueOrDefault();
                foreach (ItemLine line in ItemLines.Of(item))
                {
                    if (defence.Add(line))
                        continue;

                    totals.Add(line.Stat, line.Min);
                    CharacterOffence.Add(ref offence, ref toAttacks, ref toSpells, line);
                }

                totals.Armor = LocalScaling.Add(totals.Armor, defence.Armor);
                totals.Evasion = LocalScaling.Add(totals.Evasion, defence.Evasion);
                totals.Ward = LocalScaling.Add(totals.Ward, defence.Ward);
            }

            return totals;
        }

        /// <summary>
        /// The tree's sums joined to the gear's (#1181), each the same stat a gear line of it adds to. Its offence lines
        /// and added damage are not here: they start the offence sums before the walk.
        /// </summary>
        public void Add(TalentTotals tree)
        {
            Stamina += tree.Stamina;
            Strength += tree.Strength;
            Agility += tree.Agility;
            Intellect += tree.Intellect;
            Armor = LocalScaling.Add(Armor, tree.Armor);
            Evasion = LocalScaling.Add(Evasion, tree.Evasion);
            Ward = LocalScaling.Add(Ward, tree.Ward);
            Health += tree.Health;
            Power += tree.Power;
            IncreasedMaximumLife += tree.IncreasedMaximumLife;
            IncreasedMaximumMana += tree.IncreasedMaximumMana;
            IncreasedArmor += tree.IncreasedArmor;
            IncreasedEvasion += tree.IncreasedEvasion;
            IncreasedWard += tree.IncreasedWard;
            IncreasedManaRegenPct += tree.IncreasedManaRegenPct;
            BlockPct += tree.BlockPct;
            CritPct += tree.CritPct;
            MovementSpeedPct += tree.MovementSpeedPct;
            FireResistance += tree.FireResistance;
            ColdResistance += tree.ColdResistance;
            LightningResistance += tree.LightningResistance;
            BlightResistance += tree.BlightResistance;
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
                case StatType.IncreasedMaximumLife: IncreasedMaximumLife += value; break;
                case StatType.Power: Power += value; break;
                case StatType.IncreasedMaximumMana: IncreasedMaximumMana += value; break;
                case StatType.IncreasedManaRegeneration: IncreasedManaRegenPct += value; break;
                // #1181: global, scaling the whole rating, unlike an item's local increase (ItemDefence).
                case StatType.IncreasedArmor: IncreasedArmor += value; break;
                case StatType.IncreasedEvasion: IncreasedEvasion += value; break;
                case StatType.IncreasedWard: IncreasedWard += value; break;
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

    /// <summary>
    /// One item's own defence (#1179, A-R13): its copy's base armour, evasion and ward, its flat armour, evasion and ward
    /// lines, and its local increases, which scale both.
    /// </summary>
    private struct ItemDefence
    {
        private long _armor, _evasion, _ward, _increasedArmor, _increasedEvasion, _increasedWard;

        /// <summary>An item's defence before its lines: the base stats its copy rolled.</summary>
        public static ItemDefence Of(in ItemBaseStats baseStats) => new()
        {
            _armor = baseStats.Armour.GetValueOrDefault(),
            _evasion = baseStats.Evasion.GetValueOrDefault(),
            _ward = baseStats.Ward.GetValueOrDefault(),
        };

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

        private static long Scaled(long flat, long increasedPct) => LocalScaling.Increase(flat, increasedPct);
    }
}
