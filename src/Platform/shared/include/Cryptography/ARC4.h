#pragma once

#include <Common/Types.h>
#include <openssl/evp.h>
#include <array>

namespace Avalon::Crypto
{
    class ARC4
    {
    public:
        ARC4();
        ~ARC4();

        void Init(U8 const* seed, size_t len);

        template <typename Container>
        void Init(Container const& c) { Init(std::data(c), std::size(c)); }

        void UpdateData(U8* data, size_t len);

        template <typename Container>
        void UpdateData(Container& c) { UpdateData(std::data(c), std::size(c)); }
    private:
#if OPENSSL_VERSION_NUMBER >= 0x30000000L
        EVP_CIPHER* _cipher;
#endif
        EVP_CIPHER_CTX* _ctx;
    };
}
