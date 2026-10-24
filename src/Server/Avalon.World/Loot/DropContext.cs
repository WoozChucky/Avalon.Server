using Avalon.World.Public.Enums;

namespace Avalon.World.Loot;

/// <summary>
/// What a roll of a made item reads from whatever made it (#1179): its item level, whose rarity odds it rolls by
/// (the creature's rarity; Normal for a chest or a barrel), and whether it is Magic outright (a quest reward), which
/// draws no rarity.
/// </summary>
public readonly record struct DropContext(ushort ItemLevel, CreatureRarity Odds, bool ForceMagic)
{
    /// <summary>
    /// The item level every copy the roll makes takes: at least 1, whatever its maker passed (a quest with no level
    /// requirement, a creature or a band at 0), so the roll is the one place it is clamped and no copy is made that its
    /// save would refuse (CK_ItemInstances_ItemLevel).
    /// </summary>
    public ushort ItemLevel { get; } = Math.Max((ushort)1, ItemLevel);
}
