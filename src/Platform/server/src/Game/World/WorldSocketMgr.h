#pragma once

#include <Network/SocketMgr.h>

class WorldSocket;

/// Manages all sockets connected to peers and network threads
class WorldSocketMgr : public SocketMgr<WorldSocket>
{
    typedef SocketMgr<WorldSocket> BaseSocketMgr;

public:
    static WorldSocketMgr& Instance();

    /// Start network, listen at address:port .
    bool StartWorldNetwork(Avalon::Asio::IoContext& ioContext, std::string const& bindIp, U16 port, S32 networkThreads);

    /// Stops all network threads, It will wait for all running threads .
    void StopNetwork() override;

    void OnSocketOpen(tcp::socket&& sock, U32 threadIndex) override;

    std::size_t GetApplicationSendBufferSize() const { return _socketApplicationSendBufferSize; }

protected:
    WorldSocketMgr();

    NetworkThread<WorldSocket>* CreateThreads() const override;

    static void OnSocketAccept(tcp::socket&& sock, U32 threadIndex)
    {
        Instance().OnSocketOpen(std::forward<tcp::socket>(sock), threadIndex);
    }

private:
    S32 _socketSystemSendBufferSize;
    S32 _socketApplicationSendBufferSize;
    bool _tcpNoDelay;
};

#define sWorldSocketMgr WorldSocketMgr::Instance()
