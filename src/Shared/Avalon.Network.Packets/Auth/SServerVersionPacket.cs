using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SServerVersionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_VERSION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int Major { get; set; }
    [ProtoMember(2)] public int Minor { get; set; }
    [ProtoMember(3)] public int Build { get; set; }
    [ProtoMember(4)] public int Rev { get; set; }
    
    public static NetworkPacket Create(int major, int minor, int build, int rev)
    {
        using var memoryStream = new MemoryStream();
        
        var versionPacket = new SServerVersionPacket()
        {
            Major = major,
            Minor = minor,
            Build = build,
            Rev = rev
        };
        
        Serializer.Serialize(memoryStream, versionPacket);
        
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
