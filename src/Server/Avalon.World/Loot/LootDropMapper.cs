using Avalon.Network.Packets.Loot;

namespace Avalon.World.Loot;

public static class LootDropMapper
{
    /// <summary>FreeForAllAt goes out as UTC ticks, the clock SPingPacket.ServerTimestamp carries.</summary>
    public static LootDropDto ToDto(GroundLoot drop) => Fill(new LootDropDto(), drop);

    /// <summary>
    /// Writes <paramref name="drop" /> over <paramref name="dto" />, every member of it, so a message kept from an
    /// earlier drop (a kill refills the ones its instance keeps, #890) carries nothing of that drop.
    /// </summary>
    public static LootDropDto Fill(LootDropDto dto, GroundLoot drop)
    {
        dto.LootGuid = drop.Guid.RawValue;
        dto.Position.X = drop.Position.x;
        dto.Position.Y = drop.Position.y;
        dto.Position.Z = drop.Position.z;
        dto.ItemTemplateId = drop.ItemTemplateId?.Value;
        dto.Count = drop.Count;
        dto.Gold = drop.IsGold ? drop.Gold : null;
        dto.OwnerCharacterId = drop.OwnerCharacterId;
        dto.FreeForAllAt = drop.FreeForAllAt.Ticks;
        return dto;
    }
}
