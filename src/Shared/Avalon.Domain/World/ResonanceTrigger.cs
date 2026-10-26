namespace Avalon.Domain.World;

/// <summary>
/// The event a Resonance effect fires on (#1180): the wearer's direct hit landing, landing as a crit, killing a
/// creature; a direct hit landing on the wearer, the wearer evading one, or a hit taking the wearer's last ward.
/// </summary>
public enum ResonanceTrigger : byte { Hit = 1, Crit = 2, Kill = 3, Struck = 4, Evaded = 5, WardBroken = 6 }
