#include "Opcodes.h"

#include <Common/Logging/Log.h>
#include "../Packets/AllPackets.h"
#include "../WorldSession.h"
#include <iomanip>
#include <sstream>

template<class PacketClass, void(WorldSession::* HandlerFunction)(PacketClass&)>
class PacketHandler : public ClientOpcodeHandler
{
public:
    PacketHandler(char const* name, SessionStatus status, PacketProcessing processing) : ClientOpcodeHandler(name, status, processing) { }

    void Call(WorldSession* session, WorldPacket& packet) const override
    {
        PacketClass nicePacket(std::move(packet));
        nicePacket.Read();
        (session->*HandlerFunction)(nicePacket);
    }
};

template<void(WorldSession::* HandlerFunction)(WorldPacket&)>
class PacketHandler<WorldPacket, HandlerFunction> : public ClientOpcodeHandler
{
public:
    PacketHandler(char const* name, SessionStatus status, PacketProcessing processing) : ClientOpcodeHandler(name, status, processing) { }

    void Call(WorldSession* session, WorldPacket& packet) const override
    {
        (session->*HandlerFunction)(packet);
    }
};

OpcodeTable opcodeTable;

template<typename T>
struct get_packet_class
{
};

template<typename PacketClass>
struct get_packet_class<void(WorldSession::*)(PacketClass&)>
{
    using type = PacketClass;
};

OpcodeTable::OpcodeTable()
{
    memset(_internalTableClient, 0, sizeof(_internalTableClient));
}

OpcodeTable::~OpcodeTable()
{
    for (U16 i = 0; i < NUM_OPCODE_HANDLERS; ++i)
        delete _internalTableClient[i];
}

template<typename Handler, Handler HandlerFunction>
void OpcodeTable::ValidateAndSetClientOpcode(OpcodeClient opcode, char const* name, SessionStatus status, PacketProcessing processing)
{
    if (U32(opcode) == NULL_OPCODE)
    {
        LOG_ERROR("network", "Opcode {} does not have a value", name);
        return;
    }

    if (U32(opcode) >= NUM_OPCODE_HANDLERS)
    {
        LOG_ERROR("network", "Tried to set handler for an invalid opcode {}", U32(opcode));
        return;
    }

    if (_internalTableClient[opcode] != nullptr)
    {
        LOG_ERROR("network", "Tried to override client handler of {} with {} (opcode {})", opcodeTable[opcode]->Name, name, U32(opcode));
        return;
    }

    _internalTableClient[opcode] = new PacketHandler<typename get_packet_class<Handler>::type, HandlerFunction>(name, status, processing);
}

void OpcodeTable::ValidateAndSetServerOpcode(OpcodeServer opcode, char const* name, SessionStatus status)
{
    if (U32(opcode) == NULL_OPCODE)
    {
        LOG_ERROR("network", "Opcode {} does not have a value", name);
        return;
    }

    if (U32(opcode) >= NUM_OPCODE_HANDLERS)
    {
        LOG_ERROR("network", "Tried to set handler for an invalid opcode {}", U32(opcode));
        return;
    }

    if (_internalTableClient[opcode] != nullptr)
    {
        LOG_ERROR("network", "Tried to override server handler of {} with {} (opcode {})", opcodeTable[opcode]->Name, name, U32(opcode));
        return;
    }

    _internalTableClient[opcode] = new PacketHandler<WorldPacket, &WorldSession::Handle_ServerSide>(name, status, PROCESS_INPLACE);
}

