#pragma once

#include <Common/Types.h>
#include <Database/DatabaseEnv.h>
#include <Database/DatabaseEnvFwd.h>
#include <Common/Utilities/AsyncCallbackProcessor.h>
#include <Common/Utilities/CircularBuffer.h>
#include <Common/Cryptography/Authentication/AuthDefines.h>
#include <Common/Configuration/ConfigManager.h>
#include <Game/Server/WorldPacket.h>
#include <Game/Server/Packet.h>
#include <Game/World/World.h>
#include <Game/Accounts/AccountMgr.h>
#include <Game/Entities/Object/ObjectGuid.h>

#include <map>
#include <utility>

class WorldSocket;
class LoginQueryHolder;

struct AccountData
{
    AccountData() :  Data("") {}

    time_t Time{0};
    std::string Data;
};

enum PartyOperation
{
    PARTY_OP_INVITE = 0,
    PARTY_OP_UNINVITE = 1,
    PARTY_OP_LEAVE = 2,
    PARTY_OP_SWAP = 4
};

enum BFLeaveReason
{
    BF_LEAVE_REASON_CLOSE     = 0x00000001,
    //BF_LEAVE_REASON_UNK1      = 0x00000002, (not used)
    //BF_LEAVE_REASON_UNK2      = 0x00000004, (not used)
    BF_LEAVE_REASON_EXITED    = 0x00000008,
    BF_LEAVE_REASON_LOW_LEVEL = 0x00000010,
};

enum ChatRestrictionType
{
    ERR_CHAT_RESTRICTED = 0,
    ERR_CHAT_THROTTLED  = 1,
    ERR_USER_SQUELCHED  = 2,
    ERR_YELL_RESTRICTED = 3
};

enum DeclinedNameResult
{
    DECLINED_NAMES_RESULT_SUCCESS = 0,
    DECLINED_NAMES_RESULT_ERROR = 1
};

enum AccountDataType
{
    GLOBAL_CONFIG_CACHE             = 0,                    // 0x01 g
    PER_CHARACTER_CONFIG_CACHE      = 1,                    // 0x02 p
    GLOBAL_BINDINGS_CACHE           = 2,                    // 0x04 g
    PER_CHARACTER_BINDINGS_CACHE    = 3,                    // 0x08 p
    GLOBAL_MACROS_CACHE             = 4,                    // 0x10 g
    PER_CHARACTER_MACROS_CACHE      = 5,                    // 0x20 p
    PER_CHARACTER_LAYOUT_CACHE      = 6,                    // 0x40 p
    PER_CHARACTER_CHAT_CACHE        = 7,                    // 0x80 p
};

#define NUM_ACCOUNT_DATA_TYPES        8

struct CharacterRenameInfo
{
    friend class WorldSession;

protected:
    ObjectGuid Guid;
    std::string Name;
};

namespace WorldPackets {
    namespace Character {
        class PlayedTimeClient;
    }
}

//class to deal with packet processing
//allows to determine if next packet is safe to be processed
class PacketFilter
{
public:
    explicit PacketFilter(WorldSession* pSession) : m_pSession(pSession) {}
    virtual ~PacketFilter() = default;

    virtual bool Process(WorldPacket* /*packet*/) { return true; }
    [[nodiscard]] virtual bool ProcessUnsafe() const { return true; }

protected:
    WorldSession* const m_pSession;
};
//process only thread-safe packets in Map::Update()
class MapSessionFilter : public PacketFilter
{
public:
    explicit MapSessionFilter(WorldSession* pSession) : PacketFilter(pSession) {}
    ~MapSessionFilter() override = default;

    bool Process(WorldPacket* packet) override;
    //in Map::Update() we do not process player logout!
    [[nodiscard]] bool ProcessUnsafe() const override { return false; }
};

//class used to filer only thread-unsafe packets from queue
//in order to update only be used in World::UpdateSessions()
class WorldSessionFilter : public PacketFilter
{
public:
    explicit WorldSessionFilter(WorldSession* pSession) : PacketFilter(pSession) {}
    ~WorldSessionFilter() override = default;

    bool Process(WorldPacket* packet) override;
};

// Proxy structure to contain data passed to callback function,
// only to prevent bloating the parameter list
class CharacterCreateInfo
{
    friend class WorldSession;
    friend class Player;

protected:
    /// User specified variables
    std::string Name;
    U8 Race = 0;
    U8 Class = 0;
    U8 Gender = 0;
    U8 Skin = 0;
    U8 Face = 0;
    U8 HairStyle = 0;
    U8 HairColor = 0;
    U8 FacialHair = 0;
    U8 OutfitId = 0;

    /// Server side data
    U8 CharCount = 0;
};

struct PacketCounter
{
    time_t lastReceiveTime;
    U32 amountCounter;
};

struct MovementInfo;

/// Player session in the World
class WorldSession
{
public:
    WorldSession(U32 id, std::string&& name, std::shared_ptr<WorldSocket> sock, AccountTypes sec, time_t mute_time, LocaleConstant locale, bool skipQueue, U32 TotalTime);
    ~WorldSession();

