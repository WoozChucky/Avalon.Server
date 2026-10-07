using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class PacketReaderShould
{
    private static PacketReader Make(int bufferSize) =>
        new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration { PacketReaderBufferSize = bufferSize }),
            packetTypes: []);

    private static PacketReader MakeWith(params Type[] packetTypes) =>
        new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration()),
            packetTypes);

    [Fact]
    public void Take_its_buffer_size_from_configuration_with_a_default()
    {
        Assert.Equal(8192, Make(8192).BufferSize);
        Assert.Equal(4096, MakeWith().BufferSize);
    }

    [Fact]
    public void Should_ReturnDeserializedPacket_WhenTypeIsRegisteredAndPayloadIsValid()
    {
        PacketReader reader = MakeWith(typeof(CCharacterListPacket));

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            ms.ToArray().AsMemory());

        Packet? result = reader.Read(frame);

        Assert.IsType<CCharacterListPacket>(result);
    }

    [Fact]
    public void Should_ReturnNull_WhenPacketTypeIsUnknown()
    {
        PacketReader reader = MakeWith();

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            ReadOnlyMemory<byte>.Empty);

        Packet? result = reader.Read(frame);

        Assert.Null(result);
    }

    [Fact]
    public void Should_DecryptPayloadBeforeDeserializing_WhenDecryptFuncProvided()
    {
        PacketReader reader = MakeWith(typeof(CCharacterListPacket));

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());
        byte[] plaintext = ms.ToArray();

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            plaintext.AsMemory());

        // Passthrough: copies input to output unchanged — simulates decryption without real crypto
        DecryptFunc passthrough = (input, output) => { input.CopyTo(output); return input.Length; };

        Packet? result = reader.Read(frame, passthrough);

        Assert.IsType<CCharacterListPacket>(result);
    }
}
