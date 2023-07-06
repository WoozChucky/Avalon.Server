using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Map;

[ProtoContract]
public class CMapTeleportPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MAP_TELEPORT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public int MapId { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, int mapId)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CMapTeleportPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            MapId = mapId
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
