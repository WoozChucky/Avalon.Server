using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

/// <summary>
/// How one class turns its attributes into health, power and damage, and the block, dodge and crit it
/// starts with (#506). One row per class, reloaded with the combat area; the stats calculator reads it.
/// The types are the ones the calculator's constants had, so the seed reproduces them bit for bit.
/// </summary>
public class ClassStatFactors
{
    public CharacterClass Class { get; set; }

    /// <summary>Health per point of Stamina.</summary>
    public uint HpPerStamina { get; set; }

    public double PowerPerIntellect { get; set; }

    public double PowerPerAgility { get; set; }

    /// <summary>A pool that is always this size whatever the row and gear say (Fury); null for a pool that grows.</summary>
    public uint? FixedPower { get; set; }

    public double AttackPerStrength { get; set; }

    public double AttackPerAgility { get; set; }

    public double AbilityPerIntellect { get; set; }

    /// <summary>Percentage points, before gear.</summary>
    public float BaseBlock { get; set; }

    /// <summary>Percentage points, before gear.</summary>
    public float BaseDodge { get; set; }

    /// <summary>Percentage points, before gear.</summary>
    public float BaseCrit { get; set; }
}
