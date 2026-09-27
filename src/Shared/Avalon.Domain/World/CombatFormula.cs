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
}
