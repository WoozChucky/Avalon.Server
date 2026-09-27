using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

/// <summary>
/// Stat multipliers for one rarity tier. A table rather than a switch so a tier is retuned as data.
/// </summary>
public class CreatureRarityModifier
{
    public CreatureRarity Rarity { get; set; }
    public float HealthMultiplier { get; set; }
    public float DamageMultiplier { get; set; }
    public float ExperienceMultiplier { get; set; }

    /// <summary>Chance to crit, in percentage points (#506). Not a multiplier: the tier's own chance.</summary>
    public float CritPct { get; set; }

    /// <summary>Chance to dodge a hit, in percentage points (#506).</summary>
    public float DodgePct { get; set; }

    /// <summary>Chance to block a hit, in percentage points (#506).</summary>
    public float BlockPct { get; set; }
}
