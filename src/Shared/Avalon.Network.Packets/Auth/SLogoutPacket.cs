using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SLogoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOGOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public LogoutResult Result { get; set; }

    public static NetworkPacket Create(LogoutResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SLogoutPacket { Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}

public enum LogoutResult : short
{
    Success,
    RecentlyInCombat,
    NotInGame,
    NotSameAccount,
    InternalError,
    ConnectedElsewhere
}
