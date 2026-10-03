namespace Avalon.World.Auras;

/// <summary>What applying an aura did.</summary>
public enum AuraApplyResult
{
    /// <summary>
    /// Nothing: the target is dead, ignores hits (a harmful aura), holds none, is at the cap, or the aura is not loaded
    /// or no longer fits the ability that applies it.
    /// </summary>
    Refused,
    Applied,
    Refreshed,
    Stacked,
}
