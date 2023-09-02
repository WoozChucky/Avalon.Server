#include <Game/Server/WorldSession.h>

#include <Game/Server/WorldSocket.h>
#include <Game/Entities/Player/Player.h>
#include <Game/Server/Packets/CharacterPackets.h>
#include <Game/Time/GameTime.h>
#include <Database/QueryHolder.h>
#include <Shared/Realms/Realm.h>

namespace
{
    std::string const DefaultPlayerName = "<none>";
}

/// WorldSession constructor
WorldSession::WorldSession(U32 id, std::string&& name, std::shared_ptr<WorldSocket> sock, AccountTypes sec,
                           time_t mute_time, LocaleConstant locale, bool skipQueue, U32 TotalTime) :
        m_muteTime(mute_time),
        m_timeOutTime(0),
        AntiDOS(this),
        m_GUIDLow(0),
        _player(nullptr),
        m_Socket(sock),
        _security(sec),
        _skipQueue(skipQueue),
        _accountId(id),
        _accountName(std::move(name)),
        m_total_time(TotalTime),
        _logoutTime(0),
        m_inQueue(false),
        m_playerLoading(false),
        m_playerLogout(false),
        m_playerRecentlyLogout(false),
        m_playerSave(false),
        m_sessionDbcLocale(LOCALE_enUS),
        m_sessionDbLocaleIndex(locale),
        m_latency(0),
        _calendarEventCreationCooldown(0),
        _addonMessageReceiveCount(0),
        _timeSyncClockDeltaQueue(6),
        _timeSyncClockDelta(0),
        _pendingTimeSyncRequests()
{
    _offlineTime = 0;
    _kicked = false;

    _timeSyncNextCounter = 0;
    _timeSyncTimer = 0;

    if (sock)
    {
        m_Address = sock->GetRemoteIpAddress().to_string();
        ResetTimeOutTime(false);
        LoginDatabase.Execute("UPDATE account SET online = 1 WHERE id = {};", GetAccountId()); // One-time query
    }
}

/// WorldSession destructor
WorldSession::~WorldSession()
{
    LoginDatabase.Execute("UPDATE account SET totaltime = {} WHERE id = {}", GetTotalTime(), GetAccountId());

    ///- unload player if not unloaded
    if (_player)
        LogoutPlayer(true);

    /// - If have unclosed socket, close it
    if (m_Socket)
    {
        m_Socket->CloseSocket();
        m_Socket = nullptr;
    }

    ///- empty incoming packet queue
    WorldPacket* packet = nullptr;
    while (_recvQueue.next(packet))
        delete packet;

    LoginDatabase.Execute("UPDATE account SET online = 0 WHERE id = {};", GetAccountId());     // One-time query
}

/// %Log the player out
void WorldSession::LogoutPlayer(bool save)
{
    m_playerLogout = true;
    m_playerSave = save;

    if (_player)
    {
        if (save)
        {
            _player->SaveToDB(false, true);
        }

        LOG_INFO("entities.player", "Account: {} (IP: {}) Logout Character:[{}] ({}) Level: {}",
                 GetAccountId(), GetRemoteAddress(), _player->GetName(), _player->GetGUID().ToString(), _player->GetZoneId());

        SetPlayer(nullptr); // pointer already deleted

        //! Send the 'logout complete' packet to the client
        //! Client will respond by sending 3x CMSG_CANCEL_TRADE, which we currently dont handle
        SendPacket(WorldPackets::Character::LogoutComplete().Write());
        LOG_DEBUG("network", "SESSION: Sent SMSG_LOGOUT_COMPLETE Message");

        //! Since each account can only have one online character at any given time, ensure all characters for active account are marked as offline
        CharacterDatabasePreparedStatement* stmt = CharacterDatabase.GetPreparedStatement(CHAR_UPD_ACCOUNT_ONLINE);
        stmt->SetData(0, GetAccountId());
        CharacterDatabase.Execute(stmt);
    }

    m_playerLogout = false;
    m_playerSave = false;
    m_playerRecentlyLogout = true;
    SetLogoutStartTime(0);
}

