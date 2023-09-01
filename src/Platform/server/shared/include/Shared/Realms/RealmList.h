#pragma once

#include <Common/Types.h>
#include <Shared/Realms/Realm.h>
#include <array>
#include <map>
#include <unordered_set>
#include <vector>

struct RealmBuildInfo
{
    U32 Build;
    U32 MajorVersion;
    U32 MinorVersion;
    U32 BugfixVersion;
    std::array<char, 4> HotfixVersion;
    std::array<U8, 20> WindowsHash;
    std::array<U8, 20> MacHash;
};

namespace boost::system
{
    class error_code;
}

/// Storage object for the list of realms on the server
class RealmList
{
public:
    using RealmMap = std::map<RealmHandle, Realm>;

    static RealmList* Instance();

    void Initialize(Avalon::Asio::IoContext& ioContext, U32 updateInterval);
    void Close();

    [[nodiscard]] RealmMap const& GetRealms() const { return _realms; }
    [[nodiscard]] Realm const* GetRealm(RealmHandle const& id) const;

    [[nodiscard]] RealmBuildInfo const* GetBuildInfo(U32 build) const;

private:
    RealmList();
    ~RealmList() = default;

    void LoadBuildInfo();
    void UpdateRealms(boost::system::error_code const& error);
    void UpdateRealm(RealmHandle const& id, U32 build, std::string const& name,
        boost::asio::ip::address&& address, boost::asio::ip::address&& localAddr, boost::asio::ip::address&& localSubmask,
        U16 port, U8 icon, RealmFlags flag, U8 timezone, AccountTypes allowedSecurityLevel, float population);

    std::vector<RealmBuildInfo> _builds;
    RealmMap _realms;
    U32 _updateInterval{0};
    std::unique_ptr<Avalon::Asio::DeadlineTimer> _updateTimer;
    std::unique_ptr<Avalon::Asio::Resolver> _resolver;
};

#define sRealmList RealmList::Instance()
