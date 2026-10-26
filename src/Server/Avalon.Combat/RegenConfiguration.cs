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

    /// <summary>Percent of maximum mana regenerated a second while in combat (#1181); 0 turns it off.</summary>
    [Range(0.0, 100.0)]
    public float ManaRegenPctInCombat { get; set; } = 1f;

    /// <summary>Percent of maximum mana regenerated a second out of combat (#1181); 0 turns it off.</summary>
    [Range(0.0, 100.0)]
    public float ManaRegenPctOutOfCombat { get; set; } = 4f;

    /// <summary>Seconds without any damage taken before a ward starts to recharge (#1178).</summary>
    [Range(0.0, 60.0)]
    public float WardRechargeDelaySeconds { get; set; } = 4f;

    /// <summary>Seconds a recharging ward takes to go from empty to full (#1178); 0 fills it at once.</summary>
    [Range(0.0, 60.0)]
    public float WardRechargeSeconds { get; set; } = 2f;
}
