#pragma once

class Player {
public:
    Player();
    ~Player();

    bool IsInWorld() const { return false; };
    U32 GetZoneId() const {return 0;};
};
