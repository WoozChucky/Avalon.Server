using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// The inventory requests (#463). The client encodes and decodes these by field number, so a
/// renumbered member breaks it silently. Each field is pinned alone with its bytes:
/// (number &lt;&lt; 3 | wire type), then the value. Zero is the default and writes nothing, so every
/// pinned value is non-zero.
/// </summary>
public class ItemRequestPacketsShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    private static T RoundTrip<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }

    [Fact]
    public void Use_The_Next_Free_Opcodes()
    {
        Assert.Equal(0x2080, (short)NetworkPacketType.CMSG_ITEM_MOVE);
        Assert.Equal(0x2081, (short)NetworkPacketType.CMSG_ITEM_DESTROY);
        Assert.Equal(0x3090, (short)NetworkPacketType.SMSG_ITEM_RESULT);
        Assert.Equal(NetworkPacketType.CMSG_ITEM_MOVE, CItemMovePacket.PacketType);
        Assert.Equal(NetworkPacketType.CMSG_ITEM_DESTROY, CItemDestroyPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_ITEM_RESULT, SItemResultPacket.PacketType);
    }

    [Fact]
    public void Keep_The_Move_Field_Numbers_The_Client_Writes()
    {
        Assert.Equal("0801", Hex(new CItemMovePacket { RequestId = 1 }));
        Assert.Equal("1002", Hex(new CItemMovePacket { FromContainer = 2 }));
        Assert.Equal("1803", Hex(new CItemMovePacket { FromSlot = 3 }));
        Assert.Equal("2001", Hex(new CItemMovePacket { ToContainer = 1 }));
        Assert.Equal("2805", Hex(new CItemMovePacket { ToSlot = 5 }));
        Assert.Equal("3004", Hex(new CItemMovePacket { Count = 4 }));
    }

    [Fact]
    public void Keep_The_Destroy_Field_Numbers_The_Client_Writes()
    {
        Assert.Equal("0801", Hex(new CItemDestroyPacket { RequestId = 1 }));
        Assert.Equal("1002", Hex(new CItemDestroyPacket { Container = 2 }));
        Assert.Equal("1803", Hex(new CItemDestroyPacket { Slot = 3 }));
        Assert.Equal("2004", Hex(new CItemDestroyPacket { Count = 4 }));
    }

    /// <summary>"The whole stack" and "none of it" must stay two different requests.</summary>
    [Fact]
    public void Tell_A_Missing_Count_From_A_Count_Of_Zero()
    {
        Assert.Null(RoundTrip(new CItemMovePacket { RequestId = 1 }).Count);
        Assert.Equal(0u, RoundTrip(new CItemMovePacket { RequestId = 1, Count = 0 }).Count);
        Assert.Null(RoundTrip(new CItemDestroyPacket { RequestId = 1 }).Count);
        Assert.Equal(0u, RoundTrip(new CItemDestroyPacket { RequestId = 1, Count = 0 }).Count);
    }

    [Fact]
    public void Keep_The_Result_Field_Numbers_The_Client_Reads()
    {
        Assert.Equal("0801", Hex(new SItemResultPacket { RequestId = 1 }));
        Assert.Equal("100c", Hex(new SItemResultPacket { Result = ItemRequestResult.Blocked }));
        // Field 3, length 4: an InventorySlotUpdateDto with Container 1 and Slot 2 and no item.
        Assert.Equal("1a0408011002", Hex(new SItemResultPacket
        {
            Slots = [new InventorySlotUpdateDto { Container = 1, Slot = 2 }],
        }));
    }

    [Theory]
    [InlineData(ItemRequestResult.Ok, 0)]
    [InlineData(ItemRequestResult.NotFound, 1)]
    [InlineData(ItemRequestResult.InvalidSlot, 2)]
    [InlineData(ItemRequestResult.InvalidCount, 3)]
    [InlineData(ItemRequestResult.WrongEquipSlot, 4)]
    [InlineData(ItemRequestResult.LevelTooLow, 5)]
    [InlineData(ItemRequestResult.WrongClass, 6)]
    [InlineData(ItemRequestResult.NotStackable, 7)]
    [InlineData(ItemRequestResult.TargetFull, 8)]
    [InlineData(ItemRequestResult.CannotDestroy, 9)]
    [InlineData(ItemRequestResult.BankClosed, 10)]
    [InlineData(ItemRequestResult.Dead, 11)]
    [InlineData(ItemRequestResult.Blocked, 12)]
    public void Number_Every_Result_As_The_Spec_Lists_It(ItemRequestResult result, byte value)
    {
        Assert.Equal(value, (byte)result);
    }

    [Fact]
    public void Carry_A_Refusal_And_The_Slots_It_Named()
    {
        NetworkPacket packet = SItemResultPacket.Create(9, ItemRequestResult.TargetFull,
        [
            new InventorySlotUpdateDto
            {
                Container = 1, Slot = 0,
                Item = new ItemSlotDto { Container = 1, Slot = 0, ItemTemplateId = 100, Count = 20 },
            },
            new InventorySlotUpdateDto { Container = 1, Slot = 4 },
        ], Plain);

        Assert.Equal(NetworkPacketType.SMSG_ITEM_RESULT, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        SItemResultPacket read = Serializer.Deserialize<SItemResultPacket>(stream);

        Assert.Equal(9u, read.RequestId);
        Assert.Equal(ItemRequestResult.TargetFull, read.Result);
        Assert.Equal(2, read.Slots.Length);
        Assert.Equal(20u, read.Slots[0].Item!.Count);
        Assert.Null(read.Slots[1].Item);
    }

    [Fact]
    public void Read_An_Accepted_Result_As_Having_No_Slots()
    {
        NetworkPacket packet = SItemResultPacket.Create(3, ItemRequestResult.Ok, [], Plain);

        using var stream = new MemoryStream(packet.Payload);
        SItemResultPacket read = Serializer.Deserialize<SItemResultPacket>(stream);

        Assert.Equal(3u, read.RequestId);
        Assert.Equal(ItemRequestResult.Ok, read.Result);
        Assert.Empty(read.Slots);
    }
}
