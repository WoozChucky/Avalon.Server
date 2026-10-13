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

    /// <summary>A packet whose contract protobuf-net cannot build: it has no way to write or read a thread.</summary>
    [ProtoContract]
    public sealed class UnbuildablePacket : Packet
    {
        public static readonly NetworkPacketType PacketType = NetworkPacketType.CMSG_PING;

        [ProtoMember(1)] public Thread? Member { get; set; }
    }

    /// <summary>
    /// protobuf-net reports a contract it cannot build on every decode, with the exception type it also throws for
    /// malformed bytes, which the read path takes for the peer's doing (#897). So the reader decodes once with every
    /// type as it is built, and refuses to be built with one it could never read: a server builds its reader before
    /// it opens its port, and does not start.
    /// </summary>
    [Fact]
    public void Refuse_to_be_built_with_a_packet_whose_contract_cannot_be_decoded()
    {
        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => MakeWith(typeof(UnbuildablePacket)));

        Assert.Contains(nameof(UnbuildablePacket), refused.Message, StringComparison.Ordinal);
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
