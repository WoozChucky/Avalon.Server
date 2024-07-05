using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Map;

[ProtoContract]
public class CMapTeleportPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MAP_TELEPORT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public int MapId { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, int mapId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CMapTeleportPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            MapId = mapId
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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
