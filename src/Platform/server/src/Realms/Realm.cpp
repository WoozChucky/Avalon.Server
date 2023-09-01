

#include "Realm.h"
#include <Common/Asio/IpAddress.h>
#include <Common/Asio/IpNetwork.h>

#include <boost/asio/ip/tcp.hpp>

boost::asio::ip::tcp_endpoint Realm::GetAddressForClient(boost::asio::ip::address const& clientAddr) const
{
    boost::asio::ip::address realmIp;

    // Attempt to send best address for client
    if (clientAddr.is_loopback())
    {
        // Try guessing if realm is also connected locally
        if (LocalAddress->is_loopback() || ExternalAddress->is_loopback())
        {
            realmIp = clientAddr;
        }
        else
        {
            // Assume that user connecting from the machine that bnetserver is located on
            // has all realms available in his local network
            realmIp = *LocalAddress;
        }
    }
    else
    {
        if (clientAddr.is_v4() && Avalon::Net::IsInNetwork(LocalAddress->to_v4(), LocalSubnetMask->to_v4(), clientAddr.to_v4()))
        {
            realmIp = *LocalAddress;
        }
        else
        {
            realmIp = *ExternalAddress;
        }
    }

    // Return external IP
    return boost::asio::ip::tcp_endpoint(realmIp, Port);
}
