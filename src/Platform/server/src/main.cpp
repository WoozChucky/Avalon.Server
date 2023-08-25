#include <Logging/Log.h>
#include <Configuration/ConfigManager.h>
#include <Asio/IoContext.h>
#include <Cryptography/OpenSSLCrypto.h>
#include <Threading/ProcessPriority.h>

#include <openssl/crypto.h>
#include <boost/asio/signal_set.hpp>

#include <filesystem>
#include <csignal>

#include "Banner.h"
#include "Database/MySQLThreading.h"
#include "Versioning.h"
#include "FreezeDetector.h"
#include "CommandLine/CliRunnable.h"
#include "Game/World/World.h"
#include "Game/World/WorldSocketMgr.h"


namespace fs = std::filesystem;

#ifndef AVALON_CORE_CONFIG
#define AVALON_CORE_CONFIG "avalon.conf"
#endif


void SignalHandler(boost::system::error_code const& error, int /*signalNumber*/);

bool StartDB();
void StopDB();

void ShutdownCLIThread(std::thread* cliThread);

void WorldUpdateLoop();


int main(int argc, char** argv) {

    signal(SIGABRT, &Avalon::AbortHandler);

    auto configFile = fs::path(sConfigMgr->GetConfigPath() + std::string(AVALON_CORE_CONFIG));

    // Add file and args in config
    sConfigMgr->Configure(configFile.generic_string(), {argv, argv + argc}, {});

    if (!sConfigMgr->LoadAppConfigs())
        return 1;

    std::vector<std::string> overriddenKeys = sConfigMgr->OverrideWithEnvVariablesIfAny();

    std::shared_ptr<Avalon::Asio::IoContext> ioContext = std::make_shared<Avalon::Asio::IoContext>();

    sLog->Initialize(sConfigMgr->GetOption<bool>("Log.Async.Enable", false) ? ioContext.get() : nullptr);

    Avalon::Banner::Show("avalonserver-daemon",
                        [](std::string_view text)
                        {
                            LOG_INFO("server.worldserver", text);
                        },
                        []()
                        {
                            LOG_INFO("server.worldserver", "> Using configuration file       {}", sConfigMgr->GetFilename());
                            LOG_INFO("server.worldserver", "> Using SSL version:             {} (library: {})", OPENSSL_VERSION_TEXT, OpenSSL_version(OPENSSL_VERSION));
                            LOG_INFO("server.worldserver", "> Using Boost version:           {}.{}.{}", BOOST_VERSION / 100000, BOOST_VERSION / 100 % 1000, BOOST_VERSION % 100);
                        });

    for (std::string const& key : overriddenKeys)
        LOG_INFO("server.worldserver", "Configuration field {} was overridden with environment variable.", key);

    OpenSSLCrypto::threadsSetup();

    std::shared_ptr<void> opensslHandle(nullptr, [](void*) { OpenSSLCrypto::threadsCleanup(); });

    /// server PID file creation
    std::string pidFile = sConfigMgr->GetOption<std::string>("PidFile", "");
    if (!pidFile.empty())
    {
        if (U32 pid = CreatePIDFile(pidFile))
            LOG_ERROR("server", "Daemon PID: {}\n", pid); // outError for red color in console
        else
        {
            LOG_ERROR("server", "Cannot create PID file {} (possible error: permission)\n", pidFile);
            return 1;
        }
    }

    boost::asio::signal_set signals(*ioContext, SIGINT, SIGTERM);
#if AV_PLATFORM == AV_PLATFORM_WIN
    signals.add(SIGBREAK);
#endif

    signals.async_wait(SignalHandler);

    LOG_TRACE("server.loading", "Hello, world!");

    // Start the Boost based thread pool
    int numThreads = sConfigMgr->GetOption<S32>("ThreadPool", 1);
    std::shared_ptr<std::vector<std::thread>> threadPool(new std::vector<std::thread>(), [ioContext](std::vector<std::thread>* del)
    {
        ioContext->stop();
        for (std::thread& thr : *del)
            thr.join();

        delete del;
    });

    if (numThreads < 1)
    {
        numThreads = 1;
    }

    for (int i = 0; i < numThreads; ++i)
    {
        threadPool->push_back(std::thread([ioContext]()
                                          {
                                              ioContext->run();
                                          }));
    }

    // Set process priority according to configuration settings
    SetProcessPriority("server.worldserver", sConfigMgr->GetOption<S32>(CONFIG_PROCESSOR_AFFINITY, 0), sConfigMgr->GetOption<bool>(CONFIG_HIGH_PRIORITY, false));

    if (!StartDB())
        return 1;

    std::shared_ptr<void> dbHandle(nullptr, [](void*) { StopDB(); });

    sWorld->SetInitialWorldSettings();

    // Launch the listener socket
    U16 socketPort = sConfigMgr->GetOption<U16>("WorldServerPort", 21000);
    std::string worldListener = sConfigMgr->GetOption<std::string>("BindIP", "0.0.0.0");

    S32 networkThreads = sConfigMgr->GetOption<S32>("Network.Threads", 1);

    if (networkThreads <= 0)
    {
        LOG_ERROR("server.worldserver", "Network.Threads must be greater than 0");
        return 1;
    }

    if (!sWorldSocketMgr.StartWorldNetwork(*ioContext, worldListener, socketPort, networkThreads))
    {
        LOG_ERROR("server.worldserver", "Failed to initialize network");
        World::StopNow(ERROR_EXIT_CODE);
        return 1;
    }

    std::shared_ptr<void> sWorldSocketMgrHandle(nullptr, [](void*)
    {
        sWorld->KickAll();              // save and kick all players
        sWorld->UpdateSessions(1);      // real players unload required UpdateSessions call

        sWorldSocketMgr.StopNetwork();
    });

    // Start the freeze check callback cycle in 5 seconds (cycle itself is 1 sec)
    std::shared_ptr<FreezeDetector> freezeDetector;
    if (S32 coreStuckTime = sConfigMgr->GetOption<S32>("MaxCoreStuckTime", 60))
    {
        freezeDetector = std::make_shared<FreezeDetector>(*ioContext, coreStuckTime * 1000);
        FreezeDetector::Start(freezeDetector);
        LOG_INFO("server.worldserver", "Starting up anti-freeze thread ({} seconds max stuck time)...", coreStuckTime);
    }

    LOG_INFO("server.worldserver", "{} (avalonserver-daemon) ready...", "GitRevision::GetFullVersion()");

    // Launch CliRunnable thread
    std::shared_ptr<std::thread> cliThread;

    if (sConfigMgr->GetOption<bool>("Console.Enable", true))
    {
        cliThread.reset(new std::thread(CliThread), &ShutdownCLIThread);
    }

    // game update loop here ...
    WorldUpdateLoop();

    // Shutdown starts here
    threadPool.reset();

    sLog->SetSynchronous();

    LOG_INFO("server.worldserver", "Halting process...");

    return World::GetExitCode();
}

