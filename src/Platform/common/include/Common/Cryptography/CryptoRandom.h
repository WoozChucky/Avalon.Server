#pragma once

#include <Common/Types.h>
#include <array>

namespace Avalon::Crypto
{
    void GetRandomBytes(U8* buf, size_t len);

    template <typename Container>
    void GetRandomBytes(Container& c)
    {
        GetRandomBytes(std::data(c), std::size(c));
    }

    template <size_t S>
    std::array<U8, S> GetRandomBytes()
    {
        std::array<U8, S> arr;
        GetRandomBytes(arr);
        return arr;
    }
}
