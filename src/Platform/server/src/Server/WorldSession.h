#pragma once

class WorldSession {
public:
    Player* GetPlayer() const { return nullptr; }
    bool PlayerLoading() { return false; }
    void KickPlayer(std::string reason, bool = true) {}
    U32 GetAccountId() const { return 0; }
    bool HandleSocketClosed() { return true; }
    void SetOfflineTime(U32) {}
    void InitializeSession() {}
    void SetInQueue(bool) {}
};
