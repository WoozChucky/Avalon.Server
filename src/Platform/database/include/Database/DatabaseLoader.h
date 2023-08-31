#pragma once

#include <Common/Types.h>
#include <functional>
#include <queue>
#include <stack>
#include <string>

template <class T>
class DatabaseWorkerPool;

// A helper class to initiate all database worker pools,
// handles updating, delays preparing of statements and cleans up on failure.
class DatabaseLoader
{
public:
    DatabaseLoader(std::string const& logger, U32 const defaultUpdateMask = 0, std::string_view modulesList = {});

    // Register a database to the loader (lazy implemented)
    template <class T>
    DatabaseLoader& AddDatabase(DatabaseWorkerPool<T>& pool, std::string const& name);

    // Load all databases
    bool Load();

    enum DatabaseTypeFlags
    {
        DATABASE_NONE       = 0,

        DATABASE_LOGIN      = 1,
        DATABASE_CHARACTER  = 2,
        DATABASE_WORLD      = 4,

        DATABASE_MASK_ALL   = DATABASE_LOGIN | DATABASE_CHARACTER | DATABASE_WORLD
    };

    [[nodiscard]] U32 GetUpdateFlags() const
    {
        return _updateFlags;
    }

private:
    bool OpenDatabases();
    bool PopulateDatabases();
    bool UpdateDatabases();
    bool PrepareStatements();

    using Predicate = std::function<bool()>;
    using Closer = std::function<void()>;

    // Invokes all functions in the given queue and closes the databases on errors.
    // Returns false when there was an error.
    bool Process(std::queue<Predicate>& queue);

    std::string const _logger;
    std::string_view _modulesList;
    bool const _autoSetup;
    U32 const _updateFlags;

    std::queue<Predicate> _open, _populate, _update, _prepare;
    std::stack<Closer> _close;
};
