#include <Game/Globals/ObjectAccessor.h>

#include <Common/Logging/Log.h>
#include <Common/Utilities/Util.h>

#include <Game/Server/Protocol/Opcodes.h>
#include <Game/Entities/Player/Player.h>

bool normalizePlayerName(std::string& name)
{
    if (name.empty())
        return false;

    if (name.find(" ") != std::string::npos)
        return false;

    std::wstring tmp;
    if (!Utf8toWStr(name, tmp))
        return false;

    wstrToLower(tmp);
    if (!tmp.empty())
        tmp[0] = wcharToUpper(tmp[0]);

    if (!WStrToUtf8(tmp, name))
        return false;

    return true;
}

template<class T>
void HashMapHolder<T>::Insert(T* o)
{
    static_assert(std::is_same<Player, T>::value,
        "Only Player and Motion Transport can be registered in global HashMapHolder");

    std::unique_lock<std::shared_mutex> lock(*GetLock());

    GetContainer()[o->GetGUID()] = o;
}

template<class T>
void HashMapHolder<T>::Remove(T* o)
{
    std::unique_lock<std::shared_mutex> lock(*GetLock());

    GetContainer().erase(o->GetGUID());
}

template<class T>
T* HashMapHolder<T>::Find(ObjectGuid guid)
{
    std::shared_lock<std::shared_mutex> lock(*GetLock());

    typename MapType::iterator itr = GetContainer().find(guid);
    return (itr != GetContainer().end()) ? itr->second : nullptr;
}

template<class T>
auto HashMapHolder<T>::GetContainer() -> MapType&
{
    static MapType _objectMap;
    return _objectMap;
}

template<class T>
std::shared_mutex* HashMapHolder<T>::GetLock()
{
    static std::shared_mutex _lock;
    return &_lock;
}

HashMapHolder<Player>::MapType const& ObjectAccessor::GetPlayers()
{
    return HashMapHolder<Player>::GetContainer();
}

template class HashMapHolder<Player>;

namespace PlayerNameMapHolder
{
    typedef std::unordered_map<std::string, Player*> MapType;
    static MapType PlayerNameMap;

    void Insert(Player* p)
    {
        PlayerNameMap[p->GetName()] = p;
    }

    void Remove(Player* p)
    {
        PlayerNameMap.erase(p->GetName());
    }

    void RemoveByName(std::string const& name)
    {
        PlayerNameMap.erase(name);
    }

    Player* Find(std::string const& name)
    {
        std::string charName(name);
        if (!normalizePlayerName(charName))
            return nullptr;

        auto itr = PlayerNameMap.find(charName);
        return (itr != PlayerNameMap.end()) ? itr->second : nullptr;
    }

} // namespace PlayerNameMapHolder

Player* ObjectAccessor::FindPlayer(ObjectGuid const guid)
{
    Player* player = HashMapHolder<Player>::Find(guid);
    return player && player->IsInWorld() ? player : nullptr;
}

Player* ObjectAccessor::FindPlayerByLowGUID(ObjectGuid::LowType lowguid)
{
    ObjectGuid guid = ObjectGuid::Create<HighGuid::Player>(lowguid);
    return ObjectAccessor::FindPlayer(guid);
}

Player* ObjectAccessor::FindConnectedPlayer(ObjectGuid const guid)
{
    return HashMapHolder<Player>::Find(guid);
}

void ObjectAccessor::SaveAllPlayers()
{
    std::shared_lock<std::shared_mutex> lock(*HashMapHolder<Player>::GetLock());

    HashMapHolder<Player>::MapType const& m = GetPlayers();
    for (HashMapHolder<Player>::MapType::const_iterator itr = m.begin(); itr != m.end(); ++itr)
        itr->second->SaveToDB(false, false);
}

Player* ObjectAccessor::FindPlayerByName(std::string const& name, bool checkInWorld)
{
    if (Player* player = PlayerNameMapHolder::Find(name))
        if (!checkInWorld || player->IsInWorld())
            return player;

    return nullptr;
}

template<>
void ObjectAccessor::AddObject(Player* player)
{
    HashMapHolder<Player>::Insert(player);
    PlayerNameMapHolder::Insert(player);
}

template<>
void ObjectAccessor::RemoveObject(Player* player)
{
    HashMapHolder<Player>::Remove(player);
    PlayerNameMapHolder::Remove(player);
}

void ObjectAccessor::UpdatePlayerNameMapReference(std::string oldname, Player* player)
{
    PlayerNameMapHolder::RemoveByName(oldname);
    PlayerNameMapHolder::Insert(player);
}
