#pragma once

#include <Common/Network/AsyncAcceptor.h>
#include <Common/Debugging/Errors.h>
#include <Common/Network/NetworkThread.h>

#include <boost/asio/ip/tcp.hpp>
#include <memory>

using boost::asio::ip::tcp;

template<class SocketType>
class SocketMgr
{
public:
    virtual ~SocketMgr()
    {
        ASSERT(!_threads && !_acceptor && !_threadCount, "StopNetwork must be called prior to SocketMgr destruction");
    }

    virtual bool StartNetwork(Avalon::Asio::IoContext& ioContext, std::string const& bindIp, U16 port, int threadCount)
    {
        ASSERT(threadCount > 0);

        AsyncAcceptor* acceptor = nullptr;
        try
        {
            acceptor = new AsyncAcceptor(ioContext, bindIp, port);
        }
        catch (boost::system::system_error const& err)
        {
            LOG_ERROR("network", "Exception caught in SocketMgr.StartNetwork ({}:{}): {}", bindIp, port, err.what());
            return false;
        }

        if (!acceptor->Bind())
        {
            LOG_ERROR("network", "StartNetwork failed to bind socket acceptor");
            delete acceptor;
            return false;
        }

        _acceptor = acceptor;
        _threadCount = threadCount;
        _threads = CreateThreads();

        ASSERT(_threads);

        for (S32 i = 0; i < _threadCount; ++i)
            _threads[i].Start();

        _acceptor->SetSocketFactory([this]() { return GetSocketForAccept(); });

        return true;
    }

    virtual void StopNetwork()
    {
        _acceptor->Close();

        if (_threadCount != 0)
            for (S32 i = 0; i < _threadCount; ++i)
                _threads[i].Stop();

        Wait();

        delete _acceptor;
        _acceptor = nullptr;
        delete[] _threads;
        _threads = nullptr;
        _threadCount = 0;
    }

    void Wait()
    {
        if (_threadCount != 0)
            for (S32 i = 0; i < _threadCount; ++i)
                _threads[i].Wait();
    }

    virtual void OnSocketOpen(tcp::socket&& sock, U32 threadIndex)
    {
        try
        {
            std::shared_ptr<SocketType> newSocket = std::make_shared<SocketType>(std::move(sock));
            newSocket->Start();

            _threads[threadIndex].AddSocket(newSocket);
        }
        catch (boost::system::system_error const& err)
        {
            LOG_WARN("network", "Failed to retrieve client's remote address {}", err.what());
        }
    }

    S32 GetNetworkThreadCount() const { return _threadCount; }

    U32 SelectThreadWithMinConnections() const
    {
        U32 min = 0;

        for (S32 i = 1; i < _threadCount; ++i)
            if (_threads[i].GetConnectionCount() < _threads[min].GetConnectionCount())
                min = i;

        return min;
    }

    std::pair<tcp::socket*, U32> GetSocketForAccept()
    {
        U32 threadIndex = SelectThreadWithMinConnections();
        return std::make_pair(_threads[threadIndex].GetSocketForAccept(), threadIndex);
    }

protected:
    SocketMgr() :
        _acceptor(nullptr), _threads(nullptr), _threadCount(0) { }

    virtual NetworkThread<SocketType>* CreateThreads() const = 0;

    AsyncAcceptor* _acceptor;
    NetworkThread<SocketType>* _threads;
    S32 _threadCount;
};
