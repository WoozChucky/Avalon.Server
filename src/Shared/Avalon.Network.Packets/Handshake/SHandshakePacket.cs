using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class SHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public byte[] HandshakeData { get; set; }

    public static NetworkPacket Create(byte[] handshakeData, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SHandshakePacket { HandshakeData = handshakeData },
            PacketType, Flags, Protocol, encryptFunc);
}