/// Correspondence between opcodes and their names
void OpcodeTable::Initialize()
{
#define DEFINE_HANDLER(opcode, status, processing, handler) \
    ValidateAndSetClientOpcode<decltype(handler), handler>(opcode, #opcode, status, processing)

#define DEFINE_SERVER_OPCODE_HANDLER(opcode, status) \
    static_assert(status == STATUS_NEVER || status == STATUS_UNHANDLED, "Invalid status for server opcode"); \
    ValidateAndSetServerOpcode(opcode, #opcode, status)

    /*0x001*/ DEFINE_HANDLER(CMSG_BOOTME,                                                           STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x002*/ DEFINE_HANDLER(CMSG_DBLOOKUP,                                                         STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x003*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_DBLOOKUP,                                           STATUS_NEVER);
    /*0x004*/ DEFINE_HANDLER(CMSG_QUERY_OBJECT_POSITION,                                            STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x005*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_QUERY_OBJECT_POSITION,                              STATUS_NEVER);
    /*0x006*/ DEFINE_HANDLER(CMSG_QUERY_OBJECT_ROTATION,                                            STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x007*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_QUERY_OBJECT_ROTATION,                              STATUS_NEVER);
    /*0x009*/ DEFINE_HANDLER(CMSG_TELEPORT_TO_UNIT,                                                 STATUS_LOGGEDIN,   PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x00A*/ DEFINE_HANDLER(CMSG_ZONE_MAP,                                                         STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x00B*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_ZONE_MAP,                                           STATUS_NEVER);
    /*0x00C*/ DEFINE_HANDLER(CMSG_DEBUG_CHANGECELLZONE,                                             STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x00D*/ DEFINE_HANDLER(CMSG_MOVE_CHARACTER_CHEAT,                                             STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x00E*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_MOVE_CHARACTER_CHEAT,                               STATUS_NEVER);
    /*0x00F*/ DEFINE_HANDLER(CMSG_RECHARGE,                                                         STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x010*/ DEFINE_HANDLER(CMSG_LEARN_SPELL,                                                      STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x011*/ DEFINE_HANDLER(CMSG_CREATEMONSTER,                                                    STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x012*/ DEFINE_HANDLER(CMSG_DESTROYMONSTER,                                                   STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x013*/ DEFINE_HANDLER(CMSG_CREATEITEM,                                                       STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x014*/ DEFINE_HANDLER(CMSG_CREATEGAMEOBJECT,                                                 STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x015*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CHECK_FOR_BOTS,                                     STATUS_NEVER);
    /*0x016*/ DEFINE_HANDLER(CMSG_MAKEMONSTERATTACKGUID,                                            STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x017*/ DEFINE_HANDLER(CMSG_BOT_DETECTED2,                                                    STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x018*/ DEFINE_HANDLER(CMSG_FORCEACTION,                                                      STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x019*/ DEFINE_HANDLER(CMSG_FORCEACTIONONOTHER,                                               STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x01A*/ DEFINE_HANDLER(CMSG_FORCEACTIONSHOW,                                                  STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x01B*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_FORCEACTIONSHOW,                                    STATUS_NEVER);
    /*0x01C*/ DEFINE_HANDLER(CMSG_PETGODMODE,                                                       STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x01D*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_PETGODMODE,                                         STATUS_NEVER);
    /*0x01E*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_REFER_A_FRIEND_EXPIRED,                             STATUS_NEVER);
    /*0x01F*/ DEFINE_HANDLER(CMSG_WEATHER_SPEED_CHEAT,                                              STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x020*/ DEFINE_HANDLER(CMSG_UNDRESSPLAYER,                                                    STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x021*/ DEFINE_HANDLER(CMSG_BEASTMASTER,                                                      STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x022*/ DEFINE_HANDLER(CMSG_GODMODE,                                                          STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x023*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_GODMODE,                                            STATUS_NEVER);
    /*0x024*/ DEFINE_HANDLER(CMSG_CHEAT_SETMONEY,                                                   STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x025*/ DEFINE_HANDLER(CMSG_LEVEL_CHEAT,                                                      STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x026*/ DEFINE_HANDLER(CMSG_PET_LEVEL_CHEAT,                                                  STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x027*/ DEFINE_HANDLER(CMSG_SET_WORLDSTATE,                                                   STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x028*/ DEFINE_HANDLER(CMSG_COOLDOWN_CHEAT,                                                   STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x029*/ DEFINE_HANDLER(CMSG_USE_SKILL_CHEAT,                                                  STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02A*/ DEFINE_HANDLER(CMSG_FLAG_QUEST,                                                       STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02B*/ DEFINE_HANDLER(CMSG_FLAG_QUEST_FINISH,                                                STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02C*/ DEFINE_HANDLER(CMSG_CLEAR_QUEST,                                                      STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02D*/ DEFINE_HANDLER(CMSG_SEND_EVENT,                                                       STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02E*/ DEFINE_HANDLER(CMSG_DEBUG_AISTATE,                                                    STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x02F*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_DEBUG_AISTATE,                                      STATUS_NEVER);
    /*0x030*/ DEFINE_HANDLER(CMSG_DISABLE_PVP_CHEAT,                                                STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x031*/ DEFINE_HANDLER(CMSG_ADVANCE_SPAWN_TIME,                                               STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x032*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_DESTRUCTIBLE_BUILDING_DAMAGE,                       STATUS_NEVER);
    /*0x033*/ DEFINE_HANDLER(CMSG_AUTH_SRP6_BEGIN,                                                  STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x034*/ DEFINE_HANDLER(CMSG_AUTH_SRP6_PROOF,                                                  STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x035*/ DEFINE_HANDLER(CMSG_AUTH_SRP6_RECODE,                                                 STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x039*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_AUTH_SRP6_RESPONSE,                                 STATUS_NEVER);
    /*0x03A*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CHAR_CREATE,                                        STATUS_NEVER);
    /*0x03B*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CHAR_ENUM,                                          STATUS_NEVER);
    /*0x03C*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CHAR_DELETE,                                        STATUS_NEVER);
    /*0x03E*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_NEW_WORLD,                                          STATUS_NEVER);
    /*0x03F*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_TRANSFER_PENDING,                                   STATUS_NEVER);
    /*0x040*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_TRANSFER_ABORTED,                                   STATUS_NEVER);
    /*0x041*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CHARACTER_LOGIN_FAILED,                             STATUS_NEVER);
    /*0x042*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_LOGIN_SETTIMESPEED,                                 STATUS_NEVER);
    /*0x043*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_GAMETIME_UPDATE,                                    STATUS_NEVER);
    /*0x044*/ DEFINE_HANDLER(CMSG_GAMETIME_SET,                                                     STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x045*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_GAMETIME_SET,                                       STATUS_NEVER);
    /*0x046*/ DEFINE_HANDLER(CMSG_GAMESPEED_SET,                                                    STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x047*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_GAMESPEED_SET,                                      STATUS_NEVER);
    /*0x048*/ DEFINE_HANDLER(CMSG_SERVERTIME,                                                       STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x049*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_SERVERTIME,                                         STATUS_NEVER);
    /*0x503*/ DEFINE_HANDLER(CMSG_AFK_MONITOR_INFO_REQUEST,                                         STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x504*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_AFK_MONITOR_INFO_RESPONSE,                          STATUS_NEVER);
    /*0x505*/ DEFINE_HANDLER(CMSG_AFK_MONITOR_INFO_CLEAR,                                           STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x506*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CORPSE_NOT_IN_INSTANCE,                             STATUS_NEVER);
    /*0x507*/ DEFINE_HANDLER(CMSG_GM_NUKE_CHARACTER,                                                STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x508*/ DEFINE_HANDLER(CMSG_SET_ALLOW_LOW_LEVEL_RAID1,                                        STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x509*/ DEFINE_HANDLER(CMSG_SET_ALLOW_LOW_LEVEL_RAID2,                                        STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x50A*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_CAMERA_SHAKE,                                       STATUS_NEVER);
    /*0x50B*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_SOCKET_GEMS_RESULT,                                 STATUS_NEVER);
    /*0x50C*/ DEFINE_HANDLER(CMSG_SET_CHARACTER_MODEL,                                              STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x50D*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_REDIRECT_CLIENT,                                    STATUS_NEVER);
    /*0x50E*/ DEFINE_HANDLER(CMSG_REDIRECTION_FAILED,                                               STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x50F*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_SUSPEND_COMMS,                                      STATUS_NEVER);
    /*0x510*/ DEFINE_HANDLER(CMSG_SUSPEND_COMMS_ACK,                                                STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x511*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_FORCE_SEND_QUEUED_PACKETS,                          STATUS_NEVER);
    /*0x512*/ DEFINE_HANDLER(CMSG_REDIRECTION_AUTH_PROOF,                                           STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x513*/ DEFINE_HANDLER(CMSG_DROP_NEW_CONNECTION,                                              STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x514*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_SEND_ALL_COMBAT_LOG,                                STATUS_NEVER);
    /*0x515*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_OPEN_LFG_DUNGEON_FINDER,                            STATUS_NEVER);
    /*0x516*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_MOVE_SET_COLLISION_HGT,                             STATUS_NEVER);
    /*0x517*/ DEFINE_HANDLER(CMSG_MOVE_SET_COLLISION_HGT_ACK,                                       STATUS_UNHANDLED,  PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x518*/ DEFINE_HANDLER(MSG_MOVE_SET_COLLISION_HGT,                                            STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x519*/ DEFINE_HANDLER(CMSG_CLEAR_RANDOM_BG_WIN_TIME,                                         STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x51A*/ DEFINE_HANDLER(CMSG_CLEAR_HOLIDAY_BG_WIN_TIME,                                        STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x51B*/ DEFINE_HANDLER(CMSG_COMMENTATOR_SKIRMISH_QUEUE_COMMAND,                               STATUS_NEVER,      PROCESS_INPLACE,        &WorldSession::Handle_NULL                              );
    /*0x51C*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT1,                 STATUS_NEVER);
    /*0x51D*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_COMMENTATOR_SKIRMISH_QUEUE_RESULT2,                 STATUS_NEVER);
    /*0x51E*/ DEFINE_SERVER_OPCODE_HANDLER(SMSG_MULTIPLE_MOVES, STATUS_NEVER);

#undef DEFINE_HANDLER
#undef DEFINE_SERVER_OPCODE_HANDLER
}

template<typename T>
inline std::string GetOpcodeNameForLoggingImpl(T id)
{
    U16 opcode = U16(id);
    std::ostringstream ss;
    ss << '[';

    if (static_cast<U16>(id) < NUM_OPCODE_HANDLERS)
    {
        if (OpcodeHandler const* handler = opcodeTable[id])
            ss << handler->Name;
        else
            ss << "UNKNOWN OPCODE";
    }
    else
        ss << "INVALID OPCODE";

    ss << " 0x" << std::hex << std::setw(4) << std::setfill('0') << std::uppercase << opcode << std::nouppercase << std::dec << " (" << opcode << ")]";
    return ss.str();
}

std::string GetOpcodeNameForLogging(Opcodes opcode)
{
    return GetOpcodeNameForLoggingImpl(opcode);
}
