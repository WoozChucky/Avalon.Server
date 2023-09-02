#pragma once

#include <string_view>

namespace Avalon::Banner
{
    void Show(std::string_view applicationName, void(*log)(std::string_view text), void(*logExtraInfo)());
}

