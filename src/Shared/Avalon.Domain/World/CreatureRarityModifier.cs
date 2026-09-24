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
}
