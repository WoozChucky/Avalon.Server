using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// Auras on the wire. Each field is pinned alone with its bytes: (number &lt;&lt; 3 | wire type), then the value. Zero
/// writes nothing, so every pinned value is non-zero.
/// </summary>
public class AuraPacketsShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    /// <summary>A tick names its aura on the damage and heal packets it reuses; absent on every other hit.</summary>
    [Fact]
    public void Name_the_aura_on_a_ticks_damage_and_heal()
    {
        Assert.Equal("3005", Hex(new SUnitDamagePacket { AuraId = 5 }));
        Assert.Equal("3805", Hex(new SCharacterDamagePacket { AuraId = 5 }));
        Assert.Equal("3805", Hex(new SUnitHealedPacket { AuraId = 5 }));
        Assert.Equal("", Hex(new SUnitDamagePacket()));
    }
    [Fact]
    public void Use_the_next_free_combat_opcodes()
    {
        Assert.Equal(0x2104, (short)NetworkPacketType.CMSG_AURA_CANCEL);
        Assert.Equal(0x310D, (short)NetworkPacketType.SMSG_AURA_UPDATE);
        Assert.Equal(0x310E, (short)NetworkPacketType.SMSG_AURA_LIST);
        Assert.Equal(0x310F, (short)NetworkPacketType.SMSG_AURA_CANCEL_RESULT);
        Assert.Equal(NetworkPacketType.CMSG_AURA_CANCEL, CAuraCancelPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_AURA_UPDATE, SAuraUpdatePacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_AURA_LIST, SAuraListPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_AURA_CANCEL_RESULT, SAuraCancelResultPacket.PacketType);
    }

    [Fact]
    public void Keep_the_entry_field_numbers_the_client_reads()
    {
        Assert.Equal("0801", Hex(new AuraEntryDto { AuraId = 1 }));
        Assert.Equal("1002", Hex(new AuraEntryDto { InstanceKey = 2 }));
        Assert.Equal("1803", Hex(new AuraEntryDto { CasterGuid = 3 }));
        Assert.Equal("2003", Hex(new AuraEntryDto { Stacks = 3 }));
        Assert.Equal("28dc0b", Hex(new AuraEntryDto { RemainingMs = 1500 }));
        Assert.Equal("30f02e", Hex(new AuraEntryDto { DurationMs = 6000 }));
        Assert.Equal("3804", Hex(new AuraEntryDto { Action = AuraUpdateAction.Removed }));
    }

    [Fact]
    public void Keep_the_update_and_list_field_numbers_the_client_reads()
    {
        Assert.Equal("0809", Hex(new SAuraUpdatePacket { UnitGuid = 9 }));
        Assert.Equal("12020801", Hex(new SAuraUpdatePacket { Entries = [new AuraEntryDto { AuraId = 1 }] }));
        Assert.Equal("0809", Hex(new SAuraListPacket { UnitGuid = 9 }));
        Assert.Equal("12020801", Hex(new SAuraListPacket { Entries = [new AuraEntryDto { AuraId = 1 }] }));
    }

    [Fact]
    public void Keep_the_cancel_field_numbers()
    {
        Assert.Equal("0805", Hex(new CAuraCancelPacket { AuraId = 5 }));
        Assert.Equal("0805", Hex(new SAuraCancelResultPacket { AuraId = 5 }));
        Assert.Equal("1003", Hex(new SAuraCancelResultPacket { Result = AuraCancelResult.NotCancellable }));
    }

    /// <summary>Append-only, starting at Unknown 0: a payload without the field reads as Unknown, never as an answer.</summary>
    [Fact]
    public void Keep_every_enum_number()
    {
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<AuraUpdateAction>().Select(v => (int)v));
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<AuraCancelResult>().Select(v => (int)v));
        Assert.Equal(4, (int)AuraUpdateAction.Removed);
        Assert.Equal(4, (int)AuraCancelResult.Dead);
    }
}