/// Kick a player out of the World
void WorldSession::KickPlayer(std::string const& reason, bool setKicked)
{
    if (m_Socket)
    {
        LOG_INFO("network.kick", "Account: {} Character: '{}' {} kicked with reason: {}", GetAccountId(), _player ? _player->GetName() : "<none>",
                 _player ? _player->GetGUID().ToString() : "", reason);

        m_Socket->CloseSocket();
    }

    if (setKicked)
        SetKicked(true); // pussywizard: the session won't be left ingame for 60 seconds and to also kick offline session
}

bool WorldSession::HandleSocketClosed()
{
    if (m_Socket && !m_Socket->IsOpen() && !IsKicked() && GetPlayer() && !PlayerLogout() && GetPlayer()->IsInWorld() && !World::IsStopped())
    {
        m_Socket = nullptr;
        return true;
    }

    return false;
}

class AccountInfoQueryHolderPerRealm : public CharacterDatabaseQueryHolder
{
public:
    enum
    {
        GLOBAL_ACCOUNT_DATA = 0,
        TUTORIALS,

        MAX_QUERIES
    };

    AccountInfoQueryHolderPerRealm() { SetSize(MAX_QUERIES); }

    bool Initialize(U32 accountId)
    {
        bool ok = true;

        CharacterDatabasePreparedStatement* stmt = CharacterDatabase.GetPreparedStatement(CHAR_SEL_ACCOUNT_DATA);
        stmt->SetData(0, accountId);
        ok = SetPreparedQuery(GLOBAL_ACCOUNT_DATA, stmt) && ok;

        stmt = CharacterDatabase.GetPreparedStatement(CHAR_SEL_TUTORIALS);
        stmt->SetData(0, accountId);
        ok = SetPreparedQuery(TUTORIALS, stmt) && ok;

        return ok;
    }
};

void WorldSession::InitializeSession()
{
    U32 cacheVersion = sConfigMgr->GetOption<S32>("ClientCacheVersion", 0);
    //sScriptMgr->OnBeforeFinalizePlayerWorldSession(cacheVersion);

    std::shared_ptr<AccountInfoQueryHolderPerRealm> realmHolder = std::make_shared<AccountInfoQueryHolderPerRealm>();
    if (!realmHolder->Initialize(GetAccountId()))
    {
        SendAuthResponse(AUTH_SYSTEM_ERROR, false);
        return;
    }

    AddQueryHolderCallback(CharacterDatabase.DelayQueryHolder(realmHolder)).AfterComplete([this, cacheVersion](SQLQueryHolderBase const& holder)
      {
          InitializeSessionCallback(static_cast<AccountInfoQueryHolderPerRealm const&>(holder), cacheVersion);
      });
}

/// Send a packet to the client
void WorldSession::SendPacket(WorldPacket const* packet)
{
    if (packet->GetOpcode() == NULL_OPCODE)
    {
        LOG_ERROR("network.opcode", "{} send NULL_OPCODE", GetPlayerInfo());
        return;
    }

    if (!m_Socket)
        return;

#if defined(_DEBUG)
    // Code for network use statistic
    static U64 sendPacketCount = 0;
    static U64 sendPacketBytes = 0;

    static time_t firstTime = GameTime::GetGameTime().count();
    static time_t lastTime = firstTime;                     // next 60 secs start time

    static U64 sendLastPacketCount = 0;
    static U64 sendLastPacketBytes = 0;

    time_t cur_time = GameTime::GetGameTime().count();

    if ((cur_time - lastTime) < 60)
    {
        sendPacketCount += 1;
        sendPacketBytes += packet->size();

        sendLastPacketCount += 1;
        sendLastPacketBytes += packet->size();
    }
    else
    {
        U64 minTime = U64(cur_time - lastTime);
        U64 fullTime = U64(lastTime - firstTime);

        LOG_DEBUG("network", "Send all time packets count: {} bytes: {} avr.count/sec: {} avr.bytes/sec: {} time: {}", sendPacketCount, sendPacketBytes, float(sendPacketCount) / fullTime, float(sendPacketBytes) / fullTime, U32(fullTime));

        LOG_DEBUG("network", "Send last min packets count: {} bytes: {} avr.count/sec: {} avr.bytes/sec: {}", sendLastPacketCount, sendLastPacketBytes, float(sendLastPacketCount) / minTime, float(sendLastPacketBytes) / minTime);

        lastTime = cur_time;
        sendLastPacketCount = 1;
        sendLastPacketBytes = packet->wpos();               // wpos is real written size
    }
#endif                                                      // !_DEBUG

    LOG_TRACE("network.opcode", "S->C: {} {}", GetPlayerInfo(), GetOpcodeNameForLogging(static_cast<OpcodeServer>(packet->GetOpcode())));
    m_Socket->SendPacket(*packet);
}

