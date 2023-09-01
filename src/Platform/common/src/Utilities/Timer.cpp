#include <Common/Utilities/Timer.h>

#include <Common/Types.h>
#include <Common/Utilities/StringFormat.h>

#include <iomanip>
#include <sstream>

namespace Avalon::TimeDiff // in us
{
    constexpr U64 MILLISECONDS = 1000;
    constexpr U64 SECONDS = 1000 * MILLISECONDS;
    constexpr U64 MINUTES = 60 * SECONDS;
    constexpr U64 HOURS = 60 * MINUTES;
    constexpr U64 DAYS = 24 * HOURS;
}

template<>
U32 Avalon::Time::TimeStringTo<Seconds>(std::string_view timestring)
{
    U32 secs = 0;
    U32 buffer = 0;
    U32 multiplier = 0;

    for (char itr : timestring)
    {
        if (isdigit(itr))
        {
            buffer *= 10;
            buffer += itr - '0';
        }
        else
        {
            switch (itr)
            {
            case 'd':
                multiplier = DAY;
                break;
            case 'h':
                multiplier = HOUR;
                break;
            case 'm':
                multiplier = MINUTE;
                break;
            case 's':
                multiplier = 1;
                break;
            default:
                return 0; // bad format
            }

            buffer *= multiplier;
            secs += buffer;
            buffer = 0;
        }
    }

    return secs;
}

template<>
std::string Avalon::Time::ToTimeString<Microseconds>(U64 durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    U64 microsecs = durationTime % 1000;
    U64 millisecs = (durationTime / TimeDiff::MILLISECONDS) % 1000;
    U64 secs = (durationTime / TimeDiff::SECONDS) % 60;
    U64 minutes = (durationTime / TimeDiff::MINUTES) % 60;
    U64 hours = (durationTime / TimeDiff::HOURS) % 24;
    U64 days = durationTime / TimeDiff::DAYS;

    if (timeFormat == TimeFormat::Numeric)
    {
        if (days)
        {
            return Avalon::StringFormatFmt("{}:{:02}:{:02}:{:02}:{:02}:{:02}", days, hours, minutes, secs, millisecs);
        }
        else if (hours)
        {
            return Avalon::StringFormatFmt("{}:{:02}:{:02}:{:02}:{:02}", hours, minutes, secs, millisecs);
        }
        else if (minutes)
        {
            return Avalon::StringFormatFmt("{}:{:02}:{:02}:{:02}", minutes, secs, millisecs);
        }
        else if (secs)
        {
            return Avalon::StringFormatFmt("{}:{:02}:{:02}", secs, millisecs);
        }
        else if (millisecs)
        {
            return Avalon::StringFormatFmt("{}:{:02}", millisecs);
        }
        else // microsecs
        {
            return Avalon::StringFormatFmt("{}", microsecs);
        }
    }

    std::ostringstream ss;
    std::string stringTime;

    auto GetStringFormat = [&](U32 timeType, std::string_view shortText, std::string_view fullText1, std::string_view fullText)
    {
        ss << timeType;

        switch (timeFormat)
        {
        case TimeFormat::ShortText:
            ss << shortText;
            break;
        case TimeFormat::FullText:
            ss << (timeType == 1 ? fullText1 : fullText);
            break;
        default:
            ss << "<Unknown time format>";
        }
    };

    if (days)
    {
        GetStringFormat(days, "d ", " Day ", " Days ");
    }

    if (timeOutput == TimeOutput::Days)
    {
        stringTime = ss.str();
    }

    if (hours)
    {
        GetStringFormat(hours, "h ", " Hour ", " Hours ");
    }

    if (timeOutput == TimeOutput::Hours)
    {
        stringTime = ss.str();
    }

    if (minutes)
    {
        GetStringFormat(minutes, "m ", " Minute ", " Minutes ");
    }

    if (timeOutput == TimeOutput::Minutes)
    {
        stringTime = ss.str();
    }

    if (secs)
    {
        GetStringFormat(secs, "s ", " Second ", " Seconds ");
    }

    if (timeOutput == TimeOutput::Seconds)
    {
        stringTime = ss.str();
    }

    if (millisecs)
    {
        GetStringFormat(millisecs, "ms ", " Millisecond ", " Milliseconds ");
    }

    if (timeOutput == TimeOutput::Milliseconds)
    {
        stringTime = ss.str();
    }

    if (microsecs)
    {
        GetStringFormat(microsecs, "us ", " Microsecond ", " Microseconds ");
    }

    if (timeOutput == TimeOutput::Microseconds)
    {
        stringTime = ss.str();
    }

    return Avalon::TrimRightInPlace(stringTime);
}

template<>
std::string Avalon::Time::ToTimeString<Milliseconds>(U64 durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    return ToTimeString<Microseconds>(durationTime * TimeDiff::MILLISECONDS, timeOutput, timeFormat);
}

template<>
std::string Avalon::Time::ToTimeString<Seconds>(U64 durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    return ToTimeString<Microseconds>(durationTime * TimeDiff::SECONDS, timeOutput, timeFormat);
}

