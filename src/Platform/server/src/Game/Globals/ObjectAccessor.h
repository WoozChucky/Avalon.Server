#pragma once

#include <Common/Types.h>

#include "../Entities/Object/ObjectGuid.h"

#include <mutex>
#include <set>
#include <shared_mutex>
#include <unordered_map>

class Player;


template <class T>
class HashMapHolder
{
    //Non instanceable only static
    HashMapHolder() = default;

public:

    typedef std::unordered_map<ObjectGuid, T*> MapType;

    static void Insert(T* o);

    static void Remove(T* o);

    static T* Find(ObjectGuid guid);

    static MapType& GetContainer();

    static std::shared_mutex* GetLock();
};

namespace ObjectAccessor
{
    // these functions return objects if found in whole world
    // ACCESS LIKE THAT IS NOT THREAD SAFE
    Player* FindPlayer(ObjectGuid const guid);
    Player* FindPlayerByLowGUID(ObjectGuid::LowType lowguid);
    Player* FindConnectedPlayer(ObjectGuid const guid);
    Player* FindPlayerByName(std::string const& name, bool checkInWorld = true);

    // when using this, you must use the hashmapholder's lock
    HashMapHolder<Player>::MapType const& GetPlayers();

    template<class T>
    void AddObject(T* object)
    {
        HashMapHolder<T>::Insert(object);
    }

    template<class T>
    void RemoveObject(T* object)
    {
        HashMapHolder<T>::Remove(object);
    }

    void SaveAllPlayers();

    template<>
    void AddObject(Player* player);

    template<>
    void RemoveObject(Player* player);

    void UpdatePlayerNameMapReference(std::string oldname, Player* player);
}