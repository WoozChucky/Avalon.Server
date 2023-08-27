#include "World.h"
#include "Utilities/Timer.h"
#include "../Time/GameTime.h"
#include "../Chat/Chat.h"
#include "../../Server/WorldSession.h"
#include "../Entities/Player/Player.h"

#include <Debugging/Errors.h>
#include <Logging/Log.h>
#include <Configuration/ConfigManager.h>

std::atomic_long World::_stopEvent = false;
U8 World::_exitCode = SHUTDOWN_EXIT_CODE;
U32 World::m_worldLoopCounter = 0;

/// World constructor
World::World()
{
    _playerLimit = 0;
    _allowMovement = true;
    _shutdownMask = 0;
    _shutdownTimer = 0;
    _maxActiveSessionCount = 0;
    _maxQueuedSessionCount = 0;
    _playerCount = 0;
    _maxPlayerCount = 0;
    _nextDailyQuestReset = 0s;
    _nextWeeklyQuestReset = 0s;
    _nextMonthlyQuestReset = 0s;
    _nextRandomBGReset = 0s;
    _nextCalendarOldEventsDeletionTime = 0s;
    _nextGuildReset = 0s;
    _mail_expire_check_timer = 0s;
    _isClosed = false;
    _cleaningFlags = 0;
}

/// World destructor
World::~World()
{
    ///- Empty the kicked session set
    while (!_sessions.empty())
    {
        // not remove from queue, prevent loading new sessions
        delete _sessions.begin()->second;
        _sessions.erase(_sessions.begin());
    }

    while (!_offlineSessions.empty())
    {
        delete _offlineSessions.begin()->second;
        _offlineSessions.erase(_offlineSessions.begin());
    }

    CliCommandHolder* command = nullptr;
    while (_cliCmdQueue.next(command))
        delete command;
}

std::unique_ptr<IWorld>& getWorldInstance()
{
    static std::unique_ptr<IWorld> instance = std::make_unique<World>();
    return instance;
}

Player* World::FindPlayerInZone(U32 zone)
{
    ///- circle through active sessions and return the first player found in the zone
    SessionMap::const_iterator itr;
    for (itr = _sessions.begin(); itr != _sessions.end(); ++itr)
    {
        if (!itr->second)
            continue;

        Player* player = itr->second->GetPlayer();
        if (!player)
            continue;

        if (player->IsInWorld() && player->GetZoneId() == zone)
            return player;
    }
    return nullptr;
}

bool World::IsClosed() const
{
    return _isClosed;
}

void World::SetClosed(bool val)
{
    _isClosed = val;
}

/// Find a session by its id
WorldSession* World::FindSession(U32 id) const
{
    SessionMap::const_iterator itr = _sessions.find(id);

    if (itr != _sessions.end())
        return itr->second;                                 // also can return nullptr for kicked session
    else
        return nullptr;
}

WorldSession* World::FindOfflineSession(U32 id) const
{
    SessionMap::const_iterator itr = _offlineSessions.find(id);
    if (itr != _offlineSessions.end())
        return itr->second;
    else
        return nullptr;
}

/// Remove a given session
bool World::KickSession(U32 id)
{
    ///- Find the session, kick the user, but we can't delete session at this moment to prevent iterator invalidation
    SessionMap::const_iterator itr = _sessions.find(id);

    if (itr != _sessions.end() && itr->second)
    {
        if (itr->second->PlayerLoading())
            return false;

        itr->second->KickPlayer("KickSession", false);
    }

    return true;
}

void World::AddSession(WorldSession* s)
{
    _addSessQueue.add(s);
}

