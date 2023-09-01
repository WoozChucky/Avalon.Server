#pragma once

#include <Common/Types.h>

#include <Common/Cryptography/ARC4.h>
#include <Common/Cryptography/Authentication/AuthDefines.h>

class AuthCrypt
{
public:
    AuthCrypt() = default;

    void Init(SessionKey const& K);
    void DecryptRecv(U8* data, size_t len);
    void EncryptSend(U8* data, size_t len);

    bool IsInitialized() const { return _initialized; }

private:
    Avalon::Crypto::ARC4 _clientDecrypt;
    Avalon::Crypto::ARC4 _serverEncrypt;
    bool _initialized{ false };
};
