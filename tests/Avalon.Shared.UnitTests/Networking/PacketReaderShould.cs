using System.Collections;
using System.Reflection;
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

    /// <summary>
    /// A payload may nest only so deep (<see cref="PacketReader.MaxDecodeDepth" />, #897): groups opened inside one
    /// another in a field the packet does not have are valid bytes that protobuf-net skips by recursing, 512 deep
    /// unless told otherwise, and any peer can send them. Within the limit they are skipped as before.
    /// </summary>
    [Theory]
    [InlineData(PacketReader.MaxDecodeDepth / 2, true)]
    [InlineData(PacketReader.MaxDecodeDepth + 8, false)]
    public void Decode_a_payload_only_as_deep_as_the_limit(int groups, bool decodes)
    {
        PacketReader reader = MakeWith(typeof(CCharacterListPacket));
        // Field 15, which the packet does not have: a group opened that many times, then closed as many.
        byte[] payload = [.. Enumerable.Repeat((byte)0x7B, groups), .. Enumerable.Repeat((byte)0x7C, groups)];
        var frame = new InboundPacketFrame(new NetworkPacketHeader { Type = CCharacterListPacket.PacketType }, payload);

        if (decodes)
        {
            Assert.IsType<CCharacterListPacket>(reader.Read(frame));
            return;
        }

        PacketReadException refused = Assert.Throws<PacketReadException>(() => reader.Read(frame));
        Assert.Equal(PacketReadFailure.Undecodable, refused.Failure);
    }

    /// <summary>
    /// The decode depth is the model's, and one model reads and writes: a packet contract that nested past it could
    /// not be sent. Every contract, sent or received, stays within a quarter of the limit, so the limit is raised
    /// with the contracts and never met by one. A member that is a date, a time span, a GUID or a decimal is a
    /// message of its own on the wire, and so is a map's entry.
    /// </summary>
    [Fact]
    public void Keep_every_packet_contract_well_inside_the_decode_depth()
    {
        (Type Packet, int Depth)[] nesting = typeof(CCharacterListPacket).Assembly.GetExportedTypes()
            .Where(type => typeof(Packet).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (type, NestingOf(type, [])))
            .OrderByDescending(packet => packet.Item2)
            .ToArray();

        (Type deepest, int depth) = nesting[0];
        Assert.True(nesting.Length > 100, "The packet contracts were not found.");
        Assert.True(depth is >= 2 and <= PacketReader.MaxDecodeDepth / 4,
            $"{deepest.Name} nests {depth} deep; PacketReader.MaxDecodeDepth is {PacketReader.MaxDecodeDepth}.");
    }

    private static int NestingOf(Type message, HashSet<Type> above)
    {
        // A contract that contains itself has no depth to stay inside.
        if (!above.Add(message))
            return int.MaxValue / 2;

        int deepest = 0;
        foreach (MemberInfo member in message.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (member.GetCustomAttribute<ProtoMemberAttribute>() is null)
                continue;

            Type declared = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
            bool map = declared.IsGenericType && typeof(IDictionary).IsAssignableFrom(declared);
            foreach (Type part in map ? declared.GetGenericArguments() : [declared])
            {
                Type element = ElementOf(part);
                int below = element.GetCustomAttribute<ProtoContractAttribute>() is not null && !element.IsEnum
                    ? NestingOf(element, above)
                    : element == typeof(DateTime) || element == typeof(TimeSpan) || element == typeof(Guid) || element == typeof(decimal) ? 1 : 0;
                deepest = Math.Max(deepest, map ? below + 1 : below);
            }
        }

        above.Remove(message);
        return 1 + deepest;
    }

    private static Type ElementOf(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return underlying;
        if (type == typeof(string) || type == typeof(byte[]))
            return type;
        if (type.IsArray)
            return ElementOf(type.GetElementType()!);
        return type.IsGenericType && type.GetGenericArguments().Length == 1 && typeof(IEnumerable).IsAssignableFrom(type)
            ? ElementOf(type.GetGenericArguments()[0])
            : type;
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
