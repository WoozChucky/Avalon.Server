using ProtoBuf;

namespace Avalon.Network.Packets;

[ProtoContract]
public class NetworkPacket
{
    [ProtoMember(1)] public NetworkPacketHeader Header { get; set; }
    [ProtoMember(2)] public byte[] Payload { get; set; }
}

[ProtoContract]
public class NetworkPacketHeader
{
    [ProtoMember(1)] public NetworkPacketType Type { get; set; }
    [ProtoMember(2)] public NetworkPacketFlags Flags { get; set; }
    [ProtoMember(3)] public int Version { get; set; }
}

public enum NetworkPacketFlags
{
    None = 0,
    ClearText = 1,
    Handshake = 2,
    Encrypted = 3
}

public enum NetworkPacketType
{
    ERROR = -1,
    UNKNOWN = 0,
    
    CMSG_AUTH = 0x2000,
    CMSG_REQUEST_SERVER_VERSION = 0x2001,
    CMSG_REQUEST_ENCRYPTION_KEY = 0x2002,
    CMSG_REQUEST_LOBBY_LIST = 0x2003,
    CMSG_MOVEMENT = 0x2004,
    CMSG_JUMP = 0x2005,

    SMSG_AUTH_SUCCESS = 0x3000,
    SMSG_AUTH_FAILED = 0x3001,
    SMSG_SERVER_VERSION = 0x3002,
    SMSG_ENCRYPTION_KEY = 0x3003,
    SMSG_LOBBY_LIST = 0x3004,
    SMSG_MOVEMENT_UPDATE = 0x3005,
    SMSG_JUMP_UPDATE = 0x3006,
}
