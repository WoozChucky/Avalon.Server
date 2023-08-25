#include "FreezeDetector.h"

#include <Logging/Log.h>
#include <Debugging/Errors.h>

void FreezeDetector::Handler(std::weak_ptr<FreezeDetector> freezeDetectorRef, boost::system::error_code const& error)
{
    if (!error)
    {
        if (std::shared_ptr<FreezeDetector> freezeDetector = freezeDetectorRef.lock())
        {
            U32 curtime = getMSTime();

            //TODO: Uncomment when World::m_worldLoopCounter is implemented
            //U32 worldLoopCounter = World::m_worldLoopCounter;
            U32 worldLoopCounter = 0;
            if (freezeDetector->_worldLoopCounter != worldLoopCounter)
            {
                freezeDetector->_lastChangeMsTime = curtime;
                freezeDetector->_worldLoopCounter = worldLoopCounter;
            }
                // possible freeze
            else
            {
                U32 msTimeDiff = getMSTimeDiff(freezeDetector->_lastChangeMsTime, curtime);
                if (msTimeDiff > freezeDetector->_maxCoreStuckTimeInMs)
                {
                    LOG_ERROR("server.worldserver", "World Thread hangs for {} ms, forcing a crash!", msTimeDiff);
                    ABORT("World Thread hangs for {} ms, forcing a crash!", msTimeDiff);
                }
            }

            freezeDetector->_timer.expires_from_now(boost::posix_time::seconds(1));
            freezeDetector->_timer.async_wait(std::bind(&FreezeDetector::Handler, freezeDetectorRef, std::placeholders::_1));
        }
    }
}
