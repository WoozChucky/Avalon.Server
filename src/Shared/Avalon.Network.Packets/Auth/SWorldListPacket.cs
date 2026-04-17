using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SWorldListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public WorldInfo[] Worlds { get; set; }

    public static NetworkPacket Create(WorldInfo[] worlds, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SWorldListPacket { Worlds = worlds },
            PacketType, Flags, Protocol, encryptFunc);
}


[ProtoContract]
public class WorldInfo
{
    [ProtoMember(1)] public ushort Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public short Type { get; set; }
    [ProtoMember(4)] public short AccessLevelRequired { get; set; }
    [ProtoMember(5)] public string Host { get; set; }
    [ProtoMember(6)] public int Port { get; set; }
    [ProtoMember(7)] public string MinVersion { get; set; }
    [ProtoMember(8)] public string Version { get; set; }
    [ProtoMember(9)] public short Status { get; set; }
}
