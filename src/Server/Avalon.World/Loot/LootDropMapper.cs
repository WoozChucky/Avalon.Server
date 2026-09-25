using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.World;

namespace Avalon.World.Loot;

public static class LootDropMapper
{
    /// <summary>FreeForAllAt goes out as UTC ticks, the clock SPingPacket.ServerTimestamp carries.</summary>
    public static LootDropDto ToDto(GroundLoot drop) => new()
    {
        LootGuid = drop.Guid.RawValue,
        Position = Vector3Dto.From(drop.Position),
        ItemTemplateId = drop.ItemTemplateId?.Value,
        Count = drop.Count,
        Gold = drop.IsGold ? drop.Gold : null,
        OwnerCharacterId = drop.OwnerCharacterId,
        FreeForAllAt = drop.FreeForAllAt.Ticks,
    };
}
