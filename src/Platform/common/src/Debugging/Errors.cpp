#include <Common/Debugging/Errors.h>

#include <Common/Utilities/Duration.h>
#include <cstdio>
#include <cstdlib>
#include <thread>

/**
    @file Errors.cpp

    @brief This file contains definitions of functions used for reporting critical application errors

    It is very important that (std::)abort is NEVER called in place of *((volatile int*)nullptr) = 0;
    Calling abort() on Windows does not invoke unhandled exception filters - a mechanism used by WheatyExceptionReport
    to log crashes. exit(1) calls here are for static analysis tools to indicate that calling functions defined in this file
    terminates the application.
 */

#if AV_PLATFORM_WIN
#include <Windows.h>
#define Crash(message) \
    ULONG_PTR execeptionArgs[] = { reinterpret_cast<ULONG_PTR>(strdup(message)), reinterpret_cast<ULONG_PTR>(_ReturnAddress()) }; \
    RaiseException(EXCEPTION_ASSERTION_FAILURE, 0, 2, execeptionArgs);
#else
// should be easily accessible in gdb
extern "C" { char const* AvalonAssertionFailedMessage = nullptr; }
#define Crash(message) \
    AvalonAssertionFailedMessage = strdup(message); \
    *((volatile int*)nullptr) = 0; \
    exit(1);
#endif

namespace
{
    /**
    * @name MakeMessage
    * @brief Make message for display erros
    * @param messageType Message type (ASSERTION FAILED, FATAL ERROR, ERROR) end etc
    * @param file Path to file
    * @param line Line number in file
    * @param function Functionn name
    * @param message Condition to string format
    * @param fmtMessage [optional] Display format message after condition
    * @param debugInfo [optional] Display debug info
    */
    inline std::string MakeMessage(std::string_view messageType, std::string_view file, U32 line, std::string_view function,
        std::string_view message, std::string_view fmtMessage = {}, std::string_view debugInfo = {})
    {
        std::string msg = Avalon::StringFormatFmt("\n>> {}\n\n# Location '{}:{}'\n# Function '{}'\n# Condition '{}'\n", messageType, file, line, function, message);

        if (!fmtMessage.empty())
        {
            msg.append(Avalon::StringFormatFmt("# Message '{}'\n", fmtMessage));
        }

        if (!debugInfo.empty())
        {
            msg.append(Avalon::StringFormatFmt("\n# Debug info: '{}'\n", debugInfo));
        }

        return Avalon::StringFormatFmt(
            "#{0:-^{2}}#\n"
            " {1: ^{2}} \n"
            "#{0:-^{2}}#\n", "", msg, 70);
    }

    /**
    * @name MakeAbortMessage
    * @brief Make message for display erros
    * @param file Path to file
    * @param line Line number in file
    * @param function Functionn name
    * @param fmtMessage [optional] Display format message after condition
    */
    inline std::string MakeAbortMessage(std::string_view file, U32 line, std::string_view function, std::string_view fmtMessage = {})
    {
        std::string msg = Avalon::StringFormatFmt("\n>> ABORTED\n\n# Location '{}:{}'\n# Function '{}'\n", file, line, function);

        if (!fmtMessage.empty())
        {
            msg.append(Avalon::StringFormatFmt("# Message '{}'\n", fmtMessage));
        }

        return Avalon::StringFormatFmt(
            "\n#{0:-^{2}}#\n"
            " {1: ^{2}} \n"
            "#{0:-^{2}}#\n", "", msg, 70);
    }
}

void Avalon::Assert(std::string_view file, U32 line, std::string_view function, std::string_view debugInfo, std::string_view message, std::string_view fmtMessage /*= {}*/)
{
    std::string formattedMessage = MakeMessage("ASSERTION FAILED", file, line, function, message, fmtMessage, debugInfo);
    fmt::print(stderr, "{}", formattedMessage);
    fflush(stderr);
    Crash(formattedMessage.c_str());
}

void Avalon::Fatal(std::string_view file, U32 line, std::string_view function, std::string_view message, std::string_view fmtMessage /*= {}*/)
{
    std::string formattedMessage = MakeMessage("FATAL ERROR", file, line, function, message, fmtMessage);
    fmt::print(stderr, "{}", formattedMessage);
    fflush(stderr);
    std::this_thread::sleep_for(10s);
    Crash(formattedMessage.c_str());
}

void Avalon::Error(std::string_view file, U32 line, std::string_view function, std::string_view message)
{
    std::string formattedMessage = MakeMessage("ERROR", file, line, function, message);
    fmt::print(stderr, "{}", formattedMessage);
    fflush(stderr);
    std::this_thread::sleep_for(10s);
    Crash(formattedMessage.c_str());
}

void Avalon::Warning(std::string_view file, U32 line, std::string_view function, std::string_view message)
{
    std::string formattedMessage = MakeMessage("WARNING", file, line, function, message);
    fmt::print(stderr, "{}", formattedMessage);
}

void Avalon::Abort(std::string_view file, U32 line, std::string_view function, std::string_view fmtMessage /*= {}*/)
{
    std::string formattedMessage = MakeAbortMessage(file, line, function, fmtMessage);
    fmt::print(stderr, "{}", formattedMessage);
    fflush(stderr);
    std::this_thread::sleep_for(10s);
    Crash(formattedMessage.c_str());
}

void Avalon::AbortHandler(int sigval)
{
    // nothing useful to log here, no way to pass args
    std::string formattedMessage = StringFormatFmt("Caught signal {}\n", sigval);
    fmt::print(stderr, "{}", formattedMessage);
    fflush(stderr);
    Crash(formattedMessage.c_str());
}

std::string GetDebugInfo()
{
    return "";
}
