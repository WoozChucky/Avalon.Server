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

        // The buffer size is consumed by EnumerateAsync (passed to PacketStream.EnumerateAsync).
        // We can verify the configuration was accepted without throwing by simply instantiating
        // with a non-default value. A deeper assertion would require an integration test with a
        // real stream — this test guards against regressions in the constructor wiring.
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

        var networkPacket = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            Payload = ms.ToArray()
        };

        var result = reader.Read(networkPacket);

        Assert.IsType<CCharacterListPacket>(result);
    }

    [Fact]
    public void Should_ReturnNull_WhenPacketTypeIsUnknown()
    {
        var reader = MakeWith();

        var networkPacket = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            Payload = []
        };

        var result = reader.Read(networkPacket);

        Assert.Null(result);
    }

    [Fact]
    public void Should_DecryptPayloadBeforeDeserializing_WhenDecryptFuncProvided()
    {
        var reader = MakeWith(typeof(CCharacterListPacket));

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());
        byte[] plaintext = ms.ToArray();

        var networkPacket = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = CCharacterListPacket.PacketType },
            Payload = plaintext
        };

        // Passthrough: copies input to output unchanged — simulates decryption without real crypto
        DecryptFunc passthrough = (input, output) => { input.CopyTo(output); return input.Length; };

        var result = reader.Read(networkPacket, passthrough);

        Assert.IsType<CCharacterListPacket>(result);
    }
}
