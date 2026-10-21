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

    /// <summary>
    /// Nothing, because the target resists the crowd control it grants (#770): a creature whose resistance to it is 0
    /// (its template's override, else its rarity's), a character at the diminishing step that refuses it, or a creature
    /// walking home. Its caster is told "Immune"; it is not applied.
    /// </summary>
    Resisted,
}
