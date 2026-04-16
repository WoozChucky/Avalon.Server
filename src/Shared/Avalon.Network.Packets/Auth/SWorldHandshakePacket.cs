using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SWorldHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public long? AccountId { get; set; }
    [ProtoMember(2)] public bool Verified { get; set; }

    public static NetworkPacket Create(long accountId, bool verified, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SWorldHandshakePacket { AccountId = accountId, Verified = verified },
            PacketType, Flags, Protocol, encryptFunc);
}
