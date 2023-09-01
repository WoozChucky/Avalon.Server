#pragma once

#include <Common/Types.h>
#include <Common/Utilities/advstd.h>
#include <Common/Logging/LogCommon.h>
#include <Common/Logging/Appender.h>

#include <fmt/format.h>
#include <fmt/printf.h>

#include <locale>

template<>
struct fmt::formatter<LogLevel> : fmt::formatter<std::string>
{
    auto format(LogLevel my, format_context &ctx) const -> decltype(ctx.out())
    {
        return format_to(ctx.out(), "[{}]", Appender::getLogLevelString(my));
    }
};

namespace Avalon {

    template<typename Format, typename... Args>
    inline std::string StringFormat(Format&& fmt, Args&& ... args)
    {
        try
        {
            return fmt::sprintf(std::forward<Format>(fmt), std::forward<Args>(args)...);
        }
        catch (const fmt::format_error& formatError)
        {
            String error = "An error occurred formatting string \"" + String(fmt) + "\" : " + std::string(formatError.what());
            return error;
        }
    }

    template<typename... Args>
    inline String StringFormatFmt(StringView fmt, Args&&... args)
    {
        try
        {
            return fmt::format(fmt, std::forward<Args>(args)...);
        }
        catch (const fmt::format_error& formatError)
        {
            return fmt::format("An error occurred formatting string \"{}\": {}", fmt, formatError.what());
        }
    }

    /// Returns true if the given char pointer is null.
    inline bool IsFormatEmptyOrNull(char const* fmt)
    {
        return fmt == nullptr;
    }

    /// Returns true if the given std::string is empty.
    inline bool IsFormatEmptyOrNull(StringView fmt)
    {
        return fmt.empty();
    }

    template<class Str>
    Str Trim(const Str& s, const std::locale& loc = std::locale())
    {
        typename Str::const_iterator first = s.begin();
        typename Str::const_iterator end = s.end();

        while (first != end && std::isspace(*first, loc))
        {
            ++first;
        }

        if (first == end)
        {
            return Str();
        }

        typename Str::const_iterator last = end;

        do
        {
            --last;
        } while (std::isspace(*last, loc));

        if (first != s.begin() || last + 1 != end)
        {
            return Str(first, last + 1);
        }

        return s;
    }

    String TrimRightInPlace(String& str);

    /**
     * @brief Util function to add a suffix char. Can be used to add a slash at the end of a path
     *
     * @param str String where to apply the suffix
     * @param suffix Character to add at the end of the str
     * @return std::string Suffixed string
     */
    String AddSuffixIfNotExists(String str, const char suffix);

}
