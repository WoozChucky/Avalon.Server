using Avalon.Network.Packets.Character;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>One item on the wire, the same for the login snapshot and the per-tick update.</summary>
public static class ItemSlotDtoMapper
{
    public static ItemSlotDto ToDto(InventoryType container, InventoryItem item) => new()
    {
        Container = (ushort)container,
        Slot = item.Slot,
        ItemTemplateId = item.TemplateId.Value,
        ItemInstanceId = item.InstanceId.Value,
        Count = item.Count,
        Durability = item.Durability,
        Flags = (uint)item.Flags,
    };
}
