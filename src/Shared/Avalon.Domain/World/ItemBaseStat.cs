namespace Avalon.Domain.World;

/// <summary>
/// A stat that belongs to a base (#1179): its defence or its weapon numbers, authored per template as a range
/// (<see cref="ItemTemplateBaseStat" />) and rolled per copy (<see cref="ItemInstanceBaseStat" />). Not a
/// <see cref="StatType" />: a base stat is the item's own number, which its local modifiers scale, never a line a
/// character sums. Numbered from 0 so the wire's copy mirrors it member for member; append-only.
/// </summary>
public enum ItemBaseStat : byte
{
    /// <summary>Armour points.</summary>
    Armour = 0,

    /// <summary>Evasion rating.</summary>
    Evasion = 1,

    /// <summary>Ward points.</summary>
    Ward = 2,

    /// <summary>Chance to block, in percentage points.</summary>
    BlockChance = 3,

    /// <summary>A weapon's low end of damage, in points.</summary>
    DamageMin = 4,

    /// <summary>A weapon's high end of damage, in points.</summary>
    DamageMax = 5,

    /// <summary>Milliseconds between a weapon's basic attacks, before attack speed.</summary>
    AttackTime = 6,
}
