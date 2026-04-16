using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Map;

[ProtoContract]
public class SMapTeleportPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MAP_TELEPORT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MapInfo Map { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }

    public static NetworkPacket Create(MapInfo mapInfo, float x, float y, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SMapTeleportPacket
        {
            Map = mapInfo,
            X = x,
            Y = y
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
