#pragma once

#include <Common/Types.h>
#include <Asio/IoContext.h>
#include <Asio/DeadlineTimer.h>
#include <Utilities/Timer.h>

#include <memory>


class FreezeDetector
{
public:
    FreezeDetector(Avalon::Asio::IoContext& ioContext, U32 maxCoreStuckTime)
            : _timer(ioContext), _worldLoopCounter(0), _lastChangeMsTime(getMSTime()), _maxCoreStuckTimeInMs(maxCoreStuckTime) { }

    static void Start(std::shared_ptr<FreezeDetector> const& freezeDetector)
    {
        freezeDetector->_timer.expires_from_now(boost::posix_time::seconds(5));
        freezeDetector->_timer.async_wait(std::bind(&FreezeDetector::Handler, std::weak_ptr<FreezeDetector>(freezeDetector), std::placeholders::_1));
    }

    static void Handler(std::weak_ptr<FreezeDetector> freezeDetectorRef, boost::system::error_code const& error);

private:
    Avalon::Asio::DeadlineTimer _timer;
    U32 _worldLoopCounter;
    U32 _lastChangeMsTime;
    U32 _maxCoreStuckTimeInMs;
};
