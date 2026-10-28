using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Talents;

/// <summary>
/// Client to server (#1181 Part C): refund talent nodes at the Lectern of Paths, all or nothing. Free up to the free
/// level, then priced per node by the character's level (the price the talent state carries). Answered with exactly one
/// STalentResultPacket; an accepted request is followed by an STalentStatePacket at the end of the tick.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TALENT_REFUND)]
public sealed class CTalentRefundPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TALENT_REFUND;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The Lectern's guid, as its doodad spawn carried it; checked with its range on every request.</summary>
    [ProtoMember(2)] public ulong LecternGuid { get; set; }

    /// <summary>
    /// The nodes to refund, in any order. Every node still held afterwards must reach the class's start. An empty list is
    /// Ok and changes nothing.
    /// </summary>
    [ProtoMember(3)] public List<uint> NodeIds { get; set; } = [];
}