    bool PlayerLoading() const { return m_playerLoading; }
    bool PlayerLogout() const { return m_playerLogout; }
    bool PlayerRecentlyLoggedOut() const { return m_playerRecentlyLogout; }
    bool PlayerLogoutWithSave() const { return m_playerLogout && m_playerSave; }

    void ReadAddonsInfo(ByteBuffer& data);
    void SendAddonsInfo();

    void ReadMovementInfo(WorldPacket& data, MovementInfo* mi);
    void WriteMovementInfo(WorldPacket* data, MovementInfo* mi);

    void SendPacket(WorldPacket const* packet);
    void SendNotification(const char* format, ...) ATTR_PRINTF(2, 3);
    void SendNotification(U32 string_id, ...);
    void SendQueryTimeResponse();

    void SendAuthResponse(U8 code, bool shortForm, U32 queuePos = 0);
    void SendClientCacheVersion(U32 version);

    AccountTypes GetSecurity() const { return _security; }
    bool CanSkipQueue() const { return _skipQueue; }
    U32 GetAccountId() const { return _accountId; }
    Player* GetPlayer() const { return _player; }
    std::string const& GetPlayerName() const;
    std::string GetPlayerInfo() const;

    ObjectGuid::LowType GetGuidLow() const;
    void SetSecurity(AccountTypes security) { _security = security; }
    std::string const& GetRemoteAddress() { return m_Address; }
    void SetPlayer(Player* player);

    void SetTotalTime(U32 TotalTime) { m_total_time = TotalTime; }
    U32 GetTotalTime() const { return m_total_time; }

    /// Session in auth.queue currently
    void SetInQueue(bool state) { m_inQueue = state; }

    /// Is the user engaged in a log out process?
    bool isLogingOut() const { return _logoutTime || m_playerLogout; }

    /// Engage the logout process for the user
    void SetLogoutStartTime(time_t requestTime)
    {
        _logoutTime = requestTime;
    }

    /// Is logout cooldown expired?
    bool ShouldLogOut(time_t currTime) const
    {
        return (_logoutTime > 0 && currTime >= _logoutTime + 20);
    }

    void LogoutPlayer(bool save);
    void KickPlayer(bool setKicked = true) { return this->KickPlayer("Unknown reason", setKicked); }
    void KickPlayer(std::string const& reason, bool setKicked = true);

    void QueuePacket(WorldPacket* new_packet);
    bool Update(U32 diff, PacketFilter& updater);

    /// Handle the authentication waiting queue (to be completed)
    void SendAuthWaitQueue(U32 position);

    void SendNameQueryOpcode(ObjectGuid guid);

    // Account Data
    AccountData* GetAccountData(AccountDataType type) { return &m_accountData[type]; }
    void SetAccountData(AccountDataType type, time_t tm, std::string const& data);
    void SendAccountDataTimes(U32 mask);
    void LoadAccountData(PreparedQueryResult result, U32 mask);

    // Account mute time
    time_t m_muteTime;

    // Locales
    LocaleConstant GetSessionDbcLocale() const { return m_sessionDbcLocale; }
    LocaleConstant GetSessionDbLocaleIndex() const { return m_sessionDbLocaleIndex; }
    char const* GetAvalonString(U32 entry) const;

    U32 GetLatency() const { return m_latency; }
    void SetLatency(U32 latency) { m_latency = latency; }

    std::atomic<time_t> m_timeOutTime;
    void UpdateTimeOutTime(U32 diff)
    {
        if (time_t(diff) > m_timeOutTime)
            m_timeOutTime = 0;
        else
            m_timeOutTime -= diff;
    }
    void ResetTimeOutTime(bool onlyActive)
    {
        if (GetPlayer())
            m_timeOutTime = S32(sConfigMgr->GetOption<S32>("SocketTimeOutTimeActive", 60000));
        else if (!onlyActive)
            m_timeOutTime = S32(sConfigMgr->GetOption<S32>("SocketTimeOutTime", 900000));
    }
    bool IsConnectionIdle() const
    {
        return (m_timeOutTime <= 0 && !m_inQueue);
    }

    // Packets cooldown
    time_t GetCalendarEventCreationCooldown() const { return _calendarEventCreationCooldown; }
    void SetCalendarEventCreationCooldown(time_t cooldown) { _calendarEventCreationCooldown = cooldown; }

    // Time Synchronisation
    void ResetTimeSync();
    void SendTimeSync();
public:                                                 // opcodes handlers
    void Handle_NULL(WorldPacket& null);                // not used
    void Handle_EarlyProccess(WorldPacket& recvPacket); // just mark packets processed in WorldSocket::OnRead
    void Handle_ServerSide(WorldPacket& recvPacket);    // sever side only, can't be accepted from client
    void Handle_Deprecated(WorldPacket& recvPacket);    // never used anymore by client

    void HandleCharEnumOpcode(WorldPacket& recvPacket);
    void HandleCharDeleteOpcode(WorldPacket& recvPacket);
    void HandleCharCreateOpcode(WorldPacket& recvPacket);
    void HandlePlayerLoginOpcode(WorldPacket& recvPacket);
    void HandleCharEnum(PreparedQueryResult result);
    void HandlePlayerLoginFromDB(LoginQueryHolder const& holder);
    void HandlePlayerLoginToCharInWorld(Player* pCurrChar);
    void HandlePlayerLoginToCharOutOfWorld(Player* pCurrChar);