void World::AddSession_(WorldSession* s)
{
    ASSERT (s);

    // kick existing session with same account (if any)
    // if character on old session is being loaded, then return
    if (!KickSession(s->GetAccountId()))
    {
        s->KickPlayer("kick existing session with same account");
        delete s; // session not added yet in session list, so not listed in queue
        return;
    }

    SessionMap::const_iterator old = _sessions.find(s->GetAccountId());
    if (old != _sessions.end())
    {
        WorldSession* oldSession = old->second;

        if (!RemoveQueuedPlayer(oldSession))
            _disconnects[s->GetAccountId()] = GameTime::GetGameTime().count();

        // pussywizard:
        if (oldSession->HandleSocketClosed())
        {
            // there should be no offline session if current one is logged onto a character
            SessionMap::iterator iter;
            if ((iter = _offlineSessions.find(oldSession->GetAccountId())) != _offlineSessions.end())
            {
                WorldSession* tmp = iter->second;
                _offlineSessions.erase(iter);
                delete tmp;
            }
            oldSession->SetOfflineTime(GameTime::GetGameTime().count());
            _offlineSessions[oldSession->GetAccountId()] = oldSession;
        }
        else
        {
            delete oldSession;
        }
    }

    _sessions[s->GetAccountId()] = s;

    U32 Sessions = GetActiveAndQueuedSessionCount();
    U32 pLimit = GetPlayerAmountLimit();

    // don't count this session when checking player limit
    --Sessions;

    if (pLimit > 0 && Sessions >= pLimit)
    {
        AddQueuedPlayer(s);
        UpdateMaxSessionCounters();
        return;
    }

    s->InitializeSession();

    UpdateMaxSessionCounters();
}

bool World::HasRecentlyDisconnected(WorldSession* session)
{
    if (!session)
        return false;

    if (U32 tolerance = 150000)
    {
        for (DisconnectMap::iterator i = _disconnects.begin(); i != _disconnects.end();)
        {
            if ((GameTime::GetGameTime().count() - i->second) < tolerance)
            {
                if (i->first == session->GetAccountId())
                    return true;
                ++i;
            }
            else
                _disconnects.erase(i++);
        }
    }
    return false;
}

S32 World::GetQueuePos(WorldSession* sess)
{
    U32 position = 1;

    for (Queue::const_iterator iter = _queuedPlayer.begin(); iter != _queuedPlayer.end(); ++iter, ++position)
        if ((*iter) == sess)
            return position;

    return 0;
}

void World::AddQueuedPlayer(WorldSession* sess)
{
    sess->SetInQueue(true);
    _queuedPlayer.push_back(sess);
}

bool World::RemoveQueuedPlayer(WorldSession* sess)
{
    U32 sessions = GetActiveSessionCount();

    U32 position = 1;
    Queue::iterator iter = _queuedPlayer.begin();

    // search to remove and count skipped positions
    bool found = false;

    for (; iter != _queuedPlayer.end(); ++iter, ++position)
    {
        if (*iter == sess)
        {
            sess->SetInQueue(false);
            iter = _queuedPlayer.erase(iter);
            found = true;
            break;
        }
    }

    // if session not queued then it was an active session
    if (!found)
    {
        ASSERT(sessions > 0);
        --sessions;
    }

    // accept first in queue
    if ((!GetPlayerAmountLimit() || sessions < GetPlayerAmountLimit()) && !_queuedPlayer.empty())
    {
        WorldSession* pop_sess = _queuedPlayer.front();
        pop_sess->InitializeSession();
        _queuedPlayer.pop_front();

        // update iter to point first queued socket or end() if queue is empty now
        iter = _queuedPlayer.begin();
        position = 1;
    }

    return found;
}

