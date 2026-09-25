using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Characters;

/// <summary>
/// One occupied inventory slot, flattened from the CharacterInventory row that places it and the
/// ItemInstance that gives it substance. Built only from Avalon.Common types, because
/// Avalon.World.Public does not reference the domain.
/// </summary>
public readonly record struct InventoryItem(
    ushort Slot,
    ItemInstanceId InstanceId,
    ItemTemplateId TemplateId,
    uint Count,
    uint Durability,
    ItemInstanceFlags Flags,
    uint Charges = 0);
