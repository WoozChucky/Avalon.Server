#pragma once

#include "Common/Types.h"
#include <array>

constexpr size_t SESSION_KEY_LENGTH = 40;
using SessionKey = std::array<U8, SESSION_KEY_LENGTH>;