/// Initialize config values
void World::LoadConfigSettings(bool reload)
{
    if (reload)
    {
        if (!sConfigMgr->Reload())
        {
            LOG_ERROR("server.loading", "World settings reload fail: can't read settings.");
            return;
        }

        sLog->LoadFromConfig();
    }

    // Set realm id and enable db logging
    sLog->SetRealmId(1);

    // load update time related configs
    //sWorldUpdateTime.LoadFromConfig();

    ///- Read the player limit and the Message of the day from the config file
    if (!reload)
    {
        SetPlayerAmountLimit(sConfigMgr->GetOption<S32>("PlayerLimit", 1000));
    }

    ///- Get string for new logins (newly created characters)
    SetNewCharString(sConfigMgr->GetOption<std::string>("PlayerStart.String", ""));

    auto compression = sConfigMgr->GetOption<S32>("Compression", 1);
    if (compression < 1 || compression > 9)
    {
        LOG_ERROR("server.loading", "Compression level ({}) must be in range 1..9. Using default compression level (1).", compression);
        compression = 1;
    }

    auto mapUpdateInterval = sConfigMgr->GetOption<S32>("MapUpdateInterval", 10);
    if (mapUpdateInterval < MIN_MAP_UPDATE_DELAY)
    {
        LOG_ERROR("server.loading", "MapUpdateInterval ({}) must be greater {}. Use this minimal value.", mapUpdateInterval, MIN_MAP_UPDATE_DELAY);
        mapUpdateInterval = MIN_MAP_UPDATE_DELAY;
    }

    //if (reload)
        //sMapMgr->SetMapUpdateInterval(mapUpdateInterval);

    auto worldServerPort = sConfigMgr->GetOption<S32>("WorldServerPort", 21000);

    auto closeIdleConnections    = sConfigMgr->GetOption<bool>("CloseIdleConnections", true);
    auto socketTimeOutTime       = sConfigMgr->GetOption<S32>("SocketTimeOutTime", 900000);
    auto socketTimeOutTimeActive = sConfigMgr->GetOption<S32>("SocketTimeOutTimeActive", 60000);
    auto sessionAddDelay         = sConfigMgr->GetOption<S32>("SessionAddDelay", 10000);


    auto updateUptimeInterval    = sConfigMgr->GetOption<S32>("UpdateUptimeInterval", 10);
    if (updateUptimeInterval <= 0)
    {
        LOG_ERROR("server.loading", "UpdateUptimeInterval ({}) must be > 0, set to default 10.", updateUptimeInterval);
        updateUptimeInterval = 1;
    }

    if (reload)
    {
        //_timers[WUPDATE_UPTIME].SetInterval(_int_configs[CONFIG_UPTIME_UPDATE]*MINUTE * IN_MILLISECONDS);
        //_timers[WUPDATE_UPTIME].Reset();
    }

    auto maxOverspeedPings = sConfigMgr->GetOption<S32>("MaxOverspeedPings", 2);
    if (maxOverspeedPings != 0 && maxOverspeedPings < 2)
    {
        LOG_ERROR("server.loading", "MaxOverspeedPings ({}) must be in range 2..infinity (or 0 to disable check). Set to 2.", maxOverspeedPings);
        maxOverspeedPings = 2;
    }

    ///- Read the "Data" directory from the config file
    std::string dataPath = sConfigMgr->GetOption<std::string>("DataDir", "./");
    if (dataPath.empty() || (dataPath.at(dataPath.length() - 1) != '/' && dataPath.at(dataPath.length() - 1) != '\\'))
        dataPath.push_back('/');

#if AV_PLATFORM == AV_PLATFORM_UNIX
    if (dataPath[0] == '~')
    {
        const char* home = getenv("HOME");
        if (home)
            dataPath.replace(0, 1, home);
    }
#endif

    if (reload)
    {
        if (dataPath != _dataPath)
            LOG_ERROR("server.loading", "DataDir option can't be changed at avalon.conf reload, using current value ({}).", _dataPath);
    }
    else
    {
        _dataPath = dataPath;
        LOG_INFO("server.loading", "Using DataDir {}", _dataPath);
    }

    // AutoBroadcast
    auto broadcast         = sConfigMgr->GetOption<bool>("AutoBroadcast.On", false);
    auto broadcastCenter   = sConfigMgr->GetOption<S32>("AutoBroadcast.Center", 0);
    auto broadcastTimer = sConfigMgr->GetOption<S32>("AutoBroadcast.Timer", 60000);
    if (reload)
    {
        //_timers[WUPDATE_AUTOBROADCAST].SetInterval(_int_configs[CONFIG_AUTOBROADCAST_INTERVAL]);
        //_timers[WUPDATE_AUTOBROADCAST].Reset();
    }

    // MySQL ping time interval
    auto maxPingTime = sConfigMgr->GetOption<S32>("MaxPingTime", 30);

    // packet spoof punishment
    auto packetSpoofPolicy = sConfigMgr->GetOption<S32>("PacketSpoof.Policy", (U32)1);
    auto packetSpoofBanMode = sConfigMgr->GetOption<S32>("PacketSpoof.BanMode", (U32)0);
    if (packetSpoofBanMode > 1)
        packetSpoofBanMode = (U32)0;

    auto packetSpoofBanDuration = sConfigMgr->GetOption<S32>("PacketSpoof.BanDuration", 86400);

    // Realm Availability
    auto worldAvailability = sConfigMgr->GetOption<bool>("World.RealmAvailability", true);
}

