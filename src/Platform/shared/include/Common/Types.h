#pragma once

#include <cstdint>
#include <string>
#include <string_view>

#include <Utilities/advstd.h>


using S8 = int8_t;
using S16 = int16_t;
using S32 = int32_t;
using S64 = int64_t;

using U8 = uint8_t;
using U16 = uint16_t;
using U32 = uint32_t;
using U64 = uint64_t;

using F32 = float;

using String = std::string;
using StringView = std::string_view;

namespace Avalon {
    // end "iterator" tag for find_type_if
    struct find_type_end;

    template<template<typename...> typename Check, typename... Ts>
    struct find_type_if;

    template<template<typename...> typename Check>
    struct find_type_if<Check>
    {
        using type = find_type_end;
    };

    template<template<typename...> typename Check, typename T1, typename... Ts>
    struct find_type_if<Check, T1, Ts...> : std::conditional_t<Check<T1>::value, advstd::type_identity<T1>, find_type_if<Check, Ts...>>
    {
    };

    template<template<typename...> typename Check, typename... Ts>
    using find_type_if_t = typename find_type_if<Check, Ts...>::type;

    template <typename T>
    struct dependant_false { static constexpr bool value = false; };

    template <typename T>
    constexpr bool dependant_false_v = dependant_false<T>::value;
}

constexpr auto MINUTE = 60;
constexpr auto HOUR = MINUTE * 60;
constexpr auto DAY = HOUR * 24;
constexpr auto WEEK = DAY * 7;
constexpr auto MONTH = DAY * 30;
constexpr auto YEAR = MONTH * 12;
constexpr auto IN_MILLISECONDS = 1000;
