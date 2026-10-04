using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

public enum GameAdmissionResult { Accepted = 0, InvalidRequest = 1, AuthorizationRequired = 2, ServiceUnavailable = 3, UnsupportedClient = 4 }
[ProtoContract]
public sealed class SGameAdmissionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GAME_ADMISSION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    [ProtoMember(1)] public byte[] PublicKey { get; set; } = [];
    [ProtoMember(2)] public GameAdmissionResult Result { get; set; }
    public static NetworkPacket Create(byte[] publicKey, GameAdmissionResult result = GameAdmissionResult.Accepted) => PacketSerializationHelper.SerializeUnencrypted(
        new SGameAdmissionPacket { PublicKey = publicKey, Result = result }, PacketType, Flags, Protocol);
}
