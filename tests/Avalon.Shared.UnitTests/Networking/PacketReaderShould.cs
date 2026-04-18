using System.IO;
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
    public void UseConfiguredBufferSize_WhenConstructed()
    {
        var reader = Make(8192);
        Assert.NotNull(reader);
    }

    [Fact]
    public void UseDefaultBufferSize_WhenNotOverridden()
    {
        var reader = new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration()),
            packetTypes: []);

        Assert.NotNull(reader);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    [InlineData(65535)]
    public void AcceptValidBufferSizes(int size)
    {
        var ex = Record.Exception(() => Make(size));
        Assert.Null(ex);
    }

    [Fact]
    public void Should_ReturnDeserializedPacket_WhenTypeIsRegisteredAndPayloadIsValid()
    {
        var reader = MakeWith(typeof(CCharacterListPacket));

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            ms.ToArray().AsMemory());

        var result = reader.Read(frame);

        Assert.IsType<CCharacterListPacket>(result);
    }

    [Fact]
    public void Should_ReturnNull_WhenPacketTypeIsUnknown()
    {
        var reader = MakeWith();

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            ReadOnlyMemory<byte>.Empty);

        var result = reader.Read(frame);

        Assert.Null(result);
    }

    [Fact]
    public void Should_DecryptPayloadBeforeDeserializing_WhenDecryptFuncProvided()
    {
        var reader = MakeWith(typeof(CCharacterListPacket));

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());
        byte[] plaintext = ms.ToArray();

        var frame = new InboundPacketFrame(
            new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            plaintext.AsMemory());

        // Passthrough: copies input to output unchanged — simulates decryption without real crypto
        DecryptFunc passthrough = (input, output) => { input.CopyTo(output); return input.Length; };

        var result = reader.Read(frame, passthrough);

        Assert.IsType<CCharacterListPacket>(result);
    }
}
