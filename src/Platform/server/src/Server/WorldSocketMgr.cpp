#include "WorldSocketMgr.h"

#include "Common/Configuration/ConfigManager.h"
#include "Common/Network/NetworkThread.h"

#include "WorldSocket.h"
#include "boost/system/error_code.hpp"

class WorldSocketThread : public NetworkThread<WorldSocket>
{
public:
    void SocketAdded(std::shared_ptr<WorldSocket> sock) override
    {
        sock->SetSendBufferSize(sWorldSocketMgr.GetApplicationSendBufferSize());
    }

    void SocketRemoved(std::shared_ptr<WorldSocket> sock) override
    {
    }
};

WorldSocketMgr::WorldSocketMgr() :
    BaseSocketMgr(), _socketSystemSendBufferSize(-1), _socketApplicationSendBufferSize(65536), _tcpNoDelay(true)
{
}

WorldSocketMgr& WorldSocketMgr::Instance()
{
    static WorldSocketMgr instance;
    return instance;
}

bool WorldSocketMgr::StartWorldNetwork(Avalon::Asio::IoContext& ioContext, std::string const& bindIp, U16 port, S32 threadCount)
{
    _tcpNoDelay = sConfigMgr->GetOption<bool>("Network.TcpNodelay", true);

    int const max_connections = AVALON_MAX_LISTEN_CONNECTIONS;
    LOG_DEBUG("network", "Max allowed socket connections {}", max_connections);

    // -1 means use default
    _socketSystemSendBufferSize = sConfigMgr->GetOption<S32>("Network.OutKBuff", -1);
    _socketApplicationSendBufferSize = sConfigMgr->GetOption<S32>("Network.OutUBuff", 65536);

    if (_socketApplicationSendBufferSize <= 0)
    {
        LOG_ERROR("network", "Network.OutUBuff is wrong in your config file");
        return false;
    }

    if (!BaseSocketMgr::StartNetwork(ioContext, bindIp, port, threadCount))
        return false;

    _acceptor->AsyncAcceptWithCallback<&WorldSocketMgr::OnSocketAccept>();

    return true;
}

void WorldSocketMgr::StopNetwork()
{
    BaseSocketMgr::StopNetwork();
}

void WorldSocketMgr::OnSocketOpen(tcp::socket&& sock, U32 threadIndex)
{
    // set some options here
    if (_socketSystemSendBufferSize >= 0)
    {
        boost::system::error_code err;
        sock.set_option(boost::asio::socket_base::send_buffer_size(_socketSystemSendBufferSize), err);

        if (err && err != boost::system::errc::not_supported)
        {
            LOG_ERROR("network", "WorldSocketMgr::OnSocketOpen sock.set_option(boost::asio::socket_base::send_buffer_size) err = {}", err.message());
            return;
        }
    }

    // Set TCP_NODELAY.
    if (_tcpNoDelay)
    {
        boost::system::error_code err;
        sock.set_option(boost::asio::ip::tcp::no_delay(true), err);

        if (err)
        {
            LOG_ERROR("network", "WorldSocketMgr::OnSocketOpen sock.set_option(boost::asio::ip::tcp::no_delay) err = {}", err.message());
            return;
        }
    }

    BaseSocketMgr::OnSocketOpen(std::forward<tcp::socket>(sock), threadIndex);
}

NetworkThread<WorldSocket>* WorldSocketMgr::CreateThreads() const
{
    return new WorldSocketThread[GetNetworkThreadCount()];
}
