using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SLogoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOGOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public LogoutResult Result { get; set; }

    public static NetworkPacket Create(int accountId, LogoutResult result)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SLogoutPacket()
        {
            AccountId = accountId,
            Result = result
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

public enum LogoutResult : short
{
    Success,
    RecentlyInCombat,
    NotInGame,
    InternalError,
}
