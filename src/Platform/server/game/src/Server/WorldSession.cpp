#include <Game/Server/WorldSession.h>

#include <Game/Server/WorldSocket.h>

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
