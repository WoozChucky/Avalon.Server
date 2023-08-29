#pragma once

#include <Common/Types.h>

#include "WorldPacket.h"

class Player;

class WorldSession {
public:
    Player* GetPlayer() { return nullptr; }
    bool PlayerLoading() { return false; }
    void KickPlayer(std::string reason, bool = true) {}
    U32 GetAccountId() const { return 0; }
    bool HandleSocketClosed() { return true; }
    void SetOfflineTime(U32) {}
    void InitializeSession() {}
    void SetInQueue(bool) {}
public:                                                 // opcodes handlers
    void Handle_NULL(WorldPacket& null);                // not used
    void Handle_EarlyProccess(WorldPacket& recvPacket); // just mark packets processed in WorldSocket::OnRead
    void Handle_ServerSide(WorldPacket& recvPacket);    // sever side only, can't be accepted from client
    void Handle_Deprecated(WorldPacket& recvPacket);    // never used anymore by client
};
