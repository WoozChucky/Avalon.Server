using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Talents;

/// <summary>
/// Client to server (#1181): allocate talent nodes, all or nothing. The nodes are taken in the order sent, so a node may
/// connect through one named before it. Answered with exactly one STalentResultPacket; an accepted request is followed by
/// an STalentStatePacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TALENT_ALLOCATE)]
public sealed class CTalentAllocatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TALENT_ALLOCATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The nodes to allocate, in the order they connect. An empty list is Ok and changes nothing.</summary>
    [ProtoMember(2)] public List<uint> NodeIds { get; set; } = [];
}
