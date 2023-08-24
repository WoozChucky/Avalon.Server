#pragma once

#include <cstddef>
#include <type_traits>

// this namespace holds implementations of upcoming stdlib features that our c++ version doesn't have yet
namespace advstd
{
    // This workaround for std::remove_cvref_t, std::type_identify is needed for GCC 8...
    /// @todo: remove when we drop GCC 8 support. https://en.cppreference.com/w/cpp/compiler_support/20
    // C++20 advstd::remove_cvref_t
    template <class T>
    using remove_cvref_t = std::remove_cv_t<std::remove_reference_t<T>>;

    // C++20 std::type_identity
    template <typename T>
    struct type_identity
    {
        using type = T;
    };

    // C++20 std::type_identity_t
    template <typename T>
    using type_identity_t = typename type_identity<T>::type;
}
