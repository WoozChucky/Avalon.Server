namespace Avalon.Network.Packets.Abstractions;

public enum NetworkPacketType : short
{
    ERROR = -1,
    UNKNOWN = 0,

    /**************************************************************************
     * Client Packets
     *************************************************************************/
    
    // Authentication
    
    CMSG_AUTH = 0x2000,
    CMSG_AUTH_PATCH = 0x2001,
    
    // Character
    
    CMSG_CHARACTER_LIST = 0x2010,
    CMSG_CHARACTER_CREATE = 0x2011,
    CMSG_CHARACTER_DELETE = 0x2012,
    CMSG_CHARACTER_SELECTED = 0x2013,
    
    CMSG_REQUEST_SERVER_VERSION = 0x2021,
    CMSG_REQUEST_ENCRYPTION_KEY = 0x2002,
    CMSG_REQUEST_LOBBY_LIST = 0x2003,
    CMSG_MOVEMENT = 0x2004,
    CMSG_PING = 0x2005,
    CMSG_PONG = 0x2006,
    CMSG_CHAT_MESSAGE = 0x2007,
    CMSG_CHAT_OPEN = 0x2008,
    CMSG_CHAT_CLOSE = 0x2009,
    CMSG_GROUP_INVITE_RESULT = 0x200A,
    
    /**************************************************************************
     * Server Packets
     *************************************************************************/
    
    // Authentication
    
    SMSG_AUTH_RESULT = 0x3000,

    // Character
    
    SMSG_CHARACTER_LIST = 0x3010,
    SMSG_CHARACTER_CREATED = 0x3011,
    SMSG_CHARACTER_DELETED = 0x3012,
    SMSG_CHARACTER_SELECTED = 0x3013,
    
    SMSG_PLAYER_DISCONNECTED = 0x3400,
    SMSG_PLAYER_CONNECTED = 0x3001,
    SMSG_PONG = 0x3006,
    SMSG_PING = 0x3007,
    
    SMSG_SERVER_VERSION = 0x3002,
    SMSG_ENCRYPTION_KEY = 0x3003,
    SMSG_LOBBY_LIST = 0x3004,
    SMSG_PLAYER_POSITION_UPDATE = 0x3005,
    SMSG_NPC_UPDATE = 0x3008,
    SMSG_CHAT_MESSAGE = 0x3009,
    SMSG_CHAT_OPEN = 0x300A,
    SMSG_CHAT_CLOSE = 0x300B,

    SMSG_GROUP_INVITE = 0x300D,
    SMSG_GROUP_INVITE_RESULT = 0x300E,
}