void World::SetInitialWorldSettings()
{
    ///- Server startup begin
    U32 startupBegin = getMSTime();

    ///- Initialize the random number generator
    srand((unsigned int)GameTime::GetGameTime().count());

    ///- Initialize config settings
    LoadConfigSettings();

    ///- Initialize pool manager
    //sPoolMgr->Initialize();

    ///- Initialize game event manager
    //sGameEventMgr->Initialize();

    ///- Loading strings. Getting no records means core load has to be canceled because no error message can be output.
    LOG_INFO("server.loading", " ");
    LOG_INFO("server.loading", "Loading Avalon Strings...");


    ///- Load the DBC files
    LOG_INFO("server.loading", "Initialize Data Stores...");

    ///- Initialize game time and timers
    LOG_INFO("server.loading", "Initialize Game Time and Timers");
    LOG_INFO("server.loading", " ");

    U32 startupDuration = GetMSTimeDiffToNow(startupBegin);

    LOG_INFO("server.loading", " ");
    LOG_INFO("server.loading", "WORLD: World Initialized In {} Minutes {} Seconds", (startupDuration / 60000), ((startupDuration % 60000) / 1000)); // outError for red color in console
    LOG_INFO("server.loading", " ");

}

/// Update the World !
void World::Update(U32 diff)
{
    ///- Update the game time and check for shutdown time
    _UpdateGameTime();
    Seconds currentGameTime = GameTime::GetGameTime();

    //sWorldUpdateTime.UpdateWithDiff(diff);

    // Record update if recording set in log and diff is greater then minimum set in log
    //sWorldUpdateTime.RecordUpdateTime(GameTime::GetGameTimeMS(), diff, GetActiveSessionCount());

    //DynamicVisibilityMgr::Update(GetActiveSessionCount());

    ///- Update the different timers
    for (int i = 0; i < WUPDATE_COUNT; ++i)
    {
        if (_timers[i].GetCurrent() >= 0)
            _timers[i].Update(diff);
        else
            _timers[i].SetCurrent(0);
    }

    UpdateSessions(diff);

    ///- Ping to keep MySQL connections alive
    //if (_timers[WUPDATE_PINGDB].Passed())
    //{
    //    _timers[WUPDATE_PINGDB].Reset();
        LOG_DEBUG("sql.driver", "Ping MySQL to keep connection alive");
    //    CharacterDatabase.KeepAlive();
    //    LoginDatabase.KeepAlive();
    //    WorldDatabase.KeepAlive();
    //}

    {
        // And last, but not least handle the issued cli commands
        ProcessCliCommands();
    }

    {
        //playersSaveScheduler.Update(diff);
    }
}

void World::ForceGameEventUpdate()
{

}

/// Send a System Message to all players (except self if mentioned)
void World::SendWorldText(U32 string_id, ...)
{

}

void World::SendWorldTextOptional(U32 string_id, U32 flag, ...)
{

}

/// Send a System Message to all GMs (except self if mentioned)
void World::SendGMText(U32 string_id, ...)
{

}

/// Kick (and save) all players
void World::KickAll()
{
    _queuedPlayer.clear();                                 // prevent send queue update packet and login queued sessions

    // session not removed at kick and will removed in next update tick
    for (SessionMap::const_iterator itr = _sessions.begin(); itr != _sessions.end(); ++itr)
        itr->second->KickPlayer("KickAll sessions");

    // pussywizard: kick offline sessions
    for (SessionMap::const_iterator itr = _offlineSessions.begin(); itr != _offlineSessions.end(); ++itr)
        itr->second->KickPlayer("KickAll offline sessions");
}

/// Update the game time
void World::_UpdateGameTime()
{
    ///- update the time
    Seconds lastGameTime = GameTime::GetGameTime();
    GameTime::UpdateGameTimers();

    Seconds elapsed = GameTime::GetGameTime() - lastGameTime;

    ///- if there is a shutdown timer
    if (!IsStopped() && _shutdownTimer > 0 && elapsed > 0s)
    {
        ///- ... and it is overdue, stop the world (set m_stopEvent)
        if (_shutdownTimer <= elapsed.count())
        {
            if (!(_shutdownMask & SHUTDOWN_MASK_IDLE) || GetActiveAndQueuedSessionCount() == 0)
                _stopEvent = true;                         // exist code already set
            else
                _shutdownTimer = 1;                        // minimum timer value to wait idle state
        }
            ///- ... else decrease it and if necessary display a shutdown countdown to the users
        else
        {
            _shutdownTimer -= elapsed.count();

            ShutdownMsg();
        }
    }
}

