#pragma once

#include "IoContext.h"
#include <boost/asio/strand.hpp>

#if BOOST_VERSION >= 106600
#include <boost/asio/bind_executor.hpp>
#endif

namespace Avalon::Asio
{
    /**
        Hack to make it possible to forward declare strand (which is a inner class)
    */
    class Strand : public IoContextBaseNamespace::IoContextBase::strand
    {
    public:
        Strand(IoContext& ioContext) : IoContextBaseNamespace::IoContextBase::strand(ioContext) { }
    };

    using boost::asio::bind_executor;
}
