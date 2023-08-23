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
              boost::asio::ip::tcp::resolver::iterator endpointIterator) :
            socket(ioService, sslContext) {
        socket.set_verify_mode(boost::asio::ssl::verify_peer);
        socket.set_verify_callback(
                [this](auto && PH1, auto && PH2) { return verify_certificate(std::forward<decltype(PH1)>(PH1), std::forward<decltype(PH2)>(PH2)); });

        boost::asio::async_connect(socket.lowest_layer(), endpointIterator,
                                   boost::bind(&SSLClient::handle_connect, this,
                                               boost::asio::placeholders::error));
    }
    ~SSLClient();

    bool Connect();
    void Disconnect();

    bool IsConnected() const;

    bool Send(const char* data, size_t size);
    bool Receive(char* data, size_t size);

private:

    enum { max_length = 1024 };

    boost::asio::ssl::stream<boost::asio::ip::tcp::socket> socket;
    char request_[max_length];
    char reply_[max_length];

    bool verify_certificate(bool preverified,
                            boost::asio::ssl::verify_context& ctx)
    {
        // The verify callback can be used to check whether the certificate that is
        // being presented is valid for the peer. For example, RFC 2818 describes
        // the steps involved in doing this for HTTPS. Consult the OpenSSL
        // documentation for more details. Note that the callback is called once
        // for each certificate in the certificate chain, starting from the root
        // certificate authority.

        // In this example we will simply print the certificate's subject name.
        char subject_name[256];
        X509* cert = X509_STORE_CTX_get_current_cert(ctx.native_handle());
        X509_NAME_oneline(X509_get_subject_name(cert), subject_name, 256);
        std::cout << "Verifying " << subject_name << "\n";

        return preverified;
    }

    void handle_connect(const boost::system::error_code& error)
    {
        if (!error)
        {
            socket.async_handshake(boost::asio::ssl::stream_base::client,
                                    boost::bind(&SSLClient::handle_handshake, this,
                                                boost::asio::placeholders::error));
        }
        else
        {
            std::cout << "Connect failed: " << error.message() << "\n";
        }
    }

    void handle_handshake(const boost::system::error_code& error)
    {
        if (!error)
        {
            std::cout << "Enter message: ";
            std::cin.getline(request_, max_length);
            size_t request_length = strlen(request_);

            boost::asio::async_write(socket,
                                     boost::asio::buffer(request_, request_length),
                                     boost::bind(&SSLClient::handle_write, this,
                                                 boost::asio::placeholders::error,
                                                 boost::asio::placeholders::bytes_transferred));
        }
        else
        {
            std::cout << "Handshake failed: " << error.message() << "\n";
        }
    }

    void handle_write(const boost::system::error_code& error,
                      size_t bytes_transferred)
    {
        if (!error)
        {
            boost::asio::async_read(socket,
                                    boost::asio::buffer(reply_, bytes_transferred),
                                    boost::bind(&SSLClient::handle_read, this,
                                                boost::asio::placeholders::error,
                                                boost::asio::placeholders::bytes_transferred));
        }
        else
        {
            std::cout << "Write failed: " << error.message() << "\n";
        }
    }

    void handle_read(const boost::system::error_code& error,
                     size_t bytes_transferred)
    {
        if (!error)
        {
            std::cout << "Reply: ";
            std::cout.write(reply_, bytes_transferred);
            std::cout << "\n";
        }
        else
        {
            std::cout << "Read failed: " << error.message() << "\n";
        }
    }
};
