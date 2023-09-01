#pragma once

#include <Common/Utilities/StringFormat.h>

namespace Avalon
{
    // Default function
    [[noreturn]] void Assert(std::string_view file, U32 line, std::string_view function, std::string_view debugInfo, std::string_view message, std::string_view fmtMessage = {});
    [[noreturn]] void Fatal(std::string_view file, U32 line, std::string_view function, std::string_view message, std::string_view fmtMessage = {});
    [[noreturn]] void Error(std::string_view file, U32 line, std::string_view function, std::string_view message);
    [[noreturn]] void Abort(std::string_view file, U32 line, std::string_view function, std::string_view fmtMessage = {});

    template<typename... Args>
    inline void Assert(std::string_view file, U32 line, std::string_view function, std::string_view debugInfo, std::string_view message, std::string_view fmt, Args&&... args)
    {
        Assert(file, line, function, debugInfo, message, StringFormatFmt(fmt, std::forward<Args>(args)...));
    }

    template<typename... Args>
    inline void Fatal(std::string_view file, U32 line, std::string_view function, std::string_view message, std::string_view fmt, Args&&... args)
    {
        Fatal(file, line, function, message, StringFormatFmt(fmt, std::forward<Args>(args)...));
    }

    template<typename... Args>
    inline void Abort(std::string_view file, U32 line, std::string_view function, std::string_view fmt, Args&&... args)
    {
        Abort(file, line, function, StringFormatFmt(fmt, std::forward<Args>(args)...));
    }

    void Warning(std::string_view file, U32 line, std::string_view function, std::string_view message);

    [[noreturn]] void AbortHandler(int sigval);

}

String GetDebugInfo();

#define WPAssert(cond, ...) do { if (!(cond)) Avalon::Assert(__FILE__, __LINE__, __FUNCTION__, GetDebugInfo(), #cond, ##__VA_ARGS__); } while(0)
#define WPAssert_NODEBUGINFO(cond) do { if (!(cond)) Avalon::Assert(__FILE__, __LINE__, __FUNCTION__, "", #cond); } while(0)
#define WPFatal(cond, ...) do { if (!(cond)) Avalon::Fatal(__FILE__, __LINE__, __FUNCTION__, #cond, ##__VA_ARGS__); } while(0)
#define WPError(cond, msg) do { if (!(cond)) Avalon::Error(__FILE__, __LINE__, __FUNCTION__, (msg)); } while(0)
#define WPWarning(cond, msg) do { if (!(cond)) Avalon::Warning(__FILE__, __LINE__, __FUNCTION__, (msg)); } while(0)
#define WPAbort(...) do { Avalon::Abort(__FILE__, __LINE__, __FUNCTION__, ##__VA_ARGS__); } while(0)

#ifdef PERFORMANCE_PROFILING
#define ASSERT(cond, ...) ((void)0)
#define ASSERT_NODEBUGINFO(cond, ...) ((void)0)
#else
#define ASSERT WPAssert
#define ASSERT_NODEBUGINFO WPAssert_NODEBUGINFO
#endif

#if AC_PLATFORM == AC_PLATFORM_WINDOWS
#define EXCEPTION_ASSERTION_FAILURE 0xC0000420L
#endif

#define ABORT WPAbort

template <typename T>
inline T* ASSERT_NOTNULL_IMPL(T* pointer, std::string_view expr)
{
    ASSERT(pointer, "{}", expr);
    return pointer;
}

#define ASSERT_NOTNULL(pointer) ASSERT_NOTNULL_IMPL(pointer, #pointer)
