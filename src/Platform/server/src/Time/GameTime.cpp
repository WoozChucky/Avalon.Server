#include "GameTime.h"
#include <Common/Utilities/Timer.h>

namespace GameTime
{
    using namespace std::chrono;

    Seconds const StartTime = GetEpochTime();

    Seconds GameTime = GetEpochTime();
    Milliseconds GameMSTime = 0ms;

    SystemTimePoint GameTimeSystemPoint = SystemTimePoint::min();
    TimePoint GameTimeSteadyPoint = TimePoint::min();

    Seconds GetStartTime()
    {
        return StartTime;
    }

    Seconds GetGameTime()
    {
        return GameTime;
    }

    Milliseconds GetGameTimeMS()
    {
        return GameMSTime;
    }

    SystemTimePoint GetSystemTime()
    {
        return GameTimeSystemPoint;
    }

    TimePoint Now()
    {
        return GameTimeSteadyPoint;
    }

    Seconds GetUptime()
    {
        return GameTime - StartTime;
    }

    void UpdateGameTimers()
    {
        GameTime = GetEpochTime();
        GameMSTime = GetTimeMS();
        GameTimeSystemPoint = system_clock::now();
        GameTimeSteadyPoint = steady_clock::now();
    }
}
