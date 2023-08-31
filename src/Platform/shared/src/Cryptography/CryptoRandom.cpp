#include <Cryptography/CryptoRandom.h>

#include <Debugging/Errors.h>
#include <openssl/rand.h>

void Avalon::Crypto::GetRandomBytes(U8* buf, size_t len)
{
    int result = RAND_bytes(buf, len);
    ASSERT(result == 1, "Not enough randomness in OpenSSL's entropy pool. What in the world are you running on?");
}
