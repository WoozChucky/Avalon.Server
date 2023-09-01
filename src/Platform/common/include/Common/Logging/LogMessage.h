#pragma once

#include <Common/Types.h>
#include <Common/Utilities/Duration.h>
#include <Common/Logging/LogCommon.h>

struct LogMessage
{
    LogMessage(LogLevel _level, std::string const& _type, std::string_view _text);
    LogMessage(LogLevel _level, std::string const& _type, std::string_view _text, std::string_view _param1);

    LogMessage(LogMessage const& /*other*/) = delete;
    LogMessage& operator=(LogMessage const& /*other*/) = delete;

    static std::string getTimeStr(Seconds time);
    std::string getTimeStr() const;

    LogLevel const level;
    std::string const type;
    std::string const text;
    std::string prefix;
    std::string param1;
    Seconds mtime;

    ///@ Returns size of the log message content in bytes
    U32 Size() const
    {
        return static_cast<U32>(prefix.size() + text.size());
    }
};