void SignalHandler(boost::system::error_code const& error, int /*signalNumber*/)
{
    if (!error) {
        LOG_INFO("server.worldserver", "Signal received, shutting down world.");
        // World::StopNow(SHUTDOWN_EXIT_CODE);
    }
}

bool StartDB()
{
    MySQL::Library_Init();

    U32 version = MySQL::GetLibraryVersion();

    if (version < MARIADB_MIN_SUPPORTED_VERSION) {
        LOG_ERROR("server.worldserver", "Your MariaDb library is too old. Please update it to at least 10.2.0");
        return false;
    }

    LOG_INFO("server.worldserver", "> Using MariaDb version:           {}.{}.{}", version / 10000, version / 100 % 1000, version % 100);

    return true;
}

void StopDB()
{
    MySQL::Library_End();
}

void ShutdownCLIThread(std::thread* cliThread)
{
    if (cliThread)
    {
#ifdef _WIN32
        // First try to cancel any I/O in the CLI thread
        if (!CancelSynchronousIo(cliThread->native_handle()))
        {
            // if CancelSynchronousIo() fails, print the error and try with old way
            DWORD errorCode = GetLastError();
            LPCSTR errorBuffer;

            DWORD formatReturnCode = FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
                                                   nullptr, errorCode, 0, (LPTSTR)&errorBuffer, 0, nullptr);
            if (!formatReturnCode)
                errorBuffer = "Unknown error";

            LOG_DEBUG("server.worldserver", "Error cancelling I/O of CliThread, error code {}, detail: {}", U32(errorCode), errorBuffer);

            if (!formatReturnCode)
                LocalFree((LPSTR)errorBuffer);

            // send keyboard input to safely unblock the CLI thread
            INPUT_RECORD b[4];
            HANDLE hStdIn = GetStdHandle(STD_INPUT_HANDLE);
            b[0].EventType = KEY_EVENT;
            b[0].Event.KeyEvent.bKeyDown = TRUE;
            b[0].Event.KeyEvent.uChar.AsciiChar = 'X';
            b[0].Event.KeyEvent.wVirtualKeyCode = 'X';
            b[0].Event.KeyEvent.wRepeatCount = 1;

            b[1].EventType = KEY_EVENT;
            b[1].Event.KeyEvent.bKeyDown = FALSE;
            b[1].Event.KeyEvent.uChar.AsciiChar = 'X';
            b[1].Event.KeyEvent.wVirtualKeyCode = 'X';
            b[1].Event.KeyEvent.wRepeatCount = 1;

            b[2].EventType = KEY_EVENT;
            b[2].Event.KeyEvent.bKeyDown = TRUE;
            b[2].Event.KeyEvent.dwControlKeyState = 0;
            b[2].Event.KeyEvent.uChar.AsciiChar = '\r';
            b[2].Event.KeyEvent.wVirtualKeyCode = VK_RETURN;
            b[2].Event.KeyEvent.wRepeatCount = 1;
            b[2].Event.KeyEvent.wVirtualScanCode = 0x1c;

            b[3].EventType = KEY_EVENT;
            b[3].Event.KeyEvent.bKeyDown = FALSE;
            b[3].Event.KeyEvent.dwControlKeyState = 0;
            b[3].Event.KeyEvent.uChar.AsciiChar = '\r';
            b[3].Event.KeyEvent.wVirtualKeyCode = VK_RETURN;
            b[3].Event.KeyEvent.wVirtualScanCode = 0x1c;
            b[3].Event.KeyEvent.wRepeatCount = 1;
            DWORD numb;
            WriteConsoleInput(hStdIn, b, 4, &numb);
        }
#endif
        cliThread->join();
        delete cliThread;
    }
}

