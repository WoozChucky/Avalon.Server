using Avalon.World.Public.Enums;

namespace Avalon.World.Loot;

/// <summary>
/// What a roll of a made item reads from whatever made it (#1179): its item level (B-R21), whose rarity odds it rolls by
/// (the creature's rarity; Normal for a chest or a barrel), and whether it is Magic outright (a quest reward), which
/// draws no rarity.
/// </summary>
public readonly record struct DropContext(ushort ItemLevel, CreatureRarity Odds, bool ForceMagic);
