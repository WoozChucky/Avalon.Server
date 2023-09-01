#include <Common/Network/SSLCient.h>

#include <iostream>
#include <utility>

SSLClient::SSLClient(boost::asio::io_service& ioService,
                     boost::asio::ssl::context& sslContext,
                     boost::asio::ip::tcp::resolver::iterator endpointIterator) : socket(ioService, sslContext) {

    this->socket.set_verify_mode(boost::asio::ssl::verify_peer);

    this->socket.set_verify_callback(
            boost::bind(&SSLClient::VerifyCertificate, this, _1, _2));

    boost::asio::async_connect(
            socket.lowest_layer(),
            std::move(endpointIterator), // TODO: Check if std::move is correctly used here
            boost::bind(&SSLClient::HandleConnect, this, boost::asio::placeholders::error)
    );
}

bool SSLClient::Connect() {
    return false;
}

bool  SSLClient::VerifyCertificate(bool preverified,
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

void SSLClient::HandleConnect(const boost::system::error_code& error) {
    if (!error)
    {
        socket.async_handshake(boost::asio::ssl::stream_base::client,
                               boost::bind(&SSLClient::HandleHandshake, this,
                                           boost::asio::placeholders::error));
    }
    else
    {
        std::cout << "Connect failed: " << error.message() << "\n";
    }
}

void SSLClient::HandleHandshake(const boost::system::error_code& error)
{
    if (!error)
    {
        std::cout << "Enter message: ";
        std::cin.getline(request, max_length);
        size_t request_length = strlen(request);

        boost::asio::async_write(socket,
                                 boost::asio::buffer(request, request_length),
                                 boost::bind(&SSLClient::HandleWrite, this,
                                             boost::asio::placeholders::error,
                                             boost::asio::placeholders::bytes_transferred));
    }
    else
    {
        std::cout << "Handshake failed: " << error.message() << "\n";
    }
}

void SSLClient::HandleWrite(const boost::system::error_code& error, size_t bytes_transferred)
{
    if (!error)
    {
        boost::asio::async_read(socket,
                                boost::asio::buffer(reply, bytes_transferred),
                                boost::bind(&SSLClient::HandleRead, this,
                                            boost::asio::placeholders::error,
                                            boost::asio::placeholders::bytes_transferred));
    }
    else
    {
        std::cout << "Write failed: " << error.message() << "\n";
    }
}

void SSLClient::HandleRead(const boost::system::error_code& error, size_t bytes_transferred)
{
    if (!error)
    {
        std::cout << "Reply: ";
        std::cout.write(reply, bytes_transferred);
        std::cout << "\n";
    }
    else
    {
        std::cout << "Read failed: " << error.message() << "\n";
    }
}
