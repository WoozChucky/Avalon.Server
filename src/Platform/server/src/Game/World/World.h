#include "IWorld.h"
#include "Threading/LockedQueue.h"

#include <Utilities/Timer.h>

#include <memory>
#include <map>
#include <list>

enum ShutdownMask
{
    SHUTDOWN_MASK_RESTART = 1,
    SHUTDOWN_MASK_IDLE    = 2,
};

enum ShutdownExitCode
{
    SHUTDOWN_EXIT_CODE = 0,
    ERROR_EXIT_CODE    = 1,
    RESTART_EXIT_CODE  = 2,
};

enum WorldTimers
{
    WUPDATE_AUCTIONS,
    WUPDATE_WEATHERS,
    WUPDATE_UPTIME,
    WUPDATE_CORPSES,
    WUPDATE_EVENTS,
    WUPDATE_CLEANDB,
    WUPDATE_AUTOBROADCAST,
    WUPDATE_MAILBOXQUEUE,
    WUPDATE_PINGDB,
    WUPDATE_5_SECS,
    WUPDATE_WHO_LIST,
    WUPDATE_COUNT
};

/// The World
class World: public IWorld
{
public:
    World();
    ~World() override;

    static World* instance();

    static U32 m_worldLoopCounter;

    [[nodiscard]] WorldSession* FindSession(U32 id) const override;
    [[nodiscard]] WorldSession* FindOfflineSession(U32 id) const override;
    void AddSession(WorldSession* s) override;
    bool KickSession(U32 id) override;
    /// Get the number of current active sessions
    void UpdateMaxSessionCounters() override;
    [[nodiscard]] const SessionMap& GetAllSessions() const override { return _sessions; }
    [[nodiscard]] U32 GetActiveAndQueuedSessionCount() const override { return _sessions.size(); }
    [[nodiscard]] U32 GetActiveSessionCount() const override { return _sessions.size() - _queuedPlayer.size(); }
    [[nodiscard]] U32 GetQueuedSessionCount() const override { return _queuedPlayer.size(); }
    /// Get the maximum number of parallel sessions on the server since last reboot
    [[nodiscard]] U32 GetMaxQueuedSessionCount() const override { return _maxQueuedSessionCount; }
    [[nodiscard]] U32 GetMaxActiveSessionCount() const override { return _maxActiveSessionCount; }
    /// Get number of players
    [[nodiscard]] inline U32 GetPlayerCount() const override { return _playerCount; }
    [[nodiscard]] inline U32 GetMaxPlayerCount() const override { return _maxPlayerCount; }

    /// Increase/Decrease number of players
    inline void IncreasePlayerCount() override
    {
        _playerCount++;
        _maxPlayerCount = std::max(_maxPlayerCount, _playerCount);
    }
    inline void DecreasePlayerCount() override { _playerCount--; }

    Player* FindPlayerInZone(U32 zone) override;

    /// Deny clients?
    [[nodiscard]] bool IsClosed() const override;

    /// Close world
    void SetClosed(bool val) override;

    /// Active session server limit
    void SetPlayerAmountLimit(U32 limit) override { _playerLimit = limit; }
    [[nodiscard]] U32 GetPlayerAmountLimit() const override { return _playerLimit; }

    //player Queue
    using Queue = std::list<WorldSession *>;
    void AddQueuedPlayer(WorldSession*) override;
    bool RemoveQueuedPlayer(WorldSession* session) override;
    S32 GetQueuePos(WorldSession*) override;
    bool HasRecentlyDisconnected(WorldSession*) override;

    /// \todo Actions on m_allowMovement still to be implemented
    /// Is movement allowed?
    [[nodiscard]] bool getAllowMovement() const override { return _allowMovement; }
    /// Allow/Disallow object movements
    void SetAllowMovement(bool allow) override { _allowMovement = allow; }

    /// Set the string for new characters (first login)
    void SetNewCharString(std::string const& str) override { _newCharString = str; }
    /// Get the string for new characters (first login)
    [[nodiscard]] std::string const& GetNewCharString() const override { return _newCharString; }

    /// Get the path where data (dbc, maps) are stored on disk
    [[nodiscard]] std::string const& GetDataPath() const override { return _dataPath; }

    /// Next daily quests and random bg reset time
    [[nodiscard]] Seconds GetNextDailyQuestsResetTime() const override { return _nextDailyQuestReset; }
    [[nodiscard]] Seconds GetNextWeeklyQuestsResetTime() const override { return _nextWeeklyQuestReset; }
    [[nodiscard]] Seconds GetNextRandomBGResetTime() const override { return _nextRandomBGReset; }