void WorldSession::SendAuthResponse(U8 code, bool shortForm, U32 queuePos)
{
    WorldPacket packet(SMSG_AUTH_RESPONSE, 1 + 4 + 1 + 4 + 1 + (shortForm ? 0 : (4 + 1)));
    packet << U8(code);
    packet << U32(0);                                   // BillingTimeRemaining
    packet << U8(0);                                    // BillingPlanFlags
    packet << U32(0);                                   // BillingTimeRested
    packet << U8(0);                          // 0 - normal, 1 - TBC, 2 - WOTLK, must be set in database manually for each account

    if (!shortForm)
    {
        packet << U32(queuePos);                             // Queue position
        packet << U8(0);                                     // Realm has a free character migration - bool
    }

    SendPacket(&packet);
}

void WorldSession::SetPlayer(Player* player)
{
    _player = player;

    // set m_GUID that can be used while player loggined and later until m_playerRecentlyLogout not reset
    if (_player)
        m_GUIDLow = _player->GetGUID().GetCounter();
}

void WorldSession::ProcessQueryCallbacks()
{
    _queryProcessor.ProcessReadyCallbacks();
    _transactionCallbacks.ProcessReadyCallbacks();
    _queryHolderProcessor.ProcessReadyCallbacks();
}

TransactionCallback& WorldSession::AddTransactionCallback(TransactionCallback&& callback)
{
    return _transactionCallbacks.AddCallback(std::move(callback));
}

SQLQueryHolderCallback& WorldSession::AddQueryHolderCallback(SQLQueryHolderCallback&& callback)
{
    return _queryHolderProcessor.AddCallback(std::move(callback));
}

void WorldSession::InitializeSessionCallback(CharacterDatabaseQueryHolder const& realmHolder, U32 clientCacheVersion)
{
    LoadAccountData(realmHolder.GetPreparedResult(AccountInfoQueryHolderPerRealm::GLOBAL_ACCOUNT_DATA), GLOBAL_CACHE_MASK);

    if (!m_inQueue)
    {
        SendAuthResponse(AUTH_OK, true);
    }
    else
    {
        SendAuthWaitQueue(0);
    }

    SetInQueue(false);
    ResetTimeOutTime(false);

    SendAddonsInfo();
    SendClientCacheVersion(clientCacheVersion);
}

void WorldSession::SendAddonsInfo() {
    LOG_INFO("network", "Sending addons info to client");
}

void WorldSession::SendClientCacheVersion(U32 version)
{
    WorldPacket data(SMSG_CLIENTCACHE_VERSION, 4);
    data << U32(version);
    SendPacket(&data);
}

std::string const& WorldSession::GetPlayerName() const
{
    return _player ? _player->GetName() : DefaultPlayerName;
}

std::string WorldSession::GetPlayerInfo() const
{
    std::ostringstream ss;

    ss << "[Player: ";

    if (!m_playerLoading && _player)
    {
        ss << _player->GetName() << ' ' << _player->GetGUID().ToString() << ", ";
    }

    ss << "Account: " << GetAccountId() << "]";

    return ss.str();
}

void WorldSession::SendAuthWaitQueue(U32 position)
{
    if (position == 0)
    {
        WorldPacket packet(SMSG_AUTH_RESPONSE, 1);
        packet << U8(AUTH_OK);
        SendPacket(&packet);
    }
    else
    {
        WorldPacket packet(SMSG_AUTH_RESPONSE, 6);
        packet << U8(AUTH_WAIT_QUEUE);
        packet << U32(position);
        packet << U8(0);                                 // unk
        SendPacket(&packet);
    }
}

