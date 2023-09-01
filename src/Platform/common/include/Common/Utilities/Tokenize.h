#pragma once

#include <string_view>
#include <vector>

namespace Avalon
{
    std::vector<std::string_view> Tokenize(std::string_view str, char sep, bool keepEmpty);

    /* this would return string_view into temporary otherwise */
    std::vector<std::string_view> Tokenize(std::string&&, char, bool) = delete;
    std::vector<std::string_view> Tokenize(std::string const&&, char, bool) = delete;

    /* the delete overload means we need to make this explicit */
    inline std::vector<std::string_view> Tokenize(char const* str, char sep, bool keepEmpty) { return Tokenize(std::string_view(str ? str : ""), sep, keepEmpty); }
}