/// Shutdown the server
void World::ShutdownServ(U32 time, U32 options, U8 exitcode, const std::string& reason)
{
    // ignore if server shutdown at next tick
    if (IsStopped())
        return;

    _shutdownMask = options;
    _exitCode = exitcode;

    auto const& playersOnline = GetActiveSessionCount();

    if (time < 5 && playersOnline)
    {
        // Set time to 5s for save all players
        time = 5;
    }

    LOG_WARN("server", "Time left until shutdown/restart: {}", time);

    ///- If the shutdown time is 0, set m_stopEvent (except if shutdown is 'idle' with remaining sessions)
    if (time == 0)
    {
        if (!(options & SHUTDOWN_MASK_IDLE) || GetActiveAndQueuedSessionCount() == 0)
            _stopEvent = true;                             // exist code already set
        else
            _shutdownTimer = 1;                            //So that the session count is re-evaluated at next world tick
    }
        ///- Else set the shutdown timer and warn users
    else
    {
        _shutdownTimer = time;
        ShutdownMsg(true, nullptr, reason);
    }
}

/// Display a shutdown message to the user(s)
void World::ShutdownMsg(bool show, Player* player, const std::string& reason)
{
    // not show messages for idle shutdown mode
    if (_shutdownMask & SHUTDOWN_MASK_IDLE)
        return;

    ///- Display a message every 12 hours, hours, 5 minutes, minute, 5 seconds and finally seconds
    if (show ||
        (_shutdownTimer < 5 * MINUTE && (_shutdownTimer % 15) == 0) || // < 5 min; every 15 sec
        (_shutdownTimer < 15 * MINUTE && (_shutdownTimer % MINUTE) == 0) || // < 15 min ; every 1 min
        (_shutdownTimer < 30 * MINUTE && (_shutdownTimer % (5 * MINUTE)) == 0) || // < 30 min ; every 5 min
        (_shutdownTimer < 12 * HOUR && (_shutdownTimer % HOUR) == 0) || // < 12 h ; every 1 h
        (_shutdownTimer > 12 * HOUR && (_shutdownTimer % (12 * HOUR)) == 0)) // > 12 h ; every 12 h
    {
        std::string str = secsToTimeString(_shutdownTimer).append(".");

        if (!reason.empty())
        {
            str += " - " + reason;
        }

        ServerMessageType msgid = (_shutdownMask & SHUTDOWN_MASK_RESTART) ? SERVER_MSG_RESTART_TIME : SERVER_MSG_SHUTDOWN_TIME;

        SendServerMessage(msgid, str, player);
        LOG_DEBUG("server.worldserver", "Server is {} in {}", (_shutdownMask & SHUTDOWN_MASK_RESTART ? "restart" : "shuttingdown"), str);
    }
}

/// Cancel a planned server shutdown
void World::ShutdownCancel()
{
    // nothing cancel or too later
    if (!_shutdownTimer || _stopEvent)
        return;

    ServerMessageType msgid = (_shutdownMask & SHUTDOWN_MASK_RESTART) ? SERVER_MSG_RESTART_CANCELLED : SERVER_MSG_SHUTDOWN_CANCELLED;

    _shutdownMask = 0;
    _shutdownTimer = 0;
    _exitCode = SHUTDOWN_EXIT_CODE;                       // to default value
    SendServerMessage(msgid);

    LOG_DEBUG("server.worldserver", "Server {} cancelled.", (_shutdownMask & SHUTDOWN_MASK_RESTART ? "restart" : "shuttingdown"));
}

/// Send a server message to the user(s)
void World::SendServerMessage(ServerMessageType messageID, std::string stringParam /*= ""*/, Player* player /*= nullptr*/)
{

}

void World::UpdateSessions(U32 diff)
{

}

// This handles the issued and queued CLI commands
void World::ProcessCliCommands()
{
    CliCommandHolder::Print zprint = nullptr;
    void* callbackArg = nullptr;
    CliCommandHolder* command = nullptr;
    while (_cliCmdQueue.next(command))
    {
        LOG_DEBUG("server.worldserver", "CLI command under processing...");
        zprint = command->m_print;
        callbackArg = command->m_callbackArg;
        CliHandler handler(callbackArg, zprint);
        handler.ParseCommands(command->m_command);
        if (command->m_commandFinished)
            command->m_commandFinished(callbackArg, !handler.HasSentErrorMessage());
        delete command;
    }
}

void World::UpdateRealmCharCount(U32 accountId)
{

}

void World::UpdateMaxSessionCounters()
{
    _maxActiveSessionCount = std::max(_maxActiveSessionCount, U32(_sessions.size() - _queuedPlayer.size()));
    _maxQueuedSessionCount = std::max(_maxQueuedSessionCount, U32(_queuedPlayer.size()));
}

