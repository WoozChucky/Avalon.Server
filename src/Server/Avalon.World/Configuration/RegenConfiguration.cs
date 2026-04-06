using System.ComponentModel.DataAnnotations;

namespace Avalon.World.Configuration;

public class RegenConfiguration
{
    /// <summary>Seconds after the last hit received before a character is considered out of combat.</summary>
    [Range(0.1, 300.0)]
    public float CombatLeaveDelaySeconds { get; set; } = 5f;

    /// <summary>HP regenerated per point of Stamina per second when out of combat.</summary>
    [Range(0.0, 1000.0)]
    public float HealthRegenOutOfCombatPerStamina { get; set; } = 0.5f;

    /// <summary>
    /// Power regenerated per point of the governing stat (Intellect for Mana, Agility for Energy)
    /// per second when out of combat and not recently casting.
    /// </summary>
    [Range(0.0, 1000.0)]
    public float PowerRegenOutOfCombatPerStat { get; set; } = 0.3f;

    /// <summary>Same stat coefficient applied while in combat (and not recently casting).</summary>
    [Range(0.0, 1000.0)]
    public float PowerRegenInCombatPerStat { get; set; } = 0.05f;

    /// <summary>
    /// Seconds after the last spell cast before power regeneration resumes (the "5-second rule").
    /// Power regen is suppressed during this window.
    /// </summary>
    [Range(0.0, 60.0)]
    public float PowerRegenCastSuppressSeconds { get; set; } = 5f;
}
