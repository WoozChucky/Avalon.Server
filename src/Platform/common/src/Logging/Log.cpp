#include <Common/Logging/Log.h>

#include <Common/Configuration/ConfigManager.h>
#include <Common/Utilities/StringConvert.h>
#include <Common/Utilities/Tokenize.h>
#include <Common/Utilities/Timer.h>
#include <Common/Logging/LogMessage.h>
#include <Common/Logging/LogOperation.h>
#include <Common/Logging/Logger.h>
#include <Common/Logging/Appender.h>
#include <Common/Logging/AppenderConsole.h>
#include <Common/Logging/AppenderFile.h>
#include <Common/Asio/Strand.h>

#include <memory>

Log::Log() : AppenderId(0), highestLogLevel(LOG_LEVEL_FATAL)
{
    m_logsTimestamp = "_" + GetTimestampStr();
    RegisterAppender<AppenderConsole>();
    RegisterAppender<AppenderFile>();
}

Log::~Log()
{
    delete _strand;
    Close();
}

U8 Log::NextAppenderId()
{
    return AppenderId++;
}

Appender* Log::GetAppenderByName(std::string_view name)
{
    auto it = appenders.begin();
    while (it != appenders.end() && it->second && it->second->getName() != name)
    {
        ++it;
    }

    return it == appenders.end() ? nullptr : it->second.get();
}

void Log::CreateAppenderFromConfig(std::string const& appenderName)
{
    if (appenderName.empty())
    {
        return;
    }

    // Format = type, level, flags, optional1, optional2
    // if type = File. optional1 = file and option2 = mode
    // if type = Console. optional1 = Color
    std::string options = sConfigMgr->GetOption<std::string>(appenderName, "");

    std::vector<std::string_view> tokens = Avalon::Tokenize(options, ',', true);

    size_t const size = tokens.size();
    std::string name = appenderName.substr(9);

    if (size < 2)
    {
        fmt::print(stderr, "Log::CreateAppenderFromConfig: Wrong configuration for appender {}. Config line: {}\n", name, options);
        return;
    }

    AppenderFlags flags = APPENDER_FLAGS_NONE;
    AppenderType type = AppenderType(Avalon::StringTo<U8>(tokens[0]).value_or(APPENDER_INVALID));
    LogLevel level = LogLevel(Avalon::StringTo<U8>(tokens[1]).value_or(LOG_LEVEL_INVALID));

    auto factoryFunction = appenderFactory.find(type);
    if (factoryFunction == appenderFactory.end())
    {
        fmt::print(stderr, "Log::CreateAppenderFromConfig: Unknown type '{}' for appender {}\n", tokens[0], name);
        return;
    }

    if (level > NUM_ENABLED_LOG_LEVELS)
    {
        fmt::print(stderr, "Log::CreateAppenderFromConfig: Wrong Log Level '{}' for appender {}\n", tokens[1], name);
        return;
    }

    if (size > 2)
    {
        if (Optional<U8> flagsVal = Avalon::StringTo<U8>(tokens[2]))
        {
            flags = AppenderFlags(*flagsVal);
        }
        else
        {
            fmt::print(stderr, "Log::CreateAppenderFromConfig: Unknown flags '{}' for appender {}\n", tokens[2], name);
            return;
        }
    }

    try
    {
        Appender* appender = factoryFunction->second(NextAppenderId(), name, level, flags, tokens);
        appenders[appender->getId()].reset(appender);
    }
    catch (InvalidAppenderArgsException const& iaae)
    {
        fmt::print(stderr, "{}\n", iaae.what());
    }
}

void Log::CreateLoggerFromConfig(std::string const& appenderName)
{
    if (appenderName.empty())
    {
        return;
    }

    LogLevel level = LOG_LEVEL_DISABLED;

    std::string options = sConfigMgr->GetOption<std::string>(appenderName, "");
    std::string name = appenderName.substr(7);

    if (options.empty())
    {
        fmt::print(stderr, "Log::CreateLoggerFromConfig: Missing config option Logger.{}\n", name);
        return;
    }

    std::vector<std::string_view> tokens = Avalon::Tokenize(options, ',', true);

    if (tokens.size() != 2)
    {
        fmt::print(stderr, "Log::CreateLoggerFromConfig: Wrong config option Logger.%s=%s\n", name, options);
        return;
    }

    std::unique_ptr<Logger>& logger = loggers[name];
    if (logger)
    {
        fmt::print(stderr, "Error while configuring Logger {}. Already defined\n", name);
        return;
    }

    level = LogLevel(Avalon::StringTo<U8>(tokens[0]).value_or(LOG_LEVEL_INVALID));
    if (level > NUM_ENABLED_LOG_LEVELS)
    {
        fmt::print(stderr, "Log::CreateLoggerFromConfig: Wrong Log Level '{}' for logger {}\n", tokens[0], name);
        return;
    }

    if (level > highestLogLevel)
    {
        highestLogLevel = level;
    }

    logger = std::make_unique<Logger>(name, level);

    for (std::string_view appendName : Avalon::Tokenize(tokens[1], ' ', false))
    {
        if (Appender* appender = GetAppenderByName(appendName))
        {
            logger->addAppender(appender->getId(), appender);
        }
        else
        {
            fmt::print(stderr, "Error while configuring Appender {} in Logger {}. Appender does not exist\n", appendName, name);
        }
    }
}

void Log::ReadAppendersFromConfig()
{
    std::vector<std::string> keys = sConfigMgr->GetKeysByString("Appender.");
    for (std::string const& appenderName : keys)
    {
        CreateAppenderFromConfig(appenderName);
    }
}

