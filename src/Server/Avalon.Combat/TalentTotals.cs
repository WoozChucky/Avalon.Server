using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The summed stat lines of the talent nodes a character holds (#1181), which the stats calculation folds exactly as it
/// folds gear's (<see cref="CharacterStatsCalculator.Calculate" />, <see cref="CharacterOffence.Of" />). A reusable buffer
/// rebuilt in place (<see cref="Clear" />, then <see cref="Add" /> per line and <see cref="AddKeystone" /> per keystone rule)
/// only when the allocation changes, never per refresh or per tick; neither it nor a fold that reads it allocates. The three
/// keystone flags (#1181 Part D) are read where their rules act. Not thread-safe: a character's totals are the tick's.
/// </summary>
public sealed class TalentTotals
{
    private OffenceStats _offence;
    private DamageRanges _addedToAttacks;
    private DamageRanges _addedToSpells;

    /// <summary>
    /// No line or keystone rule has been added since the last <see cref="Clear" />: a fold reads nothing from it.
    /// </summary>
    public bool IsEmpty { get; private set; } = true;

    /// <summary>Blood Price (#1181 Part D): the holder pays ability costs from current life (<see cref="AbilityCost" />).</summary>
    public bool LifeCost { get; private set; }

    /// <summary>
    /// Steady Hand (#1181 Part D): the holder's crit chance is 0 after every fold
    /// (<see cref="CharacterStatsCalculator.Calculate" />), so no hit, heal or aura tick of its own crits.
    /// </summary>
    public bool NoCrits { get; private set; }

    /// <summary>
    /// Unbroken (#1181 Part D): the holder's ward takes blight and bleeds too (<see cref="HitResolver.WardAbsorbs" />), and
    /// waits twice as long to recharge (<see cref="WardPool.Advance" />).
    /// </summary>
    public bool WardTakesAll { get; private set; }

    internal long Stamina { get; private set; }

    internal long Strength { get; private set; }

    internal long Agility { get; private set; }

    internal long Intellect { get; private set; }

    internal long Armor { get; private set; }

    internal long Evasion { get; private set; }

    internal long Ward { get; private set; }

    internal long Health { get; private set; }

    internal long Power { get; private set; }

    internal long IncreasedMaximumLife { get; private set; }

    internal long IncreasedMaximumMana { get; private set; }

    internal long IncreasedArmor { get; private set; }

    internal long IncreasedEvasion { get; private set; }

    internal long IncreasedWard { get; private set; }

    internal float IncreasedManaRegenPct { get; private set; }

    internal float BlockPct { get; private set; }

    internal float CritPct { get; private set; }

    internal float MovementSpeedPct { get; private set; }

    internal float FireResistance { get; private set; }

    internal float ColdResistance { get; private set; }

    internal float LightningResistance { get; private set; }

    internal float BlightResistance { get; private set; }

    /// <summary>The offence lines, summed as gear's are (<see cref="CharacterOffence.Add" />), with every More multiplied in.</summary>
    internal OffenceStats Offence => _offence;

    internal DamageRanges AddedToAttacks => _addedToAttacks;

    internal DamageRanges AddedToSpells => _addedToSpells;

    /// <summary>The product of every More on maximum life: 1 with none.</summary>
    internal double MoreLife { get; private set; } = 1d;

    /// <summary>The product of every More on maximum mana: 1 with none.</summary>
    internal double MoreMana { get; private set; } = 1d;

    /// <summary>Every sum back to nothing, both More factors back to 1 and no keystone rule, for a rebuild.</summary>
    public void Clear()
    {
        Stamina = Strength = Agility = Intellect = 0;
        Armor = Evasion = Ward = Health = Power = 0;
        IncreasedMaximumLife = IncreasedMaximumMana = IncreasedArmor = IncreasedEvasion = IncreasedWard = 0;
        IncreasedManaRegenPct = BlockPct = CritPct = MovementSpeedPct = 0f;
        FireResistance = ColdResistance = LightningResistance = BlightResistance = 0f;
        _offence = default;
        _addedToAttacks = default;
        _addedToSpells = default;
        MoreLife = MoreMana = 1d;
        LifeCost = NoCrits = WardTakesAll = false;
        IsEmpty = true;
    }

    /// <summary>
    /// One node's stat line. <see cref="TalentStatKind.Added" /> sums with the gear lines of the same stat: the attributes,
    /// the flat defences, life and mana, their increases, block, crit, movement and the resistances here, every offence
    /// line as gear's (an "adds X to Y" line from <paramref name="value" /> to <paramref name="valueMax" />).
    /// <see cref="TalentStatKind.More" /> multiplies an offence key, or maximum life or mana, by <c>1 + value / 100</c>,
    /// at most down to nothing. The catalog refuses any other line (<see cref="TalentStatRules" />); one that slips through
    /// adds nothing.
    /// </summary>
    public void Add(StatType stat, TalentStatKind kind, int value, int? valueMax)
    {
        IsEmpty = false;
        if (kind == TalentStatKind.More)
        {
            AddMore(stat, value);
            return;
        }

        if (kind != TalentStatKind.Added)
            return;

        switch (stat)
        {
            case StatType.Stamina: Stamina += value; break;
            case StatType.Strength: Strength += value; break;
            case StatType.Agility: Agility += value; break;
            case StatType.Intellect: Intellect += value; break;
            case StatType.Armor: Armor += value; break;
            case StatType.Evasion: Evasion += value; break;
            case StatType.Ward: Ward += value; break;
            case StatType.Health: Health += value; break;
            case StatType.Power: Power += value; break;
            case StatType.IncreasedMaximumLife: IncreasedMaximumLife += value; break;
            case StatType.IncreasedMaximumMana: IncreasedMaximumMana += value; break;
            case StatType.IncreasedArmor: IncreasedArmor += value; break;
            case StatType.IncreasedEvasion: IncreasedEvasion += value; break;
            case StatType.IncreasedWard: IncreasedWard += value; break;
            case StatType.IncreasedManaRegeneration: IncreasedManaRegenPct += value; break;
            case StatType.BlockPct: BlockPct += value; break;
            case StatType.CritPct: CritPct += value; break;
            case StatType.MovementSpeed: MovementSpeedPct += value; break;
            case StatType.FireResistance: FireResistance += value; break;
            case StatType.ColdResistance: ColdResistance += value; break;
            case StatType.LightningResistance: LightningResistance += value; break;
            case StatType.BlightResistance: BlightResistance += value; break;
            default:
                CharacterOffence.Add(ref _offence, ref _addedToAttacks, ref _addedToSpells,
                    new ItemLine(stat, value, valueMax ?? value));
                break;
        }
    }

    /// <summary>
    /// A held keystone's rule. It counts as something added, since Blood Price and Unbroken carry no stat line; an unknown
    /// rule bends nothing.
    /// </summary>
    public void AddKeystone(KeystoneRule rule)
    {
        IsEmpty = false;
        switch (rule)
        {
            case KeystoneRule.LifeCost: LifeCost = true; break;
            case KeystoneRule.NoCrits: NoCrits = true; break;
            case KeystoneRule.WardTakesAll: WardTakesAll = true; break;
        }
    }

    private void AddMore(StatType stat, int pct)
    {
        if (OffenceStats.KeyOf(stat) is { } key)
        {
            _offence.AddMore(key, pct);
            return;
        }

        double factor = Math.Max(0d, 1d + (pct / 100d));
        if (stat == StatType.IncreasedMaximumLife) MoreLife *= factor;
        else if (stat == StatType.IncreasedMaximumMana) MoreMana *= factor;
    }
}
