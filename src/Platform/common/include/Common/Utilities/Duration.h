#pragma once

#include <chrono>

/// Microseconds shorthand typedef.
using Microseconds = std::chrono::microseconds;

/// Milliseconds shorthand typedef.
using Milliseconds = std::chrono::milliseconds;

/// Seconds shorthand typedef.
using Seconds = std::chrono::seconds;

/// Minutes shorthand typedef.
using Minutes = std::chrono::minutes;

/// Hours shorthand typedef.
using Hours = std::chrono::hours;

/// Days shorthand typedef.
using Days = std::chrono::duration<__INT64_TYPE__, std::ratio<86400>>;

/// Weeks shorthand typedef.
using Weeks = std::chrono::duration<__INT64_TYPE__, std::ratio<604800>>;

/// Years shorthand typedef.
using Years = std::chrono::duration<__INT64_TYPE__, std::ratio<31556952>>;

/// Months shorthand typedef.
using Months = std::chrono::duration<__INT64_TYPE__, std::ratio<2629746>>;

/// time_point shorthand typedefs
using TimePoint = std::chrono::steady_clock::time_point;
using SystemTimePoint = std::chrono::system_clock::time_point;

/// Makes std::chrono_literals globally available.
using namespace std::chrono_literals;

constexpr Days operator""_days(unsigned long long days)
{
    return Days(days);
}

constexpr Weeks operator""_weeks(unsigned long long weeks)
{
    return Weeks(weeks);
}

constexpr Years operator""_years(unsigned long long years)
{
    return Years(years);
}

constexpr Months operator""_months(unsigned long long months)
{
    return Months(months);
}

