#pragma once

#include <Common/Types.h>
#include <Utilities/Duration.h>

#include "unordered_map"

#define MIN_MAP_UPDATE_DELAY    1

enum ServerMessageType
{
    SERVER_MSG_SHUTDOWN_TIME      = 1,
    SERVER_MSG_RESTART_TIME       = 2,
    SERVER_MSG_STRING             = 3,
    SERVER_MSG_SHUTDOWN_CANCELLED = 4,
    SERVER_MSG_RESTART_CANCELLED  = 5
};

class WorldSession;
class Player;

/// Storage class for commands issued for delayed execution
struct CliCommandHolder
{
    using Print = void(*)(void*, std::string_view);
    using CommandFinished = void(*)(void*, bool success);

    void* m_callbackArg;
    char* m_command;
    Print m_print;
    CommandFinished m_commandFinished;

    CliCommandHolder(void* callbackArg, char const* command, Print zprint, CommandFinished commandFinished)
    : m_callbackArg(callbackArg), m_command(strdup(command)), m_print(zprint), m_commandFinished(commandFinished)
    { }
    ~CliCommandHolder(){
        free(m_command);
    }

    private:
    CliCommandHolder(CliCommandHolder const& right) = delete;
    CliCommandHolder& operator=(CliCommandHolder const& right) = delete;
};

using SessionMap = std::unordered_map<U32, WorldSession *>;

class IWorld
{
public:
    virtual ~IWorld() = default;
    [[nodiscard]] virtual WorldSession* FindSession(U32 id) const = 0;
    [[nodiscard]] virtual WorldSession* FindOfflineSession(U32 id) const = 0;
    virtual void AddSession(WorldSession* s) = 0;
    virtual bool KickSession(U32 id) = 0;
    virtual void UpdateMaxSessionCounters() = 0;
    [[nodiscard]] virtual const SessionMap& GetAllSessions() const = 0;
    [[nodiscard]] virtual U32 GetActiveAndQueuedSessionCount() const = 0;
    [[nodiscard]] virtual U32 GetActiveSessionCount() const = 0;
    [[nodiscard]] virtual U32 GetQueuedSessionCount() const = 0;
    [[nodiscard]] virtual U32 GetMaxQueuedSessionCount() const = 0;
    [[nodiscard]] virtual U32 GetMaxActiveSessionCount() const = 0;
    [[nodiscard]] virtual U32 GetPlayerCount() const = 0;
    [[nodiscard]] virtual U32 GetMaxPlayerCount() const = 0;
    virtual void IncreasePlayerCount() = 0;
    virtual void DecreasePlayerCount() = 0;
    virtual Player* FindPlayerInZone(U32 zone) = 0;
    [[nodiscard]] virtual bool IsClosed() const = 0;
    virtual void SetClosed(bool val) = 0;
    virtual void SetPlayerAmountLimit(U32 limit) = 0;
    [[nodiscard]] virtual U32 GetPlayerAmountLimit() const = 0;
    virtual void AddQueuedPlayer(WorldSession*) = 0;
    virtual bool RemoveQueuedPlayer(WorldSession* session) = 0;
    virtual S32 GetQueuePos(WorldSession*) = 0;
    virtual bool HasRecentlyDisconnected(WorldSession*) = 0;
    [[nodiscard]] virtual bool getAllowMovement() const = 0;
    virtual void SetAllowMovement(bool allow) = 0;
    virtual void SetNewCharString(std::string const& str) = 0;
    [[nodiscard]] virtual std::string const& GetNewCharString() const = 0;
    [[nodiscard]] virtual std::string const& GetDataPath() const = 0;
    [[nodiscard]] virtual Seconds GetNextDailyQuestsResetTime() const = 0;
    [[nodiscard]] virtual Seconds GetNextWeeklyQuestsResetTime() const = 0;
    [[nodiscard]] virtual Seconds GetNextRandomBGResetTime() const = 0;
    virtual void SetInitialWorldSettings() = 0;
    virtual void LoadConfigSettings(bool reload = false) = 0;
    virtual void SendWorldText(U32 string_id, ...) = 0;
    virtual void SendWorldTextOptional(U32 string_id, U32 flag, ...) = 0;
    virtual void SendGMText(U32 string_id, ...) = 0;
    virtual void SendServerMessage(ServerMessageType messageID, std::string stringParam = "", Player* player = nullptr) = 0;
    [[nodiscard]] virtual bool IsShuttingDown() const = 0;
    [[nodiscard]] virtual U32 GetShutDownTimeLeft() const = 0;
    virtual void ShutdownServ(U32 time, U32 options, U8 exitcode, const std::string& reason = std::string()) = 0;
    virtual void ShutdownCancel() = 0;
    virtual void ShutdownMsg(bool show = false, Player* player = nullptr, const std::string& reason = std::string()) = 0;
    virtual void Update(U32 diff) = 0;
    virtual void UpdateSessions(U32 diff) = 0;
    virtual void KickAll() = 0;
    virtual void KickAllLess(AccountTypes sec) = 0;
    virtual void ProcessCliCommands() = 0;
    virtual void QueueCliCommand(CliCommandHolder* commandHolder) = 0;
    virtual void ForceGameEventUpdate() = 0;
    virtual void UpdateRealmCharCount(U32 accid) = 0;
    [[nodiscard]] virtual char const* GetDBVersion() const = 0;
    [[nodiscard]] virtual U32 GetCleaningFlags() const = 0;
    virtual void   SetCleaningFlags(U32 flags) = 0;
    [[nodiscard]] virtual std::string const& GetRealmName() const = 0;
    virtual void SetRealmName(std::string name) = 0;
    [[nodiscard]] virtual AccountTypes GetPlayerSecurityLimit() const = 0;
    virtual void SetPlayerSecurityLimit(AccountTypes sec) = 0;
};
