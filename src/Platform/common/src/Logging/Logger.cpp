#include <Common/Logging/Logger.h>

#include <Common/Logging/Appender.h>
#include <Common/Logging/LogMessage.h>

Logger::Logger(std::string const& _name, LogLevel _level): name(_name), level(_level) { }

std::string const& Logger::getName() const
{
    return name;
}

LogLevel Logger::getLogLevel() const
{
    return level;
}

void Logger::addAppender(U8 id, Appender* appender)
{
    appenders[id] = appender;
}

void Logger::delAppender(U8 id)
{
    appenders.erase(id);
}

void Logger::setLogLevel(LogLevel _level)
{
    level = _level;
}

void Logger::write(LogMessage* message) const
{
    if (!level || level < message->level || message->text.empty())
    {
        fprintf(stderr, "Logger::write: Logger %s, Level %u. Msg %s Level %u WRONG LEVEL MASK OR EMPTY MSG\n", getName().c_str(), getLogLevel(), message->text.c_str(), message->level);
        return;
    }

    for (std::pair<U8 const, Appender*> const& appender : appenders)
        if (appender.second)
        {
            appender.second->write(message);
        }
}
