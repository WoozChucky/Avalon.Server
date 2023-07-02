using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CAuthPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUTH;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Password { get; set; }

    public static NetworkPacket Create(string username, string password)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CAuthPacket()
        {
            Username = username,
            Password = password
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
