using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

public enum WorldSelectResult : byte
{
    Success = 0,
    DuplicateSession = 1
}

[ProtoContract]
public class SWorldSelectPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_SELECT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public byte[] WorldKey { get; set; }
    [ProtoMember(2)] public WorldSelectResult Result { get; set; }

    public static NetworkPacket Create(byte[] worldKey, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SWorldSelectPacket { WorldKey = worldKey, Result = WorldSelectResult.Success },
            PacketType, Flags, Protocol, encryptFunc);

    public static NetworkPacket CreateError(WorldSelectResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SWorldSelectPacket { WorldKey = Array.Empty<byte>(), Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}
