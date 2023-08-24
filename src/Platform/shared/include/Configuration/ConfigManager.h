#pragma once

#include <Common/Types.h>

#include <stdexcept>
#include <vector>

class ConfigManager {

    ConfigManager() = default;
    ConfigManager(ConfigManager const&) = delete;
    ConfigManager& operator=(ConfigManager const&) = delete;
    ~ConfigManager() = default;

public:
    bool LoadAppConfigs(bool isReload = false);
    bool LoadModulesConfigs(bool isReload = false, bool isNeedPrintInfo = true);
    void Configure(String const& initFileName, std::vector<String> args, StringView modulesConfigList = {});

    static ConfigManager* Instance();

    bool Reload();

    /// Overrides configuration with environment variables and returns overridden keys
    std::vector<std::string> OverrideWithEnvVariablesIfAny();

    String const GetFilename();
    String const GetConfigPath();
    [[nodiscard]] std::vector<String> const& GetArguments() const;
    std::vector<String> GetKeysByString(String const& name);

    template<class T>
    T GetOption(String const& name, T const& def, bool showLogs = true) const;

    /*
     * Deprecated geters. This geters will be deleted
     */

    [[deprecated("Use GetOption<std::string> instead")]]
    std::string GetStringDefault(std::string const& name, const std::string& def, bool showLogs = true);

    [[deprecated("Use GetOption<bool> instead")]]
    bool GetBoolDefault(std::string const& name, bool def, bool showLogs = true);

    [[deprecated("Use GetOption<int32> instead")]]
    int GetIntDefault(std::string const& name, int def, bool showLogs = true);

    [[deprecated("Use GetOption<float> instead")]]
    float GetFloatDefault(std::string const& name, float def, bool showLogs = true);

    /*
     * End deprecated geters
     */

    bool IsDryRun() { return dryRun; }
    void SetDryRun(bool mode) { dryRun = mode; }

private:
    /// Method used only for loading main configuration files (authserver.conf and worldserver.conf)
    bool LoadInitial(String const& file, bool isReload = false);
    bool LoadAdditionalFile(String file, bool isOptional = false, bool isReload = false);

    template<class T>
    T GetValueDefault(String const& name, T const& def, bool showLogs = true) const;

    bool dryRun = false;

    std::vector<String /*config variant*/> _moduleConfigFiles;
};

class ConfigException : public std::length_error
{
public:
    explicit ConfigException(std::string const& message) : std::length_error(message) { }
};

#define sConfigMgr ConfigManager::Instance()
