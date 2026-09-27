using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// The world select answer (#554). The client decodes the result by its number, so the values are
/// append-only and pinned here, and a value it does not know must decode without throwing.
/// </summary>
public class WorldSelectPacketShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    [Theory]
    [InlineData(WorldSelectResult.Success, 0)]
    [InlineData(WorldSelectResult.DuplicateSession, 1)]
    [InlineData(WorldSelectResult.WorldUnavailable, 2)]
    public void Number_Every_Result_Append_Only(WorldSelectResult result, byte value)
    {
        Assert.Equal(value, (byte)result);
    }

    [Fact]
    public void Carry_WorldUnavailable_Through_Its_Factory()
    {
        NetworkPacket packet = SWorldSelectPacket.CreateError(WorldSelectResult.WorldUnavailable, Plain);

        Assert.Equal(NetworkPacketType.SMSG_WORLD_SELECT, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        SWorldSelectPacket read = Serializer.Deserialize<SWorldSelectPacket>(stream);
        Assert.Equal(WorldSelectResult.WorldUnavailable, read.Result);
        Assert.Empty(read.WorldKey);
    }

    [Fact]
    public void Keep_The_Result_Field_Number_The_Client_Reads()
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new SWorldSelectPacket { WorldKey = null!, Result = WorldSelectResult.WorldUnavailable });
        Assert.Equal("1002", Convert.ToHexString(stream.ToArray()));
    }

    /// <summary>A result this build does not know (field 2 = 200) decodes as its number, never a throw.</summary>
    [Fact]
    public void Decode_A_Result_It_Does_Not_Know_Without_Throwing()
    {
        using var stream = new MemoryStream(Convert.FromHexString("10C801"));
        SWorldSelectPacket read = Serializer.Deserialize<SWorldSelectPacket>(stream);

        Assert.Equal((WorldSelectResult)200, read.Result);
        Assert.False(Enum.IsDefined(read.Result));
    }
}
