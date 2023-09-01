#pragma once

#include <Common/Types.h>
#include <Common/Logging/LogCommon.h>

#include <string>
#include <unordered_map>

class Appender;
struct LogMessage;

class Logger
{
public:
    Logger(std::string const& name, LogLevel level);

    void addAppender(U8 type, Appender* appender);
    void delAppender(U8 type);

    std::string const& getName() const;
    LogLevel getLogLevel() const;
    void setLogLevel(LogLevel level);
    void write(LogMessage* message) const;

private:
    std::string name;
    LogLevel level;
    std::unordered_map<U8, Appender*> appenders;
};
