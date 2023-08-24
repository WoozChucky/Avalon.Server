#pragma once

#include <Common/Types.h>
#include <Logging/LogCommon.h>
#include <Utilities/StringFormat.h>

#include <memory>
#include <unordered_map>
#include <vector>

class Appender;
class Logger;
struct LogMessage;

namespace Avalon::Asio
{
    class IoContext;
    class Strand;
}

#define LOGGER_ROOT "root"

using AppenderCreatorFn = Appender *(*)(U8, const String &, LogLevel, AppenderFlags, const std::vector<StringView> &);

template <class AppenderImpl>
Appender* CreateAppender(U8 id, String const& name, LogLevel level, AppenderFlags flags, std::vector<StringView> const& extraArgs)
{
    return new AppenderImpl(id, name, level, flags, extraArgs);
}

class Log {

    using LoggerMap = std::unordered_map<String, Logger>;

private:
    Log();
    ~Log();
    Log(Log const &) = delete;
    Log(Log &&) = delete;
    Log &operator=(Log const &) = delete;
    Log &operator=(Log &&) = delete;

public:

    static Log *Instance();

    void Initialize(Avalon::Asio::IoContext *ioContext = nullptr);
    void SetSynchronous();  // Not threadsafe - should only be called from main() after all threads are joined
    void LoadFromConfig();
    void Close();
    [[nodiscard]] bool ShouldLog(std::string const &type, LogLevel level) const;
    bool SetLogLevel(std::string const &name, S32 level, bool isLogger = true);

    template<typename... Args>
    inline void outMessage(std::string const &filter, LogLevel const level, std::string_view fmt, Args &&... args) {
        _outMessage(filter, level, Avalon::StringFormatFmt(fmt, std::forward<Args>(args)...));
    }

    template<typename... Args>
    void outCommand(U32 account, std::string_view fmt, Args &&... args) {
        if (!ShouldLog("commands.gm", LOG_LEVEL_INFO)) {
            return;
        }

        _outCommand(Avalon::StringFormatFmt(fmt, std::forward<Args>(args)...), std::to_string(account));
    }

    void SetRealmId(U32 id);

    template<class AppenderImpl>
    void RegisterAppender() {
        RegisterAppender(AppenderImpl::type, &CreateAppender<AppenderImpl>);
    }

    [[nodiscard]] std::string const &GetLogsDir() const { return m_logsDir; }
    [[nodiscard]] std::string const &GetLogsTimestamp() const { return m_logsTimestamp; }

private:
    static std::string GetTimestampStr();
    void write(std::unique_ptr<LogMessage>&& msg) const;

    [[nodiscard]] Logger const* GetLoggerByType(std::string const& type) const;
    Appender* GetAppenderByName(std::string_view name);
    U8 NextAppenderId();
    void CreateAppenderFromConfig(std::string const& name);
    void CreateLoggerFromConfig(std::string const& name);
    void ReadAppendersFromConfig();
    void ReadLoggersFromConfig();
    void RegisterAppender(U8 index, AppenderCreatorFn appenderCreateFn);
    void _outMessage(std::string const& filter, LogLevel level, std::string_view message);
    void _outCommand(std::string_view message, std::string_view param1);

    std::unordered_map<U8, AppenderCreatorFn> appenderFactory;
    std::unordered_map<U8, std::unique_ptr<Appender>> appenders;
    std::unordered_map<std::string, std::unique_ptr<Logger>> loggers;
    U8 AppenderId;
    LogLevel highestLogLevel;

    std::string m_logsDir;
    std::string m_logsTimestamp;

    Avalon::Asio::IoContext* _ioContext;
    Avalon::Asio::Strand* _strand;
    // Deprecated debug filter logs
    DebugLogFilters _debugLogMask;

};

#define sLog Log::Instance()

#define LOG_EXCEPTION_FREE(filterType__, level__, ...) \
    { \
        try \
        { \
            sLog->outMessage(filterType__, level__, fmt::format(__VA_ARGS__)); \
        } \
        catch (std::exception const& e) \
        { \
            sLog->outMessage("server", LogLevel::LOG_LEVEL_ERROR, "Wrong format occurred ({}) at '{}:{}'", \
                e.what(), __FILE__, __LINE__); \
        } \
    }

#ifdef PERFORMANCE_PROFILING
#define LOG_MESSAGE_BODY(filterType__, level__, ...) ((void)0)
#else
#define LOG_MESSAGE_BODY(filterType__, level__, ...)                        \
        do                                                              \
        {                                                               \
            if (sLog->ShouldLog(filterType__, level__))                 \
                LOG_EXCEPTION_FREE(filterType__, level__, __VA_ARGS__); \
        } while (0)
#endif

// Fatal - 1
#define LOG_FATAL(filterType__, ...) \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_FATAL, __VA_ARGS__)

// Error - 2
#define LOG_ERROR(filterType__, ...) \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_ERROR, __VA_ARGS__)

// Warning - 3
#define LOG_WARN(filterType__, ...)  \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_WARN, __VA_ARGS__)

// Info - 4
#define LOG_INFO(filterType__, ...)  \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_INFO, __VA_ARGS__)

// Debug - 5
#define LOG_DEBUG(filterType__, ...) \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_DEBUG, __VA_ARGS__)

// Trace - 6
#define LOG_TRACE(filterType__, ...) \
    LOG_MESSAGE_BODY(filterType__, LogLevel::LOG_LEVEL_TRACE, __VA_ARGS__)

#define LOG_GM(accountId__, ...) \
    sLog->outCommand(accountId__, __VA_ARGS__)
