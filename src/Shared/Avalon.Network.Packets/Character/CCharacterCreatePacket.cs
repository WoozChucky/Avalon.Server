using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class CCharacterCreatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_CREATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public int Class { get; set; }

    public static NetworkPacket Create(int accountId, string name, int @class, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CCharacterCreatePacket()
        {
            AccountId = accountId,
            Name = name,
            Class = @class
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
        var buffer = encrypt(memoryStream.ToArray());
        
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
