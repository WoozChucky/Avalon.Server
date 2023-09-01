#include <Common/Logging/LogMessage.h>

#include <Common/Utilities/StringFormat.h>
#include <Common/Utilities/Timer.h>

LogMessage::LogMessage(LogLevel _level, std::string const& _type, std::string_view _text)
    : level(_level), type(_type), text(std::string(_text)), mtime(GetEpochTime()) { }

LogMessage::LogMessage(LogLevel _level, std::string const& _type, std::string_view _text, std::string_view _param1)
    : level(_level), type(_type), text(std::string(_text)), param1(std::string(_param1)), mtime(GetEpochTime()) { }

std::string LogMessage::getTimeStr(Seconds time)
{
    return Avalon::Time::TimeToTimestampStr(time, "%Y-%m-%d %X");
}

std::string LogMessage::getTimeStr() const
{
    return getTimeStr(mtime);
}
