#pragma once

#include <Common/Asio/AsioHacksFwd.h>
#include <Common/Types.h>

enum RealmFlags
{
    REALM_FLAG_NONE             = 0x00,
    REALM_FLAG_VERSION_MISMATCH = 0x01,
    REALM_FLAG_OFFLINE          = 0x02,
    REALM_FLAG_SPECIFYBUILD     = 0x04,
    REALM_FLAG_UNK1             = 0x08,
    REALM_FLAG_UNK2             = 0x10,
    REALM_FLAG_RECOMMENDED      = 0x20,
    REALM_FLAG_NEW              = 0x40,
    REALM_FLAG_FULL             = 0x80
};

struct RealmHandle
{
    RealmHandle()  = default;
    RealmHandle(U32 index) : Realm(index) { }

    U32 Realm{0};   // primary key in `realmlist` table

    bool operator<(RealmHandle const& r) const
    {
        return Realm < r.Realm;
    }
};

/// Type of server, this is values from second column of Cfg_Configs.dbc
enum RealmType
{
    REALM_TYPE_NORMAL       = 0,
    REALM_TYPE_PVP          = 1,
    REALM_TYPE_NORMAL2      = 4,
    REALM_TYPE_RP           = 6,
    REALM_TYPE_RPPVP        = 8,

    MAX_CLIENT_REALM_TYPE   = 14,

    REALM_TYPE_FFA_PVP      = 16 // custom, free for all pvp mode like arena PvP in all zones except rest activated places and sanctuaries
                                 // replaced by REALM_PVP in realm list
};

// Storage object for a realm
struct Realm
{
    RealmHandle Id;
    U32 Build;
    std::unique_ptr<boost::asio::ip::address> ExternalAddress;
    std::unique_ptr<boost::asio::ip::address> LocalAddress;
    std::unique_ptr<boost::asio::ip::address> LocalSubnetMask;
    U16 Port;
    std::string Name;
    U8 Type;
    RealmFlags Flags;
    U8 Timezone;
    AccountTypes AllowedSecurityLevel;
    float PopulationLevel;

    [[nodiscard]] boost::asio::ip::tcp_endpoint GetAddressForClient(boost::asio::ip::address const& clientAddr) const;
};