void WorldSession::LoadAccountData(PreparedQueryResult result, U32 mask)
{
    for (U32 i = 0; i < NUM_ACCOUNT_DATA_TYPES; ++i)
        if (mask & (1 << i))
            m_accountData[i] = AccountData();

    if (!result)
        return;

    do
    {
        Field* fields = result->Fetch();
        U32 type = fields[0].Get<U8>();
        if (type >= NUM_ACCOUNT_DATA_TYPES)
        {
            LOG_ERROR("network", "Table `{}` have invalid account data type ({}), ignore.", mask == GLOBAL_CACHE_MASK ? "account_data" : "character_account_data", type);
            continue;
        }

        if ((mask & (1 << type)) == 0)
        {
            LOG_ERROR("network", "Table `{}` have non appropriate for table  account data type ({}), ignore.", mask == GLOBAL_CACHE_MASK ? "account_data" : "character_account_data", type);
            continue;
        }

        m_accountData[type].Time = time_t(fields[1].Get<U32>());
        m_accountData[type].Data = fields[2].Get<std::string>();
    } while (result->NextRow());
}

bool WorldSession::DosProtection::EvaluateOpcode(WorldPacket& p, time_t time) const
{
    U32 maxPacketCounterAllowed = GetMaxPacketCounterAllowed(p.GetOpcode());

    // Return true if there no limit for the opcode
    if (!maxPacketCounterAllowed)
        return true;

    PacketCounter& packetCounter = _PacketThrottlingMap[p.GetOpcode()];
    if (packetCounter.lastReceiveTime != time)
    {
        packetCounter.lastReceiveTime = time;
        packetCounter.amountCounter = 0;
    }

    // Check if player is flooding some packets
    if (++packetCounter.amountCounter <= maxPacketCounterAllowed)
        return true;

    LOG_WARN("network", "AntiDOS: Account {}, IP: {}, Ping: {}, Character: {}, flooding packet (opc: {} (0x{:X}), count: {})",
             Session->GetAccountId(), Session->GetRemoteAddress(), Session->GetLatency(), Session->GetPlayerName(),
             opcodeTable[static_cast<OpcodeClient>(p.GetOpcode())]->Name, p.GetOpcode(), packetCounter.amountCounter);

    switch (_policy)
    {
        case POLICY_LOG:
            return true;
        case POLICY_KICK:
        {
            LOG_INFO("network", "AntiDOS: Player {} kicked!", Session->GetPlayerName());
            Session->KickPlayer();
            return false;
        }
        case POLICY_BAN:
        {
            U32 bm = sConfigMgr->GetOption<S32>("PacketSpoof.BanMode", (U32)0);
            U32 duration = sConfigMgr->GetOption<S32>("PacketSpoof.BanDuration", 86400);
            std::string nameOrIp = "";
            switch (bm)
            {
                case 0: // Ban account
                    (void)AccountMgr::GetName(Session->GetAccountId(), nameOrIp);
                    //sBan->BanAccount(nameOrIp, std::to_string(duration), "DOS (Packet Flooding/Spoofing", "Server: AutoDOS");
                    break;
                case 1: // Ban ip
                    nameOrIp = Session->GetRemoteAddress();
                    //sBan->BanIP(nameOrIp, std::to_string(duration), "DOS (Packet Flooding/Spoofing", "Server: AutoDOS");
                    break;
            }

            LOG_INFO("network", "AntiDOS: Player automatically banned for {} seconds.", duration);
            return false;
        }
        default: // invalid policy
            return true;
    }
}

