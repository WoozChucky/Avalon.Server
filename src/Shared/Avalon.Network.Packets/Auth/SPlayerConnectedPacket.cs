using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerConnectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_CONNECTED;
    private const NetworkProtocol Protocol = NetworkProtocol.Tcp;
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    
    public static NetworkPacket Create(int accountId, int characterId, string name)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SPlayerConnectedPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            Name = name
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
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