    void SetInitialWorldSettings() override;
    void LoadConfigSettings(bool reload = false) override;

    void SendWorldText(U32 string_id, ...) override;
    void SendGMText(U32 string_id, ...) override;
    void SendServerMessage(ServerMessageType messageID, std::string stringParam = "", Player* player = nullptr) override;

    void SendWorldTextOptional(U32 string_id, U32 flag, ...) override;

    /// Are we in the middle of a shutdown?
    [[nodiscard]] bool IsShuttingDown() const override { return _shutdownTimer > 0; }
    [[nodiscard]] U32 GetShutDownTimeLeft() const override { return _shutdownTimer; }
    void ShutdownServ(U32 time, U32 options, U8 exitcode, const std::string& reason = std::string()) override;
    void ShutdownCancel() override;
    void ShutdownMsg(bool show = false, Player* player = nullptr, const std::string& reason = std::string()) override;
    static U8 GetExitCode() { return _exitCode; }
    static void StopNow(U8 exitcode) { _stopEvent = true; _exitCode = exitcode; }
    static bool IsStopped() { return _stopEvent; }

    void Update(U32 diff) override;

    void UpdateSessions(U32 diff) override;

    /// Are we on a "Player versus Player" server?
    void KickAll() override;

    // for max speed access
    static float GetMaxVisibleDistanceOnContinents()    { return _maxVisibleDistanceOnContinents; }
    static float GetMaxVisibleDistanceInInstances()     { return _maxVisibleDistanceInInstances;  }
    static float GetMaxVisibleDistanceInBGArenas()      { return _maxVisibleDistanceInBGArenas;   }


    void ProcessCliCommands() override;
    void QueueCliCommand(CliCommandHolder* commandHolder) override { _cliCmdQueue.add(commandHolder); }

    void ForceGameEventUpdate() override;

    void UpdateRealmCharCount(U32 accid) override;

    [[nodiscard]] char const* GetDBVersion() const override { return _dbVersion.c_str(); }


    [[nodiscard]] U32 GetCleaningFlags() const override { return _cleaningFlags; }
    void   SetCleaningFlags(U32 flags) override { _cleaningFlags = flags; }

    [[nodiscard]] std::string const& GetRealmName() const override { return _realmName; } // pussywizard
    void SetRealmName(std::string name) override { _realmName = name; } // pussywizard

protected:
    void _UpdateGameTime();
    // callback for UpdateRealmCharacters
private:
    static std::atomic_long _stopEvent;
    static U8 _exitCode;
    U32 _shutdownTimer;
    U32 _shutdownMask;

    U32 _cleaningFlags;

    bool _isClosed;

    IntervalTimer _timers[WUPDATE_COUNT];
    Seconds _mail_expire_check_timer;

    SessionMap _sessions;
    SessionMap _offlineSessions;
    using DisconnectMap = std::unordered_map<U32, time_t>;
    DisconnectMap _disconnects;
    U32 _maxActiveSessionCount;
    U32 _maxQueuedSessionCount;
    U32 _playerCount;
    U32 _maxPlayerCount;

    std::string _newCharString;

    typedef std::map<U32, U64> WorldStatesMap;
    WorldStatesMap _worldstates;
    U32 _playerLimit;
    U32 _availableDbcLocaleMask;                       // by loaded DBC
    void DetectDBCLang();
    bool _allowMovement;
    std::string _dataPath;

    // for max speed access
    static float _maxVisibleDistanceOnContinents;
    static float _maxVisibleDistanceInInstances;
    static float _maxVisibleDistanceInBGArenas;

    std::string _realmName;

    // CLI command holder to be thread safe
    LockedQueue<CliCommandHolder*> _cliCmdQueue;

    // next daily quests and random bg reset time
    Seconds _nextDailyQuestReset;
    Seconds _nextWeeklyQuestReset;
    Seconds _nextMonthlyQuestReset;
    Seconds _nextRandomBGReset;
    Seconds _nextCalendarOldEventsDeletionTime;
    Seconds _nextGuildReset;

    //Player Queue
    Queue _queuedPlayer;

    // sessions that are added async
    void AddSession_(WorldSession* s);
    LockedQueue<WorldSession*> _addSessQueue;

    // used versions
    std::string _dbVersion;

    /**
     * @brief Executed when a World Session is being finalized. Be it from a normal login or via queue popping.
     *
     * @param session The World Session that we are finalizing.
     */
    inline void FinalizePlayerWorldSession(WorldSession* session);
};

std::unique_ptr<IWorld>& getWorldInstance();

#define sWorld getWorldInstance()