U32 WorldSession::DosProtection::GetMaxPacketCounterAllowed(U16 opcode) const
{
    U32 maxPacketCounterAllowed;
    switch (opcode)
    {
        // CPU usage sending 2000 packets/second on a 3.70 GHz 4 cores on Win x64
        //                                              [% CPU mysqld]   [%CPU worldserver RelWithDebInfo]
        case CMSG_PLAYER_LOGIN:                         //   0               0.5
        case CMSG_NAME_QUERY:                           //   0               1
        case CMSG_PET_NAME_QUERY:                       //   0               1
        case CMSG_NPC_TEXT_QUERY:                       //   0               1
        case CMSG_ATTACKSTOP:                           //   0               1
        case CMSG_QUERY_QUESTS_COMPLETED:               //   0               1
        case CMSG_QUERY_TIME:                           //   0               1
        case CMSG_CORPSE_MAP_POSITION_QUERY:            //   0               1
        case CMSG_MOVE_TIME_SKIPPED:                    //   0               1
        case MSG_QUERY_NEXT_MAIL_TIME:                  //   0               1
        case CMSG_SET_SHEATHED:                         //   0               1
        case MSG_RAID_TARGET_UPDATE:                    //   0               1
        case CMSG_PLAYER_LOGOUT:                        //   0               1
        case CMSG_LOGOUT_REQUEST:                       //   0               1
        case CMSG_PET_RENAME:                           //   0               1
        case CMSG_QUESTGIVER_CANCEL:                    //   0               1
        case CMSG_QUESTGIVER_REQUEST_REWARD:            //   0               1
        case CMSG_COMPLETE_CINEMATIC:                   //   0               1
        case CMSG_BANKER_ACTIVATE:                      //   0               1
        case CMSG_BUY_BANK_SLOT:                        //   0               1
        case CMSG_OPT_OUT_OF_LOOT:                      //   0               1
        case CMSG_DUEL_ACCEPTED:                        //   0               1
        case CMSG_DUEL_CANCELLED:                       //   0               1
        case CMSG_CALENDAR_COMPLAIN:                    //   0               1
        case CMSG_QUEST_QUERY:                          //   0               1.5
        case CMSG_ITEM_QUERY_SINGLE:                    //   0               1.5
        case CMSG_ITEM_NAME_QUERY:                      //   0               1.5
        case CMSG_GAMEOBJECT_QUERY:                     //   0               1.5
        case CMSG_CREATURE_QUERY:                       //   0               1.5
        case CMSG_QUESTGIVER_STATUS_QUERY:              //   0               1.5
        case CMSG_GUILD_QUERY:                          //   0               1.5
        case CMSG_ARENA_TEAM_QUERY:                     //   0               1.5
        case CMSG_TAXINODE_STATUS_QUERY:                //   0               1.5
        case CMSG_TAXIQUERYAVAILABLENODES:              //   0               1.5
        case CMSG_QUESTGIVER_QUERY_QUEST:               //   0               1.5
        case CMSG_PAGE_TEXT_QUERY:                      //   0               1.5
        case MSG_QUERY_GUILD_BANK_TEXT:                 //   0               1.5
        case MSG_CORPSE_QUERY:                          //   0               1.5
        case MSG_MOVE_SET_FACING:                       //   0               1.5
        case CMSG_REQUEST_PARTY_MEMBER_STATS:           //   0               1.5
        case CMSG_QUESTGIVER_COMPLETE_QUEST:            //   0               1.5
        case CMSG_SET_ACTION_BUTTON:                    //   0               1.5
        case CMSG_RESET_INSTANCES:                      //   0               1.5
        case CMSG_HEARTH_AND_RESURRECT:                 //   0               1.5
        case CMSG_TOGGLE_PVP:                           //   0               1.5
        case CMSG_PET_ABANDON:                          //   0               1.5
        case CMSG_ACTIVATETAXIEXPRESS:                  //   0               1.5
        case CMSG_ACTIVATETAXI:                         //   0               1.5
        case CMSG_SELF_RES:                             //   0               1.5
        case CMSG_UNLEARN_SKILL:                        //   0               1.5
        case CMSG_EQUIPMENT_SET_SAVE:                   //   0               1.5
        case CMSG_DELETEEQUIPMENT_SET:                  //   0               1.5
        case CMSG_DISMISS_CRITTER:                      //   0               1.5
        case CMSG_REPOP_REQUEST:                        //   0               1.5
        case CMSG_GROUP_INVITE:                         //   0               1.5
        case CMSG_GROUP_DECLINE:                        //   0               1.5
        case CMSG_GROUP_ACCEPT:                         //   0               1.5
        case CMSG_GROUP_UNINVITE_GUID:                  //   0               1.5
        case CMSG_GROUP_UNINVITE:                       //   0               1.5
        case CMSG_GROUP_DISBAND:                        //   0               1.5
        case CMSG_BATTLEMASTER_JOIN_ARENA:              //   0               1.5
        case CMSG_LEAVE_BATTLEFIELD:                    //   0               1.5
        case MSG_GUILD_BANK_LOG_QUERY:                  //   0               2
        case CMSG_LOGOUT_CANCEL:                        //   0               2
        case CMSG_REALM_SPLIT:                          //   0               2
        case CMSG_ALTER_APPEARANCE:                     //   0               2
        case CMSG_QUEST_CONFIRM_ACCEPT:                 //   0               2
        case MSG_GUILD_EVENT_LOG_QUERY:                 //   0               2.5
        case CMSG_READY_FOR_ACCOUNT_DATA_TIMES:         //   0               2.5
        case CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY:     //   0               2.5
        case CMSG_BEGIN_TRADE:                          //   0               2.5
        case CMSG_INITIATE_TRADE:                       //   0               3
        case CMSG_MESSAGECHAT:                          //   0               3.5
        case CMSG_INSPECT:                              //   0               3.5
        case CMSG_AREA_SPIRIT_HEALER_QUERY:             // not profiled
        case CMSG_STANDSTATECHANGE:                     // not profiled
        case MSG_RANDOM_ROLL:                           // not profiled
        case CMSG_TIME_SYNC_RESP:                       // not profiled
        case CMSG_TRAINER_BUY_SPELL:                    // not profiled
        case CMSG_FORCE_SWIM_SPEED_CHANGE_ACK:          // not profiled
        case CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK:     // not profiled
        case CMSG_FORCE_RUN_SPEED_CHANGE_ACK:           // not profiled
        case CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK:      // not profiled
        case CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK:        // not profiled
        case CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK:   // not profiled
        case CMSG_FORCE_WALK_SPEED_CHANGE_ACK:          // not profiled
        case CMSG_FORCE_TURN_RATE_CHANGE_ACK:           // not profiled
        case CMSG_FORCE_PITCH_RATE_CHANGE_ACK:          // not profiled
        {
            // "0" is a magic number meaning there's no limit for the opcode.
            // All the opcodes above must cause little CPU usage and no sync/async database queries at all
            maxPacketCounterAllowed = 0;
            break;
        }

        case CMSG_QUESTGIVER_ACCEPT_QUEST:              //   0               4
        case CMSG_QUESTLOG_REMOVE_QUEST:                //   0               4
        case CMSG_QUESTGIVER_CHOOSE_REWARD:             //   0               4
        case CMSG_CONTACT_LIST:                         //   0               5
        case CMSG_LEARN_PREVIEW_TALENTS:                //   0               6
        case CMSG_AUTOBANK_ITEM:                        //   0               6
        case CMSG_AUTOSTORE_BANK_ITEM:                  //   0               6
        case CMSG_WHO:                                  //   0               7
        case CMSG_PLAYER_VEHICLE_ENTER:                 //   0               8
        case CMSG_LEARN_PREVIEW_TALENTS_PET:            // not profiled
        case MSG_MOVE_HEARTBEAT:
        {
            maxPacketCounterAllowed = 200;
            break;
        }

        case CMSG_GUILD_SET_PUBLIC_NOTE:                //   1               2         1 async db query
        case CMSG_GUILD_SET_OFFICER_NOTE:               //   1               2         1 async db query
        case CMSG_SET_CONTACT_NOTES:                    //   1               2.5       1 async db query
        case CMSG_CALENDAR_GET_CALENDAR:                //   0               1.5       medium upload bandwidth usage
        case CMSG_GUILD_BANK_QUERY_TAB:                 //   0               3.5       medium upload bandwidth usage
        case CMSG_QUERY_INSPECT_ACHIEVEMENTS:           //   0              13         high upload bandwidth usage
        case CMSG_GAMEOBJ_REPORT_USE:                   // not profiled
        case CMSG_GAMEOBJ_USE:                          // not profiled
        case MSG_PETITION_DECLINE:                      // not profiled
        {
            maxPacketCounterAllowed = 50;
            break;
        }

        case CMSG_QUEST_POI_QUERY:                      //   0              25         very high upload bandwidth usage
        {
            maxPacketCounterAllowed = MAX_QUEST_LOG_SIZE;
            break;
        }

        case CMSG_GM_REPORT_LAG:                        //   1               3         1 async db query
        case CMSG_SPELLCLICK:                           // not profiled
        case CMSG_REMOVE_GLYPH:                         // not profiled
        case CMSG_DISMISS_CONTROLLED_VEHICLE:           // not profiled
        {
            maxPacketCounterAllowed = 20;
            break;
        }

        case CMSG_PETITION_SIGN:                        //   9               4         2 sync 1 async db queries
        case CMSG_TURN_IN_PETITION:                     //   8               5.5       2 sync db query
        case CMSG_GROUP_CHANGE_SUB_GROUP:               //   6               5         1 sync 1 async db queries
        case CMSG_PETITION_QUERY:                       //   4               3.5       1 sync db query
        case CMSG_CHAR_RACE_CHANGE:                     //   5               4         1 sync db query
        case CMSG_CHAR_CUSTOMIZE:                       //   5               5         1 sync db query
        case CMSG_CHAR_FACTION_CHANGE:                  //   5               5         1 sync db query
        case CMSG_CHAR_DELETE:                          //   4               4         1 sync db query
        case CMSG_DEL_FRIEND:                           //   7               5         1 async db query
        case CMSG_ADD_FRIEND:                           //   6               4         1 async db query
        case CMSG_CHAR_RENAME:                          //   5               3         1 async db query
        case CMSG_GMSURVEY_SUBMIT:                      //   2               3         1 async db query
        case CMSG_BUG:                                  //   1               1         1 async db query
        case CMSG_GROUP_SET_LEADER:                     //   1               2         1 async db query
        case CMSG_GROUP_RAID_CONVERT:                   //   1               5         1 async db query
        case CMSG_GROUP_ASSISTANT_LEADER:               //   1               2         1 async db query
        case CMSG_PETITION_BUY:                         // not profiled                1 sync 1 async db queries
        case CMSG_CHANGE_SEATS_ON_CONTROLLED_VEHICLE:   // not profiled
        case CMSG_REQUEST_VEHICLE_PREV_SEAT:            // not profiled
        case CMSG_REQUEST_VEHICLE_NEXT_SEAT:            // not profiled
        case CMSG_REQUEST_VEHICLE_SWITCH_SEAT:          // not profiled
        case CMSG_REQUEST_VEHICLE_EXIT:                 // not profiled
        case CMSG_CONTROLLER_EJECT_PASSENGER:           // not profiled
        case CMSG_ITEM_REFUND:                          // not profiled
        case CMSG_SOCKET_GEMS:                          // not profiled
        case CMSG_WRAP_ITEM:                            // not profiled
        case CMSG_REPORT_PVP_AFK:                       // not profiled
        {
            maxPacketCounterAllowed = 10;
            break;
        }

        case CMSG_CHAR_CREATE:                          //   7               5         3 async db queries
        case CMSG_CHAR_ENUM:                            //  22               3         2 async db queries
        case CMSG_GMTICKET_CREATE:                      //   1              25         1 async db query
        case CMSG_GMTICKET_UPDATETEXT:                  //   0              15         1 async db query
        case CMSG_GMTICKET_DELETETICKET:                //   1              25         1 async db query
        case CMSG_GMRESPONSE_RESOLVE:                   //   1              25         1 async db query
        case CMSG_CALENDAR_ADD_EVENT:                   //  21              10         2 async db query
        case CMSG_CALENDAR_UPDATE_EVENT:                // not profiled
        case CMSG_CALENDAR_REMOVE_EVENT:                // not profiled
        case CMSG_CALENDAR_COPY_EVENT:                  // not profiled
        case CMSG_CALENDAR_EVENT_INVITE:                // not profiled
        case CMSG_CALENDAR_EVENT_SIGNUP:                // not profiled
        case CMSG_CALENDAR_EVENT_RSVP:                  // not profiled
        case CMSG_CALENDAR_EVENT_REMOVE_INVITE:         // not profiled
        case CMSG_CALENDAR_EVENT_MODERATOR_STATUS:      // not profiled
        case CMSG_ARENA_TEAM_INVITE:                    // not profiled
        case CMSG_ARENA_TEAM_ACCEPT:                    // not profiled
        case CMSG_ARENA_TEAM_DECLINE:                   // not profiled
        case CMSG_ARENA_TEAM_LEAVE:                     // not profiled
        case CMSG_ARENA_TEAM_DISBAND:                   // not profiled
        case CMSG_ARENA_TEAM_REMOVE:                    // not profiled
        case CMSG_ARENA_TEAM_LEADER:                    // not profiled
        case CMSG_LOOT_METHOD:                          // not profiled
        case CMSG_GUILD_INVITE:                         // not profiled
        case CMSG_GUILD_ACCEPT:                         // not profiled
        case CMSG_GUILD_DECLINE:                        // not profiled
        case CMSG_GUILD_LEAVE:                          // not profiled
        case CMSG_GUILD_DISBAND:                        // not profiled
        case CMSG_GUILD_LEADER:                         // not profiled
        case CMSG_GUILD_MOTD:                           // not profiled
        case CMSG_GUILD_RANK:                           // not profiled
        case CMSG_GUILD_ADD_RANK:                       // not profiled
        case CMSG_GUILD_DEL_RANK:                       // not profiled
        case CMSG_GUILD_INFO_TEXT:                      // not profiled
        case CMSG_GUILD_BANK_DEPOSIT_MONEY:             // not profiled
        case CMSG_GUILD_BANK_WITHDRAW_MONEY:            // not profiled
        case CMSG_GUILD_BANK_BUY_TAB:                   // not profiled
        case CMSG_GUILD_BANK_UPDATE_TAB:                // not profiled
        case CMSG_SET_GUILD_BANK_TEXT:                  // not profiled
        case MSG_SAVE_GUILD_EMBLEM:                     // not profiled
        case MSG_PETITION_RENAME:                       // not profiled
        case MSG_TALENT_WIPE_CONFIRM:                   // not profiled
        case MSG_SET_DUNGEON_DIFFICULTY:                // not profiled
        case MSG_SET_RAID_DIFFICULTY:                   // not profiled
        case MSG_PARTY_ASSIGNMENT:                      // not profiled
        case MSG_RAID_READY_CHECK:                      // not profiled
        {
            maxPacketCounterAllowed = 3;
            break;
        }

        case CMSG_ITEM_REFUND_INFO:                     // not profiled
        {
            maxPacketCounterAllowed = PLAYER_SLOTS_COUNT;
            break;
        }

        default:
        {
            maxPacketCounterAllowed = 100;
            break;
        }
    }

    return maxPacketCounterAllowed;
}

WorldSession::DosProtection::DosProtection(WorldSession* s) :
        Session(s), _policy(Policy(sConfigMgr->GetOption<S32>("PacketSpoof.Policy", (U32)WorldSession::DosProtection::POLICY_KICK))) { }


void WorldSession::ReadAddonsInfo(ByteBuffer& data)
{
    LOG_INFO("network", "Reading addons info from client");
}

/// Add an incoming packet to the queue
void WorldSession::QueuePacket(WorldPacket* new_packet)
{
    _recvQueue.add(new_packet);
}

void WorldSession::Handle_NULL(WorldPacket& null)
{
    LOG_ERROR("network.opcode", "Received unhandled opcode {} from {}",
              GetOpcodeNameForLogging(static_cast<OpcodeClient>(null.GetOpcode())), GetPlayerInfo());
}

void WorldSession::Handle_ServerSide(WorldPacket& recvPacket)
{
    LOG_ERROR("network.opcode", "Received server-side opcode {} from {}",
              GetOpcodeNameForLogging(static_cast<OpcodeServer>(recvPacket.GetOpcode())), GetPlayerInfo());
}
