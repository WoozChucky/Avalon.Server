namespace Avalon.Domain.World;

/// <summary>
/// The constants every hit is resolved with (#506). Exactly one row, id 1, reloaded with the combat area.
/// </summary>
/// <remarks>
/// Armour reduces a hit by <c>min(A / (A + ArmorBase + ArmorPerLevel × attackerLevel), ArmorCap)</c>.
/// The caps are percentage points: a unit's crit, dodge and block chances are clamped to them.
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

    public float DodgeCap { get; set; }

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
}
