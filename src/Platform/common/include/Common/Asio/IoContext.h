#pragma once

#include <boost/version.hpp>
#include <boost/asio/io_context.hpp>
#include <boost/asio/post.hpp>

#define IoContextBaseNamespace boost::asio
#define IoContextBase io_context

namespace Avalon::Asio
{
    class IoContext
    {
    public:
        IoContext() : _impl() { }
        explicit IoContext(int concurrency_hint) : _impl(concurrency_hint) { }

        operator IoContextBaseNamespace::IoContextBase&() { return _impl; }
        operator IoContextBaseNamespace::IoContextBase const&() const { return _impl; }

        std::size_t run() { return _impl.run(); }
        void stop() { _impl.stop(); }

        boost::asio::io_context::executor_type get_executor() noexcept { return _impl.get_executor(); }

    private:
        IoContextBaseNamespace::IoContextBase _impl;
    };

    template<typename T>
    inline decltype(auto) post(IoContextBaseNamespace::IoContextBase& ioContext, T&& t)
    {
        return boost::asio::post(ioContext, std::forward<T>(t));
    }

    template<typename T>
    inline decltype(auto) get_io_context(T&& ioObject)
    {
        return ioObject.get_executor().context();
    }
}
