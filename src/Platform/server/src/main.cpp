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



namespace fs = std::filesystem;

#ifndef AVALON_CORE_CONFIG
#define AVALON_CORE_CONFIG "avalon.conf"
#endif

void SignalHandler(boost::system::error_code const& error, int /*signalNumber*/)
{
    if (!error) {
        LOG_INFO("server.worldserver", "Signal received, shutting down world.");
        // World::StopNow(SHUTDOWN_EXIT_CODE);
    }
}

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

    Avalon::Banner::Show("server-daemon",
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

    return 0;
}
