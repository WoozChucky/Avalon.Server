using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SOpenChatPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_OPEN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string ClientId { get; set; }

    public static NetworkPacket Create(string clientId, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SOpenChatPacket { ClientId = clientId },
            PacketType, Flags, Protocol, encryptFunc);
}