void WorldUpdateLoop()
{
    U32 minUpdateDiff = U32(sConfigMgr->GetOption<S32>("MinWorldUpdateTime", 1));
    U32 realCurrTime = 0;
    U32 realPrevTime = getMSTime();

    U32 maxCoreStuckTime = U32(sConfigMgr->GetOption<S32>("MaxCoreStuckTime", 60)) * 1000;
    U32 halfMaxCoreStuckTime = maxCoreStuckTime / 2;
    if (!halfMaxCoreStuckTime)
        halfMaxCoreStuckTime = std::numeric_limits<U32>::max();

    while (!World::IsStopped())
    {
        ++World::m_worldLoopCounter;
        realCurrTime = getMSTime();

        U32 diff = getMSTimeDiff(realPrevTime, realCurrTime);
        if (diff < minUpdateDiff)
        {
            U32 sleepTime = minUpdateDiff - diff;
            if (sleepTime >= halfMaxCoreStuckTime)
                LOG_ERROR("server.worldserver", "WorldUpdateLoop() waiting for {} ms with MaxCoreStuckTime set to {} ms", sleepTime, maxCoreStuckTime);
            // sleep until enough time passes that we can update all timers
            std::this_thread::sleep_for(Milliseconds(sleepTime));
            continue;
        }

        sWorld->Update(diff);
        realPrevTime = realCurrTime;
    }

}
