using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Characters;

/// <summary>
/// One occupied inventory slot, flattened from the CharacterInventory row that places it and the
/// ItemInstance that gives it substance. Built only from Avalon.Common types, because
/// Avalon.World.Public does not reference the domain. <see cref="Rarity" /> and <see cref="ItemLevel" /> are facts of
/// every copy, like its count and flags, and so is a rare's <see cref="RareName" />, the two words its roll gave it (#1179;
/// null on every other copy); its modifiers are not here, since they are privileged state this surface never reaches.
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
    uint Charges = 0);
