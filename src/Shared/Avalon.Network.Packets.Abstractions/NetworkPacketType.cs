namespace Avalon.Network.Packets.Abstractions;

public enum NetworkPacketType : short
{
    ERROR = -1,
    UNKNOWN = 0,

    /**************************************************************************
     * Client Packets
     *************************************************************************/
    
    // Handshake
    CMSG_SERVER_INFO = 0x1000,
    CMSG_CLIENT_INFO = 0x1001,
    CMSG_CLIENT_HANDSHAKE = 0x1002,
    
    // Authentication
    
    CMSG_AUTH = 0x2000,
    CMSG_AUTH_PATCH = 0x2001,
    CMSG_LOGOUT = 0x2002,
    CMSG_REGISTER = 0x2003,
    
    // Character
    CMSG_CHARACTER_LIST = 0x2010,
    CMSG_CHARACTER_CREATE = 0x2011,
    CMSG_CHARACTER_DELETE = 0x2012,
    CMSG_CHARACTER_SELECTED = 0x2013,
    CMSG_CHARACTER_LOADED = 0x2014,
    
    // Map
    CMSG_MAP_TELEPORT = 0x2020,
    
    // Quest
    CMSG_QUEST_STATUS = 0x2040,
    CMSG_QUEST_LIST = 0x2041,
    CMSG_QUEST_QUERY_AVAILABLE = 0x2042,
    
    // World
    CMSG_INTERACT = 0x2030,
    
    CMSG_REQUEST_SERVER_VERSION = 0x2021,
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
    
    // Handshake
    SMSG_SERVER_INFO = 0x3000,
    SMSG_SERVER_HANDSHAKE = 0x3002,
    SMSG_SERVER_HANDSHAKE_RESULT = 0x3001,
    
    // Authentication
    SMSG_AUTH_RESULT = 0x3010,
    SMSG_LOGOUT = 0x3011,
    SMSG_REGISTER_RESULT = 0x3012,

    // Character
    SMSG_CHARACTER_CONNECTED = 0x3020,
    SMSG_CHARACTER_DISCONNECTED = 0x3021,
    SMSG_CHARACTER_LIST = 0x3023,
    SMSG_CHARACTER_CREATED = 0x3024,
    SMSG_CHARACTER_DELETED = 0x3025,
    SMSG_CHARACTER_SELECTED = 0x3026,
    
    // Map
    SMSG_MAP_TELEPORT = 0x3030,
    
    
    SMSG_PONG = 0x3006,
    SMSG_PING = 0x3007,
    
    SMSG_SERVER_VERSION = 0x3002,
    SMSG_PLAYER_POSITION_UPDATE = 0x3005,
    SMSG_NPC_UPDATE = 0x3008,
    SMSG_CHAT_MESSAGE = 0x3009,
    SMSG_CHAT_OPEN = 0x300A,
    SMSG_CHAT_CLOSE = 0x300B,

    SMSG_GROUP_INVITE = 0x300D,
    SMSG_GROUP_INVITE_RESULT = 0x300E,
}
