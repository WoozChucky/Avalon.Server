#pragma once

#include <Asio/IoContext.h>
#include <Asio/IpAddress.h>
#include <Logging/Log.h>

#include <atomic>
#include <boost/asio/ip/tcp.hpp>
#include <functional>

using boost::asio::ip::tcp;

#define AVALON_MAX_LISTEN_CONNECTIONS boost::asio::socket_base::max_listen_connections

class AsyncAcceptor
{
public:
    using AcceptCallback = void (*)(tcp::socket &&, U32);

    AsyncAcceptor(Avalon::Asio::IoContext& ioContext, std::string const& bindIp, U16 port) :
        _acceptor(ioContext), _endpoint(Avalon::Net::make_address(bindIp), port),
        _socket(ioContext), _closed(false), _socketFactory(std::bind(&AsyncAcceptor::DefeaultSocketFactory, this))
    {
    }

    template<class T>
    void AsyncAccept();

    template<AcceptCallback acceptCallback>
    void AsyncAcceptWithCallback()
    {
        tcp::socket* socket;
        U32 threadIndex;
        std::tie(socket, threadIndex) = _socketFactory();
        _acceptor.async_accept(*socket, [this, socket, threadIndex](boost::system::error_code error)
        {
            if (!error)
            {
                try
                {
                    socket->non_blocking(true);

                    acceptCallback(std::move(*socket), threadIndex);
                }
                catch (boost::system::system_error const& err)
                {
                    LOG_INFO("network", "Failed to initialize client's socket {}", err.what());
                }
            }

            if (!_closed)
                this->AsyncAcceptWithCallback<acceptCallback>();
        });
    }

    bool Bind()
    {
        boost::system::error_code errorCode;
        _acceptor.open(_endpoint.protocol(), errorCode);
        if (errorCode)
        {
            LOG_INFO("network", "Failed to open acceptor {}", errorCode.message());
            return false;
        }

#if AV_PLATFORM_WIN
        _acceptor.set_option(boost::asio::ip::tcp::acceptor::reuse_address(true), errorCode);
        if (errorCode)
        {
            LOG_INFO("network", "Failed to set reuse_address option on acceptor {}", errorCode.message());
            return false;
        }
#endif

        _acceptor.bind(_endpoint, errorCode);
        if (errorCode)
        {
            LOG_INFO("network", "Could not bind to {}:{} {}", _endpoint.address().to_string(), _endpoint.port(), errorCode.message());
            return false;
        }

        _acceptor.listen(AVALON_MAX_LISTEN_CONNECTIONS, errorCode);
        if (errorCode)
        {
            LOG_INFO("network", "Failed to start listening on {}:{} {}", _endpoint.address().to_string(), _endpoint.port(), errorCode.message());
            return false;
        }

        return true;
    }

    void Close()
    {
        if (_closed.exchange(true))
            return;

        boost::system::error_code err;
        _acceptor.close(err);
    }

    void SetSocketFactory(std::function<std::pair<tcp::socket*, U32>()> func) { _socketFactory = func; }

private:
    std::pair<tcp::socket*, U32> DefeaultSocketFactory() { return std::make_pair(&_socket, 0); }

    tcp::acceptor _acceptor;
    tcp::endpoint _endpoint;
    tcp::socket _socket;
    std::atomic<bool> _closed;
    std::function<std::pair<tcp::socket*, U32>()> _socketFactory;
};

template<class T>
void AsyncAcceptor::AsyncAccept()
{
    _acceptor.async_accept(_socket, [this](boost::system::error_code error)
    {
        if (!error)
        {
            try
            {
                // this-> is required here to fix an segmentation fault in gcc 4.7.2 - reason is lambdas in a templated class
                std::make_shared<T>(std::move(this->_socket))->Start();
            }
            catch (boost::system::system_error const& err)
            {
                LOG_INFO("network", "Failed to retrieve client's remote address {}", err.what());
            }
        }

        // lets slap some more this-> on this so we can fix this bug with gcc 4.7.2 throwing internals in yo face
        if (!_closed)
            this->AsyncAccept<T>();
    });
}
