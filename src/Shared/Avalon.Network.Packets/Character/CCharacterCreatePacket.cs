using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CCharacterCreatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_CREATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public string Name { get; set; }
    [ProtoMember(2)] public int Class { get; set; }

    public static NetworkPacket Create(string name, int @class, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CCharacterCreatePacket()
        {
            Name = name,
            Class = @class
        };
        
        Serializer.Serialize(memoryStream, p);
        
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
