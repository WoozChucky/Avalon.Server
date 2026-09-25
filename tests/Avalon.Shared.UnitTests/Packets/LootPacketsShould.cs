using System;
using System.Collections.Generic;
using System.IO;
using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.World;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

public class LootPacketsShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    private static T Read<T>(NetworkPacket packet)
    {
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<T>(stream);
    }

    [Fact]
    public void Carry_An_Item_Drop_And_A_Gold_Pile_In_One_Spawn()
    {
        long freeForAllAt = new DateTime(2026, 9, 25, 12, 0, 30, DateTimeKind.Utc).Ticks;
        var drops = new List<LootDropDto>
        {
            new()
            {
                LootGuid = 0x0600000000000001, Position = new Vector3Dto { X = 1, Y = 2, Z = 3 },
                ItemTemplateId = 4, Count = 1, OwnerCharacterId = 7, FreeForAllAt = freeForAllAt
            },
            new() { LootGuid = 0x0600000000000002, Position = new Vector3Dto(), Gold = 25, FreeForAllAt = freeForAllAt },
        };

        NetworkPacket packet = SLootSpawnedPacket.Create(drops, Plain);
        SLootSpawnedPacket read = Read<SLootSpawnedPacket>(packet);

        Assert.Equal(NetworkPacketType.SMSG_LOOT_SPAWNED, packet.Header.Type);
        Assert.Equal(2, read.Drops.Count);
        Assert.Equal(4UL, read.Drops[0].ItemTemplateId);
        Assert.Null(read.Drops[0].Gold);
        Assert.Equal(7u, read.Drops[0].OwnerCharacterId);
        Assert.Equal(freeForAllAt, read.Drops[0].FreeForAllAt);
        Assert.Equal(3f, read.Drops[0].Position.Z);
        Assert.Null(read.Drops[1].ItemTemplateId);
        Assert.Equal(25UL, read.Drops[1].Gold);
        Assert.Null(read.Drops[1].OwnerCharacterId);
    }

    [Fact]
    public void Carry_Every_Despawned_Guid()
    {
        NetworkPacket packet = SLootDespawnedPacket.Create(
            [new ObjectGuid(ObjectType.Loot, 1), new ObjectGuid(ObjectType.Loot, 2)], Plain);

        Assert.Equal(NetworkPacketType.SMSG_LOOT_DESPAWNED, packet.Header.Type);
        Assert.Equal(
            [new ObjectGuid(ObjectType.Loot, 1).RawValue, new ObjectGuid(ObjectType.Loot, 2).RawValue],
            Read<SLootDespawnedPacket>(packet).LootGuids);
    }

    [Theory]
    [InlineData(LootPickupResult.Ok)]
    [InlineData(LootPickupResult.NotFound)]
    [InlineData(LootPickupResult.TooFar)]
    [InlineData(LootPickupResult.NotYours)]
    [InlineData(LootPickupResult.InventoryFull)]
    [InlineData(LootPickupResult.UniqueAlreadyOwned)]
    [InlineData(LootPickupResult.MoneyCapReached)]
    public void Carry_Every_Pickup_Result(LootPickupResult result)
    {
        NetworkPacket packet = SLootPickupResultPacket.Create(0x0600000000000009, result, Plain);
        SLootPickupResultPacket read = Read<SLootPickupResultPacket>(packet);

        Assert.Equal(NetworkPacketType.SMSG_LOOT_PICKUP_RESULT, packet.Header.Type);
        Assert.Equal(0x0600000000000009UL, read.LootGuid);
        Assert.Equal(result, read.Result);
    }
}
