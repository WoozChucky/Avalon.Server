using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerConnectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_CONNECTED;
    private const NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    
    public static NetworkPacket Create(ulong accountId, ulong characterId, string name, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SPlayerConnectedPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            Name = name
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
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
