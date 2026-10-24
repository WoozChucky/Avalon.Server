namespace Avalon.Network.Packets.Items;

/// <summary>
/// A stat that belongs to a base (#1179), member for member the server's own base-stat numbers (PublicMappingShould holds
/// the two in step); append-only.
/// </summary>
public enum BaseStat
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
