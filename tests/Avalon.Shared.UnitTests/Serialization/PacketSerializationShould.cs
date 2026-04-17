using System;
using System.IO;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

public class PacketSerializationShould
{
    private static readonly DateTime TestDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ProducesDeserializablePayload()
    {
        EncryptFunc identity = span => span.ToArray();

        var packet = SChatMessagePacket.Create(
            accountId: 42UL,
            characterId: 7UL,
            characterName: "Alice",
            message: "Hello",
            dateTime: TestDate,
            encryptFunc: identity);

        using var ms = new MemoryStream(packet.Payload);
        var result = Serializer.Deserialize<SChatMessagePacket>(ms);

        Assert.Equal(42UL, result.AccountId);
        Assert.Equal(7UL, result.CharacterId);
        Assert.Equal("Alice", result.CharacterName);
        Assert.Equal("Hello", result.Message);
        Assert.Equal(TestDate, result.DateTime);
    }

    [Fact]
    public void Create_MultipleCallsProduceIndependentResults()
    {
        EncryptFunc identity = span => span.ToArray();

        var packet1 = SChatMessagePacket.Create(1UL, 2UL, "Alice", "Hello", TestDate, identity);
        var packet2 = SChatMessagePacket.Create(3UL, 4UL, "Bob", "World", TestDate, identity);

        using var ms1 = new MemoryStream(packet1.Payload);
        using var ms2 = new MemoryStream(packet2.Payload);
        var result1 = Serializer.Deserialize<SChatMessagePacket>(ms1);
        var result2 = Serializer.Deserialize<SChatMessagePacket>(ms2);

        Assert.Equal(1UL, result1.AccountId);
        Assert.Equal("Alice", result1.CharacterName);
        Assert.Equal(3UL, result2.AccountId);
        Assert.Equal("Bob", result2.CharacterName);
    }

    [Fact]
    public void Create_EncryptFuncReceivesCorrectSerializedBytes()
    {
        byte[]? capturedBytes = null;
        EncryptFunc capturing = span =>
        {
            capturedBytes = span.ToArray();
            return capturedBytes;
        };

        SChatMessagePacket.Create(99UL, 1UL, "Test", "Data", TestDate, capturing);

        Assert.NotNull(capturedBytes);
        using var ms = new MemoryStream(capturedBytes);
        var deserialized = Serializer.Deserialize<SChatMessagePacket>(ms);
        Assert.Equal(99UL, deserialized.AccountId);
        Assert.Equal("Test", deserialized.CharacterName);
    }
}
