using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// Item use (2026-10-02). The client encodes and decodes these by field number, so each field is pinned alone with
/// its bytes: (number &lt;&lt; 3 | wire type), then the value. Zero writes nothing, so every pinned value is non-zero.
/// </summary>
public class ItemUsePacketsShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_the_next_free_item_opcodes()
    {
        Assert.Equal(0x2082, (short)NetworkPacketType.CMSG_ITEM_USE);
        Assert.Equal(0x3091, (short)NetworkPacketType.SMSG_ITEM_USE_RESULT);
        Assert.Equal(NetworkPacketType.CMSG_ITEM_USE, CItemUsePacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_ITEM_USE_RESULT, SItemUseResultPacket.PacketType);
    }

    [Fact]
    public void Keep_the_use_field_numbers_the_client_writes()
    {
        Assert.Equal("0801", Hex(new CItemUsePacket { RequestId = 1 }));
        Assert.Equal("1001", Hex(new CItemUsePacket { Container = 1 }));
        Assert.Equal("1803", Hex(new CItemUsePacket { Slot = 3 }));
    }

    [Fact]
    public void Keep_the_result_field_numbers_the_client_reads()
    {
        Assert.Equal("0801", Hex(new SItemUseResultPacket { RequestId = 1 }));
        Assert.Equal("100f", Hex(new SItemUseResultPacket { Result = ItemUseResult.Refused }));
        Assert.Equal("18dc0b", Hex(new SItemUseResultPacket { CooldownMs = 1500 }));
        Assert.Equal("22026e6f", Hex(new SItemUseResultPacket { Message = "no" }));
    }

    /// <summary>Append-only, starting at Unknown 0: a payload without the field reads as Unknown, never as Ok.</summary>
    [Fact]
    public void Keep_every_result_number()
    {
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            Enum.GetValues<ItemUseResult>().Select(r => (int)r));
        Assert.Equal(0, (int)ItemUseResult.Unknown);
        Assert.Equal(16, (int)ItemUseResult.InternalError);
    }

    /// <summary>owner decision 4: an item's cast bar names its item template beside the ability id, which is 0.</summary>
    [Fact]
    public void Name_the_item_on_its_cast_packets()
    {
        Assert.Equal("3003", Hex(new SUnitStartCastPacket { ItemTemplateId = 3 }));
        Assert.Equal("2003", Hex(new SUnitFinishCastPacket { ItemTemplateId = 3 }));
        Assert.Equal("2003", Hex(new SCharacterInterruptedCastPacket { ItemTemplateId = 3 }));
    }

    /// <summary>A teleport refused for no walkable ground (final owner decisions): appended, never renumbered.</summary>
    [Fact]
    public void Append_no_walkable_ground_to_the_map_transition_results()
    {
        Assert.Equal(7, (int)Avalon.Network.Packets.World.MapTransitionResult.NoWalkableGround);
        Assert.Equal(6, (int)Avalon.Network.Packets.World.MapTransitionResult.InstanceFull);
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new Avalon.Network.Packets.World.SMapTransitionPacket
        {
            Result = Avalon.Network.Packets.World.MapTransitionResult.NoWalkableGround,
        });
        stream.Position = 0;
        Assert.Equal(Avalon.Network.Packets.World.MapTransitionResult.NoWalkableGround,
            Serializer.Deserialize<Avalon.Network.Packets.World.SMapTransitionPacket>(stream).Result);
    }
}
