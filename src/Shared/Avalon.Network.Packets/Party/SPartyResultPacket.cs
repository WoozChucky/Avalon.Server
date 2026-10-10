using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>
/// One per party request, to the requester; also sent unasked to an inviter whose invite was declined or
/// expired, and to an invitee whose invite expired. Name is the other player the answer is about, if any.
/// </summary>
[ProtoContract]
public class SPartyResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PARTY_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public PartyResult Result { get; set; }
    [ProtoMember(2)] public string? Name { get; set; }

    public static OutboundPacket Create(PartyResult result, string? name, PacketEncoder encoder)
    {
        SPartyResultPacket message = PacketEncoder.Scratch<SPartyResultPacket>();
        message.Result = result;
        message.Name = name;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
