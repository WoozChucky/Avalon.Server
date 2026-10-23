using System.ComponentModel.DataAnnotations;

namespace Avalon.Combat;

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

    /// <summary>Seconds without any damage taken before a ward starts to recharge (#1178).</summary>
    [Range(0.0, 60.0)]
    public float WardRechargeDelaySeconds { get; set; } = 4f;

    /// <summary>Seconds a recharging ward takes to go from empty to full (#1178); 0 fills it at once.</summary>
    [Range(0.0, 60.0)]
    public float WardRechargeSeconds { get; set; } = 2f;
}
