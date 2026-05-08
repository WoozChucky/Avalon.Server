using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class ThreatEntry
{
    [ProtoMember(1)] public ulong AttackerGuid { get; set; }
    [ProtoMember(2)] public float ThreatPercent { get; set; }
}

[ProtoContract]
public class SThreatListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_THREAT_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong TargetGuid { get; set; }
    [ProtoMember(2)] public ThreatEntry[] Entries { get; set; } = System.Array.Empty<ThreatEntry>();

    public static NetworkPacket Create(ObjectGuid target, ThreatEntry[] entries, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SThreatListPacket { TargetGuid = target.RawValue, Entries = entries },
            PacketType, Flags, Protocol, encryptFunc);
}
