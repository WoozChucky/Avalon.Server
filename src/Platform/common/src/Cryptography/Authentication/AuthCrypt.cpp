#include <Common/Cryptography/Authentication/AuthCrypt.h>

#include <Common/Debugging/Errors.h>
#include <Common/Cryptography/HMAC.h>

void AuthCrypt::Init(SessionKey const& K)
{
    U8 ServerEncryptionKey[] = { 0xCC, 0x98, 0xAE, 0x04, 0xE8, 0x97, 0xEA, 0xCA, 0x12, 0xDD, 0xC0, 0x93, 0x42, 0x91, 0x53, 0x57 };
    _serverEncrypt.Init(Avalon::Crypto::HMAC_SHA1::GetDigestOf(ServerEncryptionKey, K));

    U8 ServerDecryptionKey[] = { 0xC2, 0xB3, 0x72, 0x3C, 0xC6, 0xAE, 0xD9, 0xB5, 0x34, 0x3C, 0x53, 0xEE, 0x2F, 0x43, 0x67, 0xCE };
    _clientDecrypt.Init(Avalon::Crypto::HMAC_SHA1::GetDigestOf(ServerDecryptionKey, K));

    // Drop first 1024 bytes, as WoW uses ARC4-drop1024.
    std::array<U8, 1024> syncBuf{};
    _serverEncrypt.UpdateData(syncBuf);
    _clientDecrypt.UpdateData(syncBuf);

    _initialized = true;
}

void AuthCrypt::DecryptRecv(U8* data, size_t len)
{
    ASSERT(_initialized);
    _clientDecrypt.UpdateData(data, len);
}

void AuthCrypt::EncryptSend(U8* data, size_t len)
{
    ASSERT(_initialized);
    _serverEncrypt.UpdateData(data, len);
}
