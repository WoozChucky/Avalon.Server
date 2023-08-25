#pragma once

#include <Utilities/Duration.h>

namespace GameTime
{
    // Server start time
    Seconds GetStartTime();

    // Current server time (unix)
    Seconds GetGameTime();

    // Milliseconds since server start
    Milliseconds GetGameTimeMS();

    /// Current chrono system_clock time point
    SystemTimePoint GetSystemTime();

    /// Current chrono steady_clock time point
    TimePoint Now();

    /// Uptime
    Seconds GetUptime();

    /// Update all timers
    void UpdateGameTimers();
}
