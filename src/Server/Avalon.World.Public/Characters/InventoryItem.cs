using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Characters;

/// <summary>
/// One occupied inventory slot, flattened from the CharacterInventory row that places it and the
/// ItemInstance that gives it substance. Built only from Avalon.Common types, because
/// Avalon.World.Public does not reference the domain. <see cref="Rarity" /> and <see cref="ItemLevel" /> are facts of
/// every copy, like its count and flags, and so are a rare's <see cref="RareName" />, the two words its roll gave it (#1179;
/// null on every other copy), and whether it is <see cref="Identified" /> (a magic, rare or unique copy that drops or comes
/// from a quest arrives unidentified), and whether it is <see cref="Tainted" /> (#1180: it bricked, and is never modified or
/// attuned again; it has no default, so every copy built states it, and none rebuilt untaints an item); its modifiers and
/// the hidden total its imprints added are not here, since they are privileged state this surface never reaches.
/// </summary>
public readonly record struct InventoryItem(
    byte Slot,
    ItemInstanceId InstanceId,
    ItemTemplateId TemplateId,
    uint Count,
    uint Durability,
    ItemInstanceFlags Flags,
    ItemRarity Rarity,
    ushort ItemLevel,
    string? RareName,
    bool Identified,
    bool Tainted,
    uint Charges = 0);
