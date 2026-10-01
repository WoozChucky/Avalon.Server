using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// SMSG_IGNORE_LIST (#723). The client decodes by field number, so each field is pinned alone with its bytes; every
/// pinned value is non-zero. A non-null string is always written, an empty one as a field of length zero.
/// </summary>
public class IgnoreListPacketShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_the_next_free_server_opcode()
    {
        Assert.Equal(0x30D0, (short)NetworkPacketType.SMSG_IGNORE_LIST);
        Assert.Equal(NetworkPacketType.SMSG_IGNORE_LIST, SIgnoreListPacket.PacketType);
    }

    [Fact]
    public void Keep_the_field_numbers()
    {
        Assert.Equal("0a02" + "1200", Hex(new SIgnoreListPacket { Characters = [new IgnoredCharacterDto()] }));
        Assert.Equal("0801" + "1200", Hex(new IgnoredCharacterDto { CharacterId = 1 }));
        Assert.Equal("120141", Hex(new IgnoredCharacterDto { Name = "A" }));
    }
}