    void SendCharCreate(ResponseCodes result);
    void SendCharDelete(ResponseCodes result);
    void SendCharRename(ResponseCodes result, CharacterRenameInfo const* renameInfo);
    void SendSetPlayerDeclinedNamesResult(DeclinedNameResult result, ObjectGuid guid);

    // played time
    void HandlePlayedTime(WorldPackets::Character::PlayedTimeClient& packet);

    // new
    void HandleMoveUnRootAck(WorldPacket& recvPacket);
    void HandleMoveRootAck(WorldPacket& recvPacket);

    void HandleSpellClick(WorldPacket& recvData);

    void HandleTeleportTimeout(bool updateInSessions);
    bool HandleSocketClosed();
    void SetOfflineTime(U32 time) { _offlineTime = time; }
    U32 GetOfflineTime() const { return _offlineTime; }
    bool IsKicked() const { return _kicked; }
    void SetKicked(bool val) { _kicked = val; }
    bool IsSocketClosed() const;

    /*
     * CALLBACKS
     */

    QueryCallbackProcessor& GetQueryProcessor() { return _queryProcessor; }
    TransactionCallback& AddTransactionCallback(TransactionCallback&& callback);
    SQLQueryHolderCallback& AddQueryHolderCallback(SQLQueryHolderCallback&& callback);

    void InitializeSession();
    void InitializeSessionCallback(CharacterDatabaseQueryHolder const& realmHolder, U32 clientCacheVersion);

private:
    void ProcessQueryCallbacks();

    QueryCallbackProcessor _queryProcessor;
    AsyncCallbackProcessor<TransactionCallback> _transactionCallbacks;
    AsyncCallbackProcessor<SQLQueryHolderCallback> _queryHolderProcessor;

    friend class World;
protected:
    class DosProtection
    {
        friend class World;
    public:
        DosProtection(WorldSession* s);
        bool EvaluateOpcode(WorldPacket& p, time_t time) const;
    protected:
        enum Policy
        {
            POLICY_LOG,
            POLICY_KICK,
            POLICY_BAN
        };

        U32 GetMaxPacketCounterAllowed(U16 opcode) const;

        WorldSession* Session;

    private:
        Policy _policy;
        typedef std::unordered_map<U16, PacketCounter> PacketThrottlingMap;
        // mark this member as "mutable" so it can be modified even in const functions
        mutable PacketThrottlingMap _PacketThrottlingMap;

        DosProtection(DosProtection const& right) = delete;
        DosProtection& operator=(DosProtection const& right) = delete;
    } AntiDOS;

private:
    // logging helper
    void LogUnexpectedOpcode(WorldPacket* packet, char const* status, const char* reason);
    void LogUnprocessedTail(WorldPacket* packet);

    // EnumData helpers
    bool IsLegitCharacterForAccount(ObjectGuid guid)
    {
        return _legitCharacters.find(guid) != _legitCharacters.end();
    }

    // this stores the GUIDs of the characters who can login
    // characters who failed on Player::BuildEnumData shouldn't login
    GuidSet _legitCharacters;

    ObjectGuid::LowType m_GUIDLow;                     // set logined or recently logout player (while m_playerRecentlyLogout set)
    Player* _player;
    std::shared_ptr<WorldSocket> m_Socket;
    std::string m_Address;

    AccountTypes _security;
    bool _skipQueue;
    U32 _accountId;
    std::string _accountName;
    U32 m_total_time;

    time_t _logoutTime;
    bool m_inQueue;                                     // session wait in auth.queue
    bool m_playerLoading;                               // code processed in LoginPlayer
    bool m_playerLogout;                                // code processed in LogoutPlayer
    bool m_playerRecentlyLogout;
    bool m_playerSave;
    LocaleConstant m_sessionDbcLocale;
    LocaleConstant m_sessionDbLocaleIndex;
    std::atomic<U32> m_latency;
    AccountData m_accountData[NUM_ACCOUNT_DATA_TYPES];
    LockedQueue<WorldPacket*> _recvQueue;
    U32 _offlineTime;
    bool _kicked;
    // Packets cooldown
    time_t _calendarEventCreationCooldown;

    // Addon Message count for Metric
    std::atomic<U32> _addonMessageReceiveCount;

    CircularBuffer<std::pair<S64, U32>> _timeSyncClockDeltaQueue; // first member: clockDelta. Second member: latency of the packet exchange that was used to compute that clockDelta.
    S64 _timeSyncClockDelta;
    void ComputeNewClockDelta();

    std::map<U32, U32> _pendingTimeSyncRequests; // key: counter. value: server time when packet with that counter was sent.
    U32 _timeSyncNextCounter;
    U32 _timeSyncTimer;

    WorldSession(WorldSession const& right) = delete;
    WorldSession& operator=(WorldSession const& right) = delete;
};
