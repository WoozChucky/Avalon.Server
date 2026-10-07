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
        Assert.Equal(3, (int)ChatChannel.Whisper);
    }

    /// <summary>#717: a whisper is channel 3, and the sender's echo names the recipient in field 7.</summary>
    [Fact]
    public void Carry_a_whisper_echo_target_as_field_7()
    {
        Assert.Equal(DefaultDateTime + "3003", Hex(new SChatMessagePacket { Channel = ChatChannel.Whisper }));
        Assert.Equal(DefaultDateTime + "3003" + "3a054b61656c61",
            Hex(new SChatMessagePacket { Channel = ChatChannel.Whisper, TargetName = "Kaela" }));
    }

    [Fact]
    public void Read_a_payload_without_a_target_as_none()
    {
        using var stream = new MemoryStream(Convert.FromHexString(DefaultDateTime + "3001"));
        SChatMessagePacket read = Serializer.Deserialize<SChatMessagePacket>(stream);

        Assert.Equal(ChatChannel.Party, read.Channel);
        Assert.Null(read.TargetName);
    }

    /// <summary>#763: the sender's class is field 8, and 0 (not a character) is not written.</summary>
    [Fact]
    public void Carry_the_sender_class_as_field_8()
    {
        Assert.Equal(DefaultDateTime + "4003", Hex(new SChatMessagePacket { CharacterClass = 3 }));
        Assert.Equal(DefaultDateTime, Hex(new SChatMessagePacket { CharacterClass = 0 }));
    }

    [Fact]
    public void Read_a_payload_without_a_class_as_0()
    {
        using var stream = new MemoryStream(Convert.FromHexString(DefaultDateTime + "3001"));
        SChatMessagePacket read = Serializer.Deserialize<SChatMessagePacket>(stream);

        Assert.Equal((ushort)0, read.CharacterClass);
    }
}
