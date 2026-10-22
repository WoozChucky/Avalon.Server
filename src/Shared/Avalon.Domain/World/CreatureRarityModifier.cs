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

    /// <summary>
    /// Scales the tier's armour, evasion and ward (#1178), finite and 0 or more. Resistances are percentages and are not
    /// scaled.
    /// </summary>
    public float DefenceMultiplier { get; set; } = 1f;

    /// <summary>
    /// Percent, 0 to 100, of a stun's duration the tier takes (#770): 0 is immune, 100 the full duration. A missing
    /// tier is unscaled, so the default is the full duration.
    /// </summary>
    public float StunDurationPct { get; set; } = 100f;

    /// <summary>Percent, 0 to 100, of a root's duration the tier takes, as <see cref="StunDurationPct" />.</summary>
    public float RootDurationPct { get; set; } = 100f;

    /// <summary>Percent, 0 to 100, of a silence's duration the tier takes, as <see cref="StunDurationPct" />.</summary>
    public float SilenceDurationPct { get; set; } = 100f;

    /// <summary>Percent, 0 to 100, of a slow's duration the tier takes, as <see cref="StunDurationPct" />.</summary>
    public float SlowDurationPct { get; set; } = 100f;
}
