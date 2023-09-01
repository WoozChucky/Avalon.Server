#pragma once

#include <Common/Types.h>
#include <Common/Utilities/Duration.h>

enum class TimeFormat : U8
{
    FullText,  // 1 Days 2 Hours 3 Minutes 4 Seconds 5 Milliseconds
    ShortText, // 1d 2h 3m 4s 5ms
    Numeric    // 1:2:3:4:5
};

enum class TimeOutput : U8
{
    Days,         // 1d
    Hours,        // 1d 2h
    Minutes,      // 1d 2h 3m
    Seconds,      // 1d 2h 3m 4s
    Milliseconds, // 1d 2h 3m 4s 5ms
    Microseconds  // 1d 2h 3m 4s 5ms 6us
};

namespace Avalon::Time
{
    template <class T>
    U32 TimeStringTo(std::string_view timeString);

    template<class T>
    std::string ToTimeString(U64 durationTime, TimeOutput timeOutput = TimeOutput::Seconds, TimeFormat timeFormat = TimeFormat::ShortText);

    template<class T>
    std::string ToTimeString(std::string_view durationTime, TimeOutput timeOutput = TimeOutput::Seconds, TimeFormat timeFormat = TimeFormat::ShortText);

    std::string ToTimeString(Microseconds durationTime, TimeOutput timeOutput = TimeOutput::Seconds, TimeFormat timeFormat = TimeFormat::ShortText);

    time_t LocalTimeToUTCTime(time_t time);
    time_t GetLocalHourTimestamp(time_t time, U8 hour, bool onlyAfterTime = true);
    std::tm TimeBreakdown(time_t t = 0);
    std::string TimeToTimestampStr(Seconds time = 0s, std::string_view fmt = {});
    std::string TimeToHumanReadable(Seconds time = 0s, std::string_view fmt = {});

    time_t GetNextTimeWithDayAndHour(S8 dayOfWeek, S8 hour); // int8 dayOfWeek: 0 (sunday) to 6 (saturday)
    time_t GetNextTimeWithMonthAndHour(S8 month, S8 hour); // int8 month: 0 (january) to 11 (december)

    U32 GetSeconds(Seconds time = 0s);      // seconds after the minute - [0, 60]
    U32 GetMinutes(Seconds time = 0s);      // minutes after the hour - [0, 59]
    U32 GetHours(Seconds time = 0s);        // hours since midnight - [0, 23]
    U32 GetDayInWeek(Seconds time = 0s);    // days since Sunday - [0, 6]
    U32 GetDayInMonth(Seconds time = 0s);   // day of the month - [1, 31]
    U32 GetDayInYear(Seconds time = 0s);    // days since January 1 - [0, 365]
    U32 GetMonth(Seconds time = 0s);        // months since January - [0, 11]
    U32 GetYear(Seconds time = 0s);         // years since 1900
}

struct tm* localtime_r(time_t const* time, struct tm* result);

inline TimePoint GetApplicationStartTime()
{
    using namespace std::chrono;

    static const TimePoint ApplicationStartTime = steady_clock::now();

    return ApplicationStartTime;
}

inline Milliseconds GetTimeMS()
{
    using namespace std::chrono;

    return duration_cast<milliseconds>(steady_clock::now() - GetApplicationStartTime());
}

inline Milliseconds GetMSTimeDiff(Milliseconds oldMSTime, Milliseconds newMSTime)
{
    if (oldMSTime > newMSTime)
    {
        return oldMSTime - newMSTime;
    }
    else
    {
        return newMSTime - oldMSTime;
    }
}

inline U32 getMSTime()
{
    using namespace std::chrono;

    return U32(duration_cast<milliseconds>(steady_clock::now() - GetApplicationStartTime()).count());
}

inline U32 getMSTimeDiff(U32 oldMSTime, U32 newMSTime)
{
    // getMSTime() have limited data range and this is case when it overflow in this tick
    if (oldMSTime > newMSTime)
    {
        return (0xFFFFFFFF - oldMSTime) + newMSTime;
    }
    else
    {
        return newMSTime - oldMSTime;
    }
}

inline U32 getMSTimeDiff(U32 oldMSTime, TimePoint newTime)
{
    using namespace std::chrono;

    U32 newMSTime = U32(duration_cast<milliseconds>(newTime - GetApplicationStartTime()).count());
    return getMSTimeDiff(oldMSTime, newMSTime);
}

inline U32 GetMSTimeDiffToNow(U32 oldMSTime)
{
    return getMSTimeDiff(oldMSTime, getMSTime());
}

inline Milliseconds GetMSTimeDiffToNow(Milliseconds oldMSTime)
{
    return GetMSTimeDiff(oldMSTime, GetTimeMS());
}

inline Seconds GetEpochTime()
{
    using namespace std::chrono;
    return duration_cast<Seconds>(system_clock::now().time_since_epoch());
}

struct IntervalTimer
{
public:
    IntervalTimer() = default;

    void Update(time_t diff)
    {
        _current += diff;
        if (_current < 0)
        {
            _current = 0;
        }
    }

    bool Passed()
    {
        return _current >= _interval;
    }

    void Reset()
    {
        if (_current >= _interval)
        {
            _current %= _interval;
        }
    }

    void SetCurrent(time_t current)
    {
        _current = current;
    }

    void SetInterval(time_t interval)
    {
        _interval = interval;
    }

    [[nodiscard]] time_t GetInterval() const
    {
        return _interval;
    }

    [[nodiscard]] time_t GetCurrent() const
    {
        return _current;
    }

private:
    time_t _interval{0};
    time_t _current{0};
};

struct TimeTracker
{
public:
    TimeTracker(time_t expiry)
        : i_expiryTime(expiry)
    {
    }

    void Update(time_t diff)
    {
        i_expiryTime -= diff;
    }

    [[nodiscard]] bool Passed() const
    {
        return i_expiryTime <= 0;
    }

    void Reset(time_t interval)
    {
        i_expiryTime = interval;
    }

    [[nodiscard]] time_t GetExpiry() const
    {
        return i_expiryTime;
    }

private:
    time_t i_expiryTime;
};

struct TimeTrackerSmall
{
public:
    TimeTrackerSmall(S32 expiry = 0)
        : i_expiryTime(expiry)
    {
    }

    void Update(S32 diff)
    {
        i_expiryTime -= diff;
    }

    [[nodiscard]] bool Passed() const
    {
        return i_expiryTime <= 0;
    }

    void Reset(S32 interval)
    {
        i_expiryTime = interval;
    }

    [[nodiscard]] S32 GetExpiry() const
    {
        return i_expiryTime;
    }

private:
    S32 i_expiryTime;
};

struct PeriodicTimer
{
public:
    PeriodicTimer(S32 period, S32 start_time)
        : i_period(period), i_expireTime(start_time)
    {
    }

    bool Update(const U32 diff)
    {
        if ((i_expireTime -= diff) > 0)
        {
            return false;
        }

        i_expireTime += i_period > S32(diff) ? i_period : diff;
        return true;
    }

    void SetPeriodic(S32 period, S32 start_time)
    {
        i_expireTime = start_time;
        i_period = period;
    }

    // Tracker interface
    void TUpdate(S32 diff) { i_expireTime -= diff; }
    [[nodiscard]] bool TPassed() const { return i_expireTime <= 0; }
    void TReset(S32 diff, S32 period)  { i_expireTime += period > diff ? period : diff; }

private:
    S32 i_period;
    S32 i_expireTime;
};
