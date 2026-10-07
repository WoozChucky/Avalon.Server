using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

public class PacketSerializationShould
{
    private static readonly DateTime s_testDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ProducesDeserializablePayload()
    {
        EncryptFunc identity = span => span.ToArray();

        NetworkPacket packet = SChatMessagePacket.Create(
            accountId: 42UL,
            characterId: 7UL,
            characterName: "Alice",
            message: "Hello",
            dateTime: s_testDate,
            encryptFunc: identity);

        using var ms = new MemoryStream(packet.Payload);
        SChatMessagePacket result = Serializer.Deserialize<SChatMessagePacket>(ms);

        Assert.Equal(42UL, result.AccountId);
        Assert.Equal(7UL, result.CharacterId);
        Assert.Equal("Alice", result.CharacterName);
        Assert.Equal("Hello", result.Message);
        Assert.Equal(s_testDate, result.DateTime);
    }

    [Fact]
    public void Create_MultipleCallsProduceIndependentResults()
    {
        EncryptFunc identity = span => span.ToArray();

        NetworkPacket packet1 = SChatMessagePacket.Create(1UL, 2UL, "Alice", "Hello", s_testDate, identity);
        NetworkPacket packet2 = SChatMessagePacket.Create(3UL, 4UL, "Bob", "World", s_testDate, identity);

        using var ms1 = new MemoryStream(packet1.Payload);
        using var ms2 = new MemoryStream(packet2.Payload);
        SChatMessagePacket result1 = Serializer.Deserialize<SChatMessagePacket>(ms1);
        SChatMessagePacket result2 = Serializer.Deserialize<SChatMessagePacket>(ms2);

        Assert.Equal(1UL, result1.AccountId);
        Assert.Equal("Alice", result1.CharacterName);
        Assert.Equal(3UL, result2.AccountId);
        Assert.Equal("Bob", result2.CharacterName);
    }
}
