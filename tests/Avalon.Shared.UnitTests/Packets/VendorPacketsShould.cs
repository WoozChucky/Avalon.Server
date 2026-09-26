using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Vendor;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// The vendor packets (#432). The client encodes and decodes these by field number, so a
/// renumbered member breaks it silently. Each field is pinned alone with its bytes:
/// (number &lt;&lt; 3 | wire type), then the value. Zero is the default and writes nothing, so every
/// pinned value is non-zero, except where "absent" and "zero" must stay different.
/// </summary>
public class VendorPacketsShould
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
        Assert.Equal(0x2090, (short)NetworkPacketType.CMSG_VENDOR_BUY);
        Assert.Equal(0x2091, (short)NetworkPacketType.CMSG_VENDOR_SELL);
        Assert.Equal(0x2092, (short)NetworkPacketType.CMSG_VENDOR_BUYBACK);
        Assert.Equal(0x30A0, (short)NetworkPacketType.SMSG_VENDOR_LIST);
        Assert.Equal(0x30A1, (short)NetworkPacketType.SMSG_VENDOR_RESULT);
        Assert.Equal(NetworkPacketType.CMSG_VENDOR_BUY, CVendorBuyPacket.PacketType);
        Assert.Equal(NetworkPacketType.CMSG_VENDOR_SELL, CVendorSellPacket.PacketType);
        Assert.Equal(NetworkPacketType.CMSG_VENDOR_BUYBACK, CVendorBuybackPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_VENDOR_LIST, SVendorListPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_VENDOR_RESULT, SVendorResultPacket.PacketType);
    }

    [Fact]
    public void Keep_The_Buy_Field_Numbers_The_Client_Writes()
    {
        Assert.Equal("0801", Hex(new CVendorBuyPacket { RequestId = 1 }));
        Assert.Equal("1002", Hex(new CVendorBuyPacket { Sequence = 2 }));
        Assert.Equal("1803", Hex(new CVendorBuyPacket { Count = 3 }));
    }

    [Fact]
    public void Keep_The_Sell_Field_Numbers_The_Client_Writes()
    {
        Assert.Equal("0801", Hex(new CVendorSellPacket { RequestId = 1 }));
        Assert.Equal("1002", Hex(new CVendorSellPacket { BagSlot = 2 }));
        Assert.Equal("1804", Hex(new CVendorSellPacket { Count = 4 }));
    }

    [Fact]
    public void Keep_The_Buyback_Field_Numbers_The_Client_Writes()
    {
        Assert.Equal("0801", Hex(new CVendorBuybackPacket { RequestId = 1 }));
        Assert.Equal("1002", Hex(new CVendorBuybackPacket { Index = 2 }));
    }

    /// <summary>"One" and "none" must stay two different requests, and "the whole stack" and "none of it".</summary>
    [Fact]
    public void Tell_A_Missing_Count_From_A_Count_Of_Zero()
    {
        Assert.Null(RoundTrip(new CVendorBuyPacket { RequestId = 1 }).Count);
        Assert.Equal(0u, RoundTrip(new CVendorBuyPacket { RequestId = 1, Count = 0 }).Count);
        Assert.Null(RoundTrip(new CVendorSellPacket { RequestId = 1 }).Count);
        Assert.Equal(0u, RoundTrip(new CVendorSellPacket { RequestId = 1, Count = 0 }).Count);
    }

    [Fact]
    public void Keep_The_List_Field_Numbers_The_Client_Reads()
    {
        Assert.Equal("0805", Hex(new SVendorListPacket { VendorGuid = 5 }));
        // Field 2, length 2: one entry with Sequence 1.
        Assert.Equal("12020801", Hex(new SVendorListPacket { Entries = [new VendorEntryDto { Sequence = 1 }] }));
        // Field 3, length 2: one buyback entry with Index 1.
        Assert.Equal("1a020801", Hex(new SVendorListPacket { Buyback = [new VendorBuybackDto { Index = 1 }] }));
    }

    [Fact]
    public void Keep_The_Entry_Field_Numbers_The_Client_Reads()
    {
        Assert.Equal("0801", Hex(new VendorEntryDto { Sequence = 1 }));
        Assert.Equal("1009", Hex(new VendorEntryDto { ItemTemplateId = 9 }));
        Assert.Equal("1807", Hex(new VendorEntryDto { Price = 7 }));
        Assert.Equal("2004", Hex(new VendorEntryDto { Stock = 4 }));
        // Field 5, length 4: one cost of item 1, count 2.
        Assert.Equal("2a0408011002", Hex(new VendorEntryDto { Costs = [new VendorCostDto { ItemTemplateId = 1, Count = 2 }] }));
    }

    /// <summary>Absent stock means unlimited; a stock of 0 means sold out. The client must be able to tell.</summary>
    [Fact]
    public void Tell_Unlimited_Stock_From_Sold_Out()
    {
        Assert.Null(RoundTrip(new VendorEntryDto { Sequence = 1 }).Stock);
        Assert.Equal(0u, RoundTrip(new VendorEntryDto { Sequence = 1, Stock = 0 }).Stock);
        Assert.Equal("2000", Hex(new VendorEntryDto { Stock = 0 }));
    }

    [Fact]
    public void Keep_The_Buyback_Entry_Field_Numbers_The_Client_Reads()
    {
        Assert.Equal("0801", Hex(new VendorBuybackDto { Index = 1 }));
        Assert.Equal("1806", Hex(new VendorBuybackDto { Price = 6 }));
        VendorBuybackDto read = RoundTrip(new VendorBuybackDto
        {
            Index = 2, Price = 30, Item = new ItemSlotDto { Container = 1, Slot = 4, ItemTemplateId = 701, Count = 1, Durability = 42 },
        });
        Assert.Equal((2u, 30UL, 701UL, 42u), (read.Index, read.Price, read.Item!.ItemTemplateId, read.Item.Durability));
    }

    [Fact]
    public void Keep_The_Result_Field_Numbers_The_Client_Reads()
    {
        Assert.Equal("0801", Hex(new SVendorResultPacket { RequestId = 1 }));
        Assert.Equal("100c", Hex(new SVendorResultPacket { Result = VendorResult.Damaged }));
    }

    [Theory]
    [InlineData(VendorResult.Ok, 0)]
    [InlineData(VendorResult.ShopClosed, 1)]
    [InlineData(VendorResult.NotFound, 2)]
    [InlineData(VendorResult.OutOfStock, 3)]
    [InlineData(VendorResult.InvalidCount, 4)]
    [InlineData(VendorResult.NotEnoughGold, 5)]
    [InlineData(VendorResult.MissingItems, 6)]
    [InlineData(VendorResult.InventoryFull, 7)]
    [InlineData(VendorResult.UniqueAlreadyOwned, 8)]
    [InlineData(VendorResult.NotSellable, 9)]
    [InlineData(VendorResult.MoneyCapReached, 10)]
    [InlineData(VendorResult.Dead, 11)]
    [InlineData(VendorResult.Damaged, 12)]
    public void Number_Every_Result_As_The_Spec_Lists_It(VendorResult result, byte value)
    {
        Assert.Equal(value, (byte)result);
    }

    [Fact]
    public void Carry_A_Whole_List_Through_Its_Factory()
    {
        NetworkPacket packet = SVendorListPacket.Create(
            92,
            [
                new VendorEntryDto { Sequence = 1, ItemTemplateId = 700, Price = 10 },
                new VendorEntryDto
                {
                    Sequence = 3, ItemTemplateId = 702, Price = 30, Stock = 5,
                    Costs = [new VendorCostDto { ItemTemplateId = 700, Count = 2 }],
                },
            ],
            [new VendorBuybackDto { Index = 0, Price = 20, Item = new ItemSlotDto { Container = 1, ItemTemplateId = 700, Count = 5 } }],
            Plain);

        Assert.Equal(NetworkPacketType.SMSG_VENDOR_LIST, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        SVendorListPacket read = Serializer.Deserialize<SVendorListPacket>(stream);

        Assert.Equal(92UL, read.VendorGuid);
        Assert.Equal(2, read.Entries.Length);
        Assert.Null(read.Entries[0].Stock);
        Assert.Equal(5u, read.Entries[1].Stock);
        Assert.Equal((700UL, 2u), (read.Entries[1].Costs[0].ItemTemplateId, read.Entries[1].Costs[0].Count));
        Assert.Equal(5u, Assert.Single(read.Buyback).Item!.Count);
    }

    [Fact]
    public void Carry_A_Result_Through_Its_Factory()
    {
        NetworkPacket packet = SVendorResultPacket.Create(9, VendorResult.OutOfStock, Plain);

        Assert.Equal(NetworkPacketType.SMSG_VENDOR_RESULT, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        SVendorResultPacket read = Serializer.Deserialize<SVendorResultPacket>(stream);
        Assert.Equal((9u, VendorResult.OutOfStock), (read.RequestId, read.Result));
    }
}
