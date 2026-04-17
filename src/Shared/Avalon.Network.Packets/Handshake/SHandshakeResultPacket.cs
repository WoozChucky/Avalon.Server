using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class SHandshakeResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_HANDSHAKE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public bool Verified { get; set; }

    public static NetworkPacket Create(bool verified, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SHandshakeResultPacket { Verified = verified },
            PacketType, Flags, Protocol, encryptFunc);
}
