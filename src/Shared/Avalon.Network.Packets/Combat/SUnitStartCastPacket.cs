using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitStartCastPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_START_CAST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Caster { get; set; }
    [ProtoMember(2)] public float CastTime { get; set; }

    public static NetworkPacket Create(ObjectGuid caster, float castTime, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SUnitStartCastPacket
        {
            Caster = caster.RawValue,
            CastTime = castTime
        };

        Serializer.Serialize(memoryStream, p);

        var buffer = encryptFunc(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
