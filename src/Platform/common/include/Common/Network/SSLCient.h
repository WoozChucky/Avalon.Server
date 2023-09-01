#pragma once

#include <boost/bind.hpp>
#include <boost/asio.hpp>
#include <boost/asio/ssl.hpp>

#include <string>

class SSLClient {
public:
    SSLClient() = delete;
    SSLClient(const SSLClient&) = delete;
    SSLClient(SSLClient&&) = delete;
    SSLClient& operator=(const SSLClient&) = delete;
    SSLClient& operator=(SSLClient&&) = delete;

    SSLClient(boost::asio::io_service& ioService,
              boost::asio::ssl::context& sslContext,
              boost::asio::ip::tcp::resolver::iterator endpointIterator);
    ~SSLClient();

    bool Connect();
    void Disconnect();

    bool IsConnected() const;

    bool Send(const char* data, size_t size);
    bool Receive(char* data, size_t size);

private:

    enum { max_length = 1024 };

    boost::asio::ssl::stream<boost::asio::ip::tcp::socket> socket;
    char request[max_length];
    char reply[max_length];

    bool VerifyCertificate(bool preverified, boost::asio::ssl::verify_context& ctx);
    void HandleConnect(const boost::system::error_code& error);
    void HandleHandshake(const boost::system::error_code& error);
    void HandleWrite(const boost::system::error_code& error, size_t bytes_transferred);
    void HandleRead(const boost::system::error_code& error, size_t bytes_transferred);

};
