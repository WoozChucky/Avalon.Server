#pragma once

#include <Common/Types.h>
#include <Database/DatabaseEnvFwd.h>
#include "../Object/ObjectGuid.h"

enum PlayerSlots
{
    // first slot for item stored (in any way in player m_items data)
    PLAYER_SLOT_START           = 0,
    // last+1 slot for item stored (in any way in player m_items data)
    PLAYER_SLOT_END             = 150,
    PLAYER_SLOTS_COUNT          = (PLAYER_SLOT_END - PLAYER_SLOT_START)
};

class WorldSession;

class Player {
public:
    Player();
    ~Player();

    bool IsInWorld() const { return false; };
    U32 GetZoneId() const {return 0;};

    static void DeleteFromDB(ObjectGuid::LowType lowGuid, U32 accountId, bool updateRealmChars, bool deleteFinally) {};

    void SaveToDB(bool create, bool logout) {};
    void SaveToDB(CharacterDatabaseTransaction trans, bool create, bool logout) {};

    [[nodiscard]] ObjectGuid GetGUID() const { return ObjectGuid::Empty; }

    [[nodiscard]] WorldSession* GetSession() const { return m_session; }
    void SetSession(WorldSession* sess) { m_session = sess; }

    [[nodiscard]] std::string const& GetName() const { return m_name; }
    void SetName(std::string const& newname) { m_name = newname; }

private:
    WorldSession* m_session;
    std::string m_name;
};
