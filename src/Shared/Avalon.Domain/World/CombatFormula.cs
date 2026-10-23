namespace Avalon.Domain.World;

/// <summary>
/// The constants every hit is resolved with (#506). Exactly one row, id 1, reloaded with the combat area.
/// </summary>
/// <remarks>
/// Armour reduces a hit by <c>min(A / (A + ArmorBase + ArmorPerLevel × attackerLevel), ArmorCap)</c>.
/// The caps are percentage points: a unit's crit and block chances are clamped to them, and the chance to evade to
/// <see cref="EvasionCap" />.
/// </remarks>
public class CombatFormula
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; }

    public float ArmorBase { get; set; }

    public float ArmorPerLevel { get; set; }

    /// <summary>The largest share of a hit armour can take away, 0 to 1.</summary>
    public float ArmorCap { get; set; }

    public float CritMultiplier { get; set; }

    public float BlockMultiplier { get; set; }

    public float CritCap { get; set; }

    public float BlockCap { get; set; }

    /// <summary>
    /// The most haste that counts, in percentage points (#627): a character's effective haste is its gear's
    /// AttackSpeed total, at most this. 0 or more.
    /// </summary>
    public float HasteCap { get; set; }

    /// <summary>The largest movement speed bonus, in percentage points of the base 4 m/s (#627).</summary>
    public float MoveSpeedCap { get; set; }

    /// <summary>
    /// The largest movement speed penalty, in percentage points of the base 4 m/s (#627): negative for a
    /// penalty, above -100 so no character is ever stopped dead, and at most <see cref="MoveSpeedCap" />.
    /// </summary>
    public float MoveSpeedFloor { get; set; }

    // Defence (#1178). Evasion avoids a hit with the chance min(E / (E + EvasionBase + EvasionPerLevel × attackerLevel),
    // EvasionCap / 100), armour's shape.

    public float EvasionBase { get; set; }

    public float EvasionPerLevel { get; set; }

    /// <summary>The largest chance to evade, in percentage points, 0 to 100.</summary>
    public float EvasionCap { get; set; }

    /// <summary>The most an element can be resisted before any maximum-resistance bonus, in percent, 0 to 90.</summary>
    public float ResistanceCap { get; set; }

    /// <summary>The lowest effective resistance, in percent, -100 to 0: -100 at most doubles an element.</summary>
    public float ResistanceFloor { get; set; }

    /// <summary>Armour each point of Strength gives, for every class. Finite and 0 or more.</summary>
    public float ArmorPerStrength { get; set; }

    /// <summary>Evasion each point of Agility gives, for every class. Finite and 0 or more.</summary>
    public float EvasionPerAgility { get; set; }

    /// <summary>Maximum ward each point of Intellect gives, for every class. Finite and 0 or more.</summary>
    public float WardPerIntellect { get; set; }

    // Offence (#1178): the attributes' class-neutral conversions, the base crit chance, the speed caps and the bare hand.

    /// <summary>Percent of increased physical attack damage each point of Strength gives. Finite and 0 or more.</summary>
    public float IncreasedPhysicalAttackPerStrength { get; set; }

    /// <summary>Percentage points of attack speed each point of Agility gives. Finite and 0 or more.</summary>
    public float AttackSpeedPerAgility { get; set; }

    /// <summary>Percent of increased spell damage each point of Intellect gives. Finite and 0 or more.</summary>
    public float IncreasedSpellPerIntellect { get; set; }

    /// <summary>Percent of increased healing each point of Intellect gives. Finite and 0 or more.</summary>
    public float IncreasedHealingPerIntellect { get; set; }

    /// <summary>Maximum health each point of Stamina gives, for every class.</summary>
    public uint LifePerStamina { get; set; }

    /// <summary>Every unit's crit chance before gear and auras, in percentage points, 0 to 100.</summary>
    public float BaseCritPct { get; set; }

    /// <summary>The most attack speed that counts, in percentage points. Finite and 0 or more.</summary>
    public float AttackSpeedCap { get; set; }

    /// <summary>The most cast speed that counts, in percentage points. Finite and 0 or more.</summary>
    public float CastSpeedCap { get; set; }

    /// <summary>The most cooldown recovery that counts, in percentage points. Finite and 0 or more.</summary>
    public float CooldownRecoveryCap { get; set; }

    /// <summary>The lowest an attack rolls with no weapon, inclusive; at most <see cref="UnarmedDamageMax" />.</summary>
    public uint UnarmedDamageMin { get; set; }

    /// <summary>The highest an attack rolls with no weapon, inclusive.</summary>
    public uint UnarmedDamageMax { get; set; }

    /// <summary>Milliseconds between basic attacks with no weapon, before attack speed; above 0.</summary>
    public uint UnarmedAttackTimeMs { get; set; }
}