void Log::ReadLoggersFromConfig()
{
    std::vector<std::string> keys = sConfigMgr->GetKeysByString("Logger.");
    for (std::string const& loggerName : keys)
    {
        CreateLoggerFromConfig(loggerName);
    }

    // Bad config configuration, creating default config
    if (loggers.find(LOGGER_ROOT) == loggers.end())
    {
        fmt::print(stderr, "Wrong Loggers configuration. Review your Logger config section.\n"
                           "Creating default loggers [root (Error), server (Info)] to console\n");

        Close(); // Clean any Logger or Appender created

        AppenderConsole* appender = new AppenderConsole(NextAppenderId(), "Console", LOG_LEVEL_DEBUG, APPENDER_FLAGS_NONE, {});
        appenders[appender->getId()].reset(appender);

        Logger* rootLogger = new Logger(LOGGER_ROOT, LOG_LEVEL_ERROR);
        rootLogger->addAppender(appender->getId(), appender);
        loggers[LOGGER_ROOT].reset(rootLogger);

        Logger* serverLogger = new Logger("server", LOG_LEVEL_INFO);
        serverLogger->addAppender(appender->getId(), appender);
        loggers["server"].reset(serverLogger);
    }
}

void Log::RegisterAppender(U8 index, AppenderCreatorFn appenderCreateFn)
{
    auto itr = appenderFactory.find(index);
    ASSERT(itr == appenderFactory.end());
    appenderFactory[index] = appenderCreateFn;
}

void Log::_outMessage(std::string const& filter, LogLevel level, std::string_view message)
{
    write(std::make_unique<LogMessage>(level, filter, message));
}

void Log::_outCommand(std::string_view message, std::string_view param1)
{
    write(std::make_unique<LogMessage>(LOG_LEVEL_INFO, "commands.gm", message, param1));
}

void Log::write(std::unique_ptr<LogMessage>&& msg) const
{
    Logger const* logger = GetLoggerByType(msg->type);

    if (_ioContext)
    {
        std::shared_ptr<LogOperation> logOperation = std::make_shared<LogOperation>(logger, std::move(msg));
        Avalon::Asio::post(*_ioContext, Avalon::Asio::bind_executor(*_strand, [logOperation]() { logOperation->call(); }));
    }
    else
        logger->write(msg.get());
}

Logger const* Log::GetLoggerByType(std::string const& type) const
{
    auto it = loggers.find(type);
    if (it != loggers.end())
    {
        return it->second.get();
    }

    if (type == LOGGER_ROOT)
    {
        return nullptr;
    }

    std::string parentLogger = LOGGER_ROOT;
    size_t found = type.find_last_of('.');
    if (found != std::string::npos)
    {
        parentLogger = type.substr(0, found);
    }

    return GetLoggerByType(parentLogger);
}

std::string Log::GetTimestampStr()
{
    return Avalon::Time::TimeToTimestampStr(GetEpochTime(), "%Y-%m-%d_%H_%M_%S");
}

bool Log::SetLogLevel(std::string const& name, S32 newLeveli, bool isLogger /* = true */)
{
    if (newLeveli < 0)
    {
        return false;
    }

    LogLevel newLevel = LogLevel(newLeveli);

    if (isLogger)
    {
        auto it = loggers.begin();
        while (it != loggers.end() && it->second->getName() != name)
        {
            ++it;
        }

        if (it == loggers.end())
        {
            return false;
        }

        it->second->setLogLevel(newLevel);

        if (newLevel != LOG_LEVEL_DISABLED && newLevel > highestLogLevel)
        {
            highestLogLevel = newLevel;
        }
    }
    else
    {
        Appender* appender = GetAppenderByName(name);
        if (!appender)
        {
            return false;
        }

        appender->setLogLevel(newLevel);
    }

    return true;
}

void Log::SetRealmId(U32 id)
{
    for (std::pair<U8 const, std::unique_ptr<Appender>>& appender : appenders)
    {
        appender.second->setRealmId(id);
    }
}

void Log::Close()
{
    loggers.clear();
    appenders.clear();
}

bool Log::ShouldLog(std::string const& type, LogLevel level) const
{
    /// @todo: Use cache to store "Type.sub1.sub2": "Type" equivalence, should
    // Speed up in cases where requesting "Type.sub1.sub2" but only configured
    // Logger "Type"

    // Don't even look for a logger if the LogLevel is higher than the highest log levels across all loggers
    if (level > highestLogLevel)
    {
        return false;
    }

    Logger const* logger = GetLoggerByType(type);
    if (!logger)
    {
        return false;
    }

    LogLevel logLevel = logger->getLogLevel();
    return logLevel != LOG_LEVEL_DISABLED && logLevel >= level;
}

Log* Log::Instance()
{
    static Log instance;
    return &instance;
}

void Log::Initialize(Avalon::Asio::IoContext* ioContext)
{
    if (ioContext)
    {
        _ioContext = ioContext;
        _strand = new Avalon::Asio::Strand(*ioContext);
    }

    LoadFromConfig();
}

void Log::SetSynchronous()
{
    delete _strand;
    _strand = nullptr;
    _ioContext = nullptr;
}

void Log::LoadFromConfig()
{
    Close();

    highestLogLevel = LOG_LEVEL_FATAL;
    AppenderId = 0;
    m_logsDir = sConfigMgr->GetOption<std::string>("LogsDir", "", false);

    if (!m_logsDir.empty())
        if ((m_logsDir.at(m_logsDir.length() - 1) != '/') && (m_logsDir.at(m_logsDir.length() - 1) != '\\'))
        {
            m_logsDir.push_back('/');
        }

    ReadAppendersFromConfig();
    ReadLoggersFromConfig();

    _debugLogMask = DebugLogFilters(sConfigMgr->GetOption<U32>("DebugLogMask", LOG_FILTER_NONE, false));
}
