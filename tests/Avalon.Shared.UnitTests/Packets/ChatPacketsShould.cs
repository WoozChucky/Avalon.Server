using System;
using System.IO;
using Avalon.Network.Packets.Social;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>The chat channel (party play, 2026-09-30): appended as field 6, and Say is 0 so an old payload reads as Say.</summary>
public class ChatPacketsShould
{
    /// <summary>Field 5, the default DateTime, which protobuf-net writes even when unset.</summary>
    private const string DefaultDateTime = "2a040801100f";

    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Carry_the_channel_as_field_6()
    {
        Assert.Equal(DefaultDateTime + "3001", Hex(new SChatMessagePacket { Channel = ChatChannel.Party }));
        Assert.Equal(DefaultDateTime + "3002", Hex(new SChatMessagePacket { Channel = ChatChannel.System }));
    }

    [Fact]
    public void Write_nothing_for_say() => Assert.Equal(DefaultDateTime, Hex(new SChatMessagePacket { Channel = ChatChannel.Say }));

    [Fact]
    public void Keep_the_channel_values()
    {
        Assert.Equal(0, (int)ChatChannel.Say);
        Assert.Equal(1, (int)ChatChannel.Party);
        Assert.Equal(2, (int)ChatChannel.System);
    }
}
