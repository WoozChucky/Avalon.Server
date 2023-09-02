#pragma once

#include <boost/version.hpp>

/**
  Collection of forward declarations to improve compile time
 */
/*
 namespace boost::posix_time
{
    class ptime;
}

namespace boost::asio
{
    template <typename Time>
    struct time_traits;
}

namespace boost::asio::ip
{
    class address;
    class tcp;

    template <typename InternetProtocol>
    class basic_endpoint;

    typedef basic_endpoint<tcp> tcp_endpoint;
}
*/

namespace Avalon::Asio
{
    class DeadlineTimer;
    class IoContext;
    class Resolver;
    class Strand;
}
