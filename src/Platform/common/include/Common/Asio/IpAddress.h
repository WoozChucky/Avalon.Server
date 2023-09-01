#pragma once

#include <Common/Types.h>
#include <boost/asio/ip/address.hpp>

namespace Avalon::Net
{
    using boost::asio::ip::make_address;
    using boost::asio::ip::make_address_v4;
    inline U32 address_to_uint(boost::asio::ip::address_v4 const& address) { return address.to_uint(); }
}
