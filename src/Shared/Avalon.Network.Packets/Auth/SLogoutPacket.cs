using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SLogoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOGOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public LogoutResult Result { get; set; }

    public static NetworkPacket Create(LogoutResult result, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SLogoutPacket()
        {
            Result = result
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

public enum LogoutResult : short
{
    Success,
    RecentlyInCombat,
    NotInGame,
    NotSameAccount,
    InternalError,
    ConnectedElsewhere
}