template<>
std::string Avalon::Time::ToTimeString<Minutes>(U64 durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    return ToTimeString<Microseconds>(durationTime * TimeDiff::MINUTES, timeOutput, timeFormat);
}

template<>
std::string Avalon::Time::ToTimeString<Seconds>(std::string_view durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    return ToTimeString<Seconds>(TimeStringTo<Seconds>(durationTime), timeOutput, timeFormat);
}

std::string Avalon::Time::ToTimeString(Microseconds durationTime, TimeOutput timeOutput /*= TimeOutput::Seconds*/, TimeFormat timeFormat /*= TimeFormat::ShortText*/)
{
    return ToTimeString<Microseconds>(durationTime.count(), timeOutput, timeFormat);
}

#if AV_PLATFORM_WIN
struct tm* localtime_r(time_t const* time, struct tm* result)
{
    localtime_s(result, time);
    return result;
}
#endif

std::tm Avalon::Time::TimeBreakdown(time_t time /*= 0*/)
{
    if (!time)
    {
        time = GetEpochTime().count();
    }

    std::tm timeLocal;
    localtime_r(&time, &timeLocal);
    return timeLocal;
}

time_t Avalon::Time::LocalTimeToUTCTime(time_t time)
{
#if AV_PLATFORM_WIN
    return time + _timezone;
#else
    return time + timezone;
#endif
}

time_t Avalon::Time::GetLocalHourTimestamp(time_t time, U8 hour, bool onlyAfterTime)
{
    tm timeLocal = TimeBreakdown(time);
    timeLocal.tm_hour = 0;
    timeLocal.tm_min = 0;
    timeLocal.tm_sec = 0;

    time_t midnightLocal = mktime(&timeLocal);
    time_t hourLocal = midnightLocal + hour * HOUR;

    if (onlyAfterTime && hourLocal <= time)
    {
        hourLocal += DAY;
    }

    return hourLocal;
}

std::string Avalon::Time::TimeToTimestampStr(Seconds time /*= 0s*/, std::string_view fmt /*= {}*/)
{
    std::stringstream ss;
    std::string format{ fmt };
    time_t t = time.count();

    if (format.empty())
    {
        format = "%Y-%m-%d %X";
    }

    ss << std::put_time(std::localtime(&t), format.c_str());
    return ss.str();
}

std::string Avalon::Time::TimeToHumanReadable(Seconds time /*= 0s*/, std::string_view fmt /*= {}*/)
{
    std::stringstream ss;
    std::string format{ fmt };
    time_t t = time.count();

    if (format.empty())
    {
        format = "%a %b %d %Y %X";
    }

    ss << std::put_time(std::localtime(&t), format.c_str());
    return ss.str();
}

time_t Avalon::Time::GetNextTimeWithDayAndHour(S8 dayOfWeek, S8 hour)
{
    if (hour < 0 || hour > 23)
    {
        hour = 0;
    }

    tm localTm = TimeBreakdown();
    localTm.tm_hour = hour;
    localTm.tm_min = 0;
    localTm.tm_sec = 0;

    if (dayOfWeek < 0 || dayOfWeek > 6)
    {
        dayOfWeek = (localTm.tm_wday + 1) % 7;
    }

    U32 add;

    if (localTm.tm_wday >= dayOfWeek)
    {
        add = (7 - (localTm.tm_wday - dayOfWeek)) * DAY;
    }
    else
    {
        add = (dayOfWeek - localTm.tm_wday) * DAY;
    }

    return mktime(&localTm) + add;
}

time_t Avalon::Time::GetNextTimeWithMonthAndHour(S8 month, S8 hour)
{
    if (hour < 0 || hour > 23)
    {
        hour = 0;
    }

    tm localTm = TimeBreakdown();
    localTm.tm_mday = 1;
    localTm.tm_hour = hour;
    localTm.tm_min = 0;
    localTm.tm_sec = 0;

    if (month < 0 || month > 11)
    {
        month = (localTm.tm_mon + 1) % 12;

        if (!month)
        {
            localTm.tm_year += 1;
        }
    }
    else if (localTm.tm_mon >= month)
    {
        localTm.tm_year += 1;
    }

    localTm.tm_mon = month;
    return mktime(&localTm);
}

U32 Avalon::Time::GetSeconds(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_sec;
}

U32 Avalon::Time::GetMinutes(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_min;
}

U32 Avalon::Time::GetHours(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_hour;
}

U32 Avalon::Time::GetDayInWeek(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_wday;
}

U32 Avalon::Time::GetDayInMonth(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_mday;
}

U32 Avalon::Time::GetDayInYear(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_yday;
}

U32 Avalon::Time::GetMonth(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_mon;
}

U32 Avalon::Time::GetYear(Seconds time /*= 0s*/)
{
    if (time == 0s)
    {
        time = GetEpochTime();
    }

    return TimeBreakdown(time.count()).tm_year;
}
