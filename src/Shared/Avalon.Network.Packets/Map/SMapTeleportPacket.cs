using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using ProtoBuf;

namespace Avalon.Network.Packets.Map;

[ProtoContract]
public class SMapTeleportPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MAP_TELEPORT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public MapInfo Map { get; set; }
    [ProtoMember(4)] public float X { get; set; }
    [ProtoMember(5)] public float Y { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, MapInfo mapInfo, float x, float y)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SMapTeleportPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            Map = mapInfo,
            X = x,
            Y = y
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
