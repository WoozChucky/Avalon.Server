#include <Database/DatabaseLoader.h>

#include <Common/Configuration/ConfigManager.h>
#include <Database/DatabaseEnv.h>
#include <Common/Utilities/Duration.h>
#include <Common/Logging/Log.h>
#include <Updater/DBUpdater.h>

#include <errmsg.h>
#include <mysqld_error.h>
#include <thread>

DatabaseLoader::DatabaseLoader(std::string const& logger, U32 const defaultUpdateMask, std::string_view modulesList)
    : _logger(logger),
    _modulesList(modulesList),
    _autoSetup(sConfigMgr->GetOption<bool>("Updates.AutoSetup", true)),
    _updateFlags(sConfigMgr->GetOption<U32>("Updates.EnableDatabases", defaultUpdateMask)) { }

template <class T>
DatabaseLoader& DatabaseLoader::AddDatabase(DatabaseWorkerPool<T>& pool, std::string const& name)
{
    bool const updatesEnabledForThis = DBUpdater<T>::IsEnabled(_updateFlags);

    _open.push([this, name, updatesEnabledForThis, &pool]() -> bool
    {
        std::string const dbString = sConfigMgr->GetOption<std::string>(name + "DatabaseInfo", "");
        if (dbString.empty())
        {
            LOG_ERROR(_logger, "Database {} not specified in configuration file!", name);
            return false;
        }

        U8 const asyncThreads = sConfigMgr->GetOption<U8>(name + "Database.WorkerThreads", 1);
        if (asyncThreads < 1 || asyncThreads > 32)
        {
            LOG_ERROR(_logger, "{} database: invalid number of worker threads specified. "
                      "Please pick a value between 1 and 32.", name);
            return false;
        }

        U8 const synchThreads = sConfigMgr->GetOption<U8>(name + "Database.SynchThreads", 1);

        pool.SetConnectionInfo(dbString, asyncThreads, synchThreads);

        if (U32 error = pool.Open())
        {
            // Try reconnect
            if (error == CR_CONNECTION_ERROR)
            {
                U8 const attempts = sConfigMgr->GetOption<U8>("Database.Reconnect.Attempts", 20);
                Seconds reconnectSeconds = Seconds(sConfigMgr->GetOption<U8>("Database.Reconnect.Seconds", 15));
                U8 reconnectCount = 0;

                while (reconnectCount < attempts)
                {
                    LOG_WARN(_logger, "> Retrying after {} seconds", static_cast<U32>(reconnectSeconds.count()));
                    std::this_thread::sleep_for(reconnectSeconds);
                    error = pool.Open();

                    if (error == CR_CONNECTION_ERROR)
                    {
                        reconnectCount++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Database does not exist
            if ((error == ER_BAD_DB_ERROR) && updatesEnabledForThis && _autoSetup)
            {
                // Try to create the database and connect again if auto setup is enabled
                if (DBUpdater<T>::Create(pool) && (!pool.Open()))
                {
                    error = 0;
                }
            }

            // If the error wasn't handled quit
            if (error)
            {
                LOG_ERROR(_logger, "DatabasePool {} NOT opened. There were errors opening the MySQL connections. "
                          "Check your log file for specific errors", name);

                return false;
            }
        }
        // Add the close operation
        _close.push([&pool]
        {
            pool.Close();
        });

        return true;
    });

    // Populate and update only if updates are enabled for this pool
    if (updatesEnabledForThis)
    {
        _populate.push([this, name, &pool]() -> bool
        {
            if (!DBUpdater<T>::Populate(pool))
            {
                LOG_ERROR(_logger, "Could not populate the {} database, see log for details.", name);
                return false;
            }

            return true;
        });

        _update.push([this, name, &pool]() -> bool
        {
            if (!DBUpdater<T>::Update(pool, _modulesList))
            {
                LOG_ERROR(_logger, "Could not update the {} database, see log for details.", name);
                return false;
            }

            return true;
        });
    }

    _prepare.push([this, name, &pool]() -> bool
    {
        if (!pool.PrepareStatements())
        {
            LOG_ERROR(_logger, "Could not prepare statements of the {} database, see log for details.", name);
            return false;
        }

        return true;
    });

    return *this;
}

bool DatabaseLoader::Load()
{
    if (!_updateFlags)
        LOG_INFO("sql.updates", "Automatic database updates are disabled for all databases!");

    if (!OpenDatabases())
        return false;

    if (!PopulateDatabases())
        return false;

    if (!UpdateDatabases())
        return false;

    if (!PrepareStatements())
        return false;

    return true;
}

bool DatabaseLoader::OpenDatabases()
{
    return Process(_open);
}

bool DatabaseLoader::PopulateDatabases()
{
    return Process(_populate);
}

bool DatabaseLoader::UpdateDatabases()
{
    return Process(_update);
}

bool DatabaseLoader::PrepareStatements()
{
    return Process(_prepare);
}

bool DatabaseLoader::Process(std::queue<Predicate>& queue)
{
    while (!queue.empty())
    {
        if (!queue.front()())
        {
            // Close all open databases which have a registered close operation
            while (!_close.empty())
            {
                _close.top()();
                _close.pop();
            }

            return false;
        }

        queue.pop();
    }

    return true;
}

template DatabaseLoader& DatabaseLoader::AddDatabase<LoginDatabaseConnection>(DatabaseWorkerPool<LoginDatabaseConnection>&, std::string const&);
template DatabaseLoader& DatabaseLoader::AddDatabase<CharacterDatabaseConnection>(DatabaseWorkerPool<CharacterDatabaseConnection>&, std::string const&);
template DatabaseLoader& DatabaseLoader::AddDatabase<WorldDatabaseConnection>(DatabaseWorkerPool<WorldDatabaseConnection>&, std::string const&);
