using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SExchangeWorldKeyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_EXCHANGE_WORLD_KEY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;

    [ProtoMember(1)] public byte[]? PublicKey { get; set; }

    public static NetworkPacket Create(byte[]? publicKey)
        => PacketSerializationHelper.SerializeUnencrypted(
            new SExchangeWorldKeyPacket { PublicKey = publicKey },
            PacketType, Flags, Protocol);
}
