using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// Change Character (#663). The client decodes by field number and enum value, so each is pinned
/// with its bytes: (number &lt;&lt; 3 | wire type), then the value.
/// </summary>
public class CharacterLeavePacketsShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_their_own_opcodes()
    {
        Assert.Equal(0x2016, (short)NetworkPacketType.CMSG_CHARACTER_LEAVE);
        Assert.Equal(0x302B, (short)NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT);
        Assert.Equal(NetworkPacketType.CMSG_CHARACTER_LEAVE, CCharacterLeavePacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT, SCharacterLeaveResultPacket.PacketType);
        Assert.Equal(NetworkPacketFlags.Encrypted, CCharacterLeavePacket.Flags);
        Assert.Equal(NetworkPacketFlags.Encrypted, SCharacterLeaveResultPacket.Flags);
    }

    [Fact]
    public void Send_an_empty_leave()
    {
        Assert.Equal("", Hex(new CCharacterLeavePacket()));
    }

    [Theory]
    [InlineData(CharacterLeaveResult.Left, "0801")]
    [InlineData(CharacterLeaveResult.NoCharacter, "0802")]
    [InlineData(CharacterLeaveResult.Selecting, "0803")]
    [InlineData(CharacterLeaveResult.AlreadyLeaving, "0804")]
    public void Carry_the_result_in_field_1(CharacterLeaveResult result, string hex)
    {
        Assert.Equal(hex, Hex(new SCharacterLeaveResultPacket { Result = result }));
    }

    [Fact]
    public void Close_a_failed_leave_with_its_own_disconnect_reason()
    {
        Assert.Equal(5, (int)DisconnectReason.CharacterSaveFailed);
    }
}
