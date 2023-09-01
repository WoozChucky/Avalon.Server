#pragma once

#include <Common/Types.h>
#include <Common/Utilities/Tokenize.h>
#include <Common/Utilities/StringFormat.h>
#include <Common/Utilities/StringConvert.h>
#include <Common/Utilities/Util.h>
#include <Common/Utilities/advstd.h>
#include <Common/Logging/Log.h>

#include <cstdlib>
#include <mutex>
#include <unordered_map>
#include <fstream>
#include <stdexcept>
#include <vector>

class ConfigException : public std::length_error
{
public:
    explicit ConfigException(std::string const& message) : std::length_error(message) { }
};

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
    T GetOption(String const& name, T const& def, bool showLogs = true) const
    {
        return GetValueDefault<T>(name, def, showLogs);
    };

    template<>
    bool GetOption<bool>(std::string const& name, bool const& def, bool showLogs /*= true*/) const
    {
        std::string val = GetValueDefault(name, std::string(def ? "1" : "0"), showLogs);

        auto boolVal = Avalon::StringTo<bool>(val);
        if (!boolVal)
        {
            if (showLogs)
            {
                LOG_ERROR("server.loading", "> Config: Bad value defined for name '{}', going to use '{}' instead",
                          name, def ? "true" : "false");
            }

            return def;
        }

        return *boolVal;
    }

    bool IsDryRun() { return dryRun; }
    void SetDryRun(bool mode) { dryRun = mode; }

private:
    /// Method used only for loading main configuration files (avalon.conf)
    bool LoadInitial(String const& file, bool isReload = false);
    bool LoadAdditionalFile(String file, bool isOptional = false, bool isReload = false);

    template<class T>
    T GetValueDefault(String const& name, T const& def, bool showLogs = true) const
    {
        std::string strValue;
        auto const& itr = _configOptions.find(name);
        if (itr == _configOptions.end())
        {
            Optional<std::string> envVar = EnvVarForIniKey(name);
            if (!envVar)
            {
                if (showLogs)
                {
                    LOG_ERROR("server.loading", "> Config: Missing property {} in config file {}, add \"{} = {}\" to this file.",
                              name, _filename, name, Avalon::ToString(def));
                }

                return def;
            }

            if (showLogs)
            {
                LOG_WARN("server.loading", "Missing property {} in config file {}, recovered with environment '{}' value.",
                         name.c_str(), _filename.c_str(), envVar->c_str());
            }

            strValue = *envVar;
        }
        else
        {
            strValue = itr->second;
        }

        auto value = Avalon::StringTo<T>(strValue);
        if (!value)
        {
            if (showLogs)
            {
                LOG_ERROR("server.loading", "> Config: Bad value defined for name '{}', going to use '{}' instead",
                          name, Avalon::ToString(def));
            }

            return def;
        }

        return *value;
    }

    template<>
    [[nodiscard]] std::string GetValueDefault<std::string>(std::string const& name, std::string const& def, bool showLogs /*= true*/) const
    {
        auto const& itr = _configOptions.find(name);
        if (itr == _configOptions.end())
        {
            Optional<std::string> envVar = EnvVarForIniKey(name);
            if (envVar)
            {
                if (showLogs)
                {
                    LOG_WARN("server.loading", "Missing property {} in config file {}, recovered with environment '{}' value.",
                             name.c_str(), _filename.c_str(), envVar->c_str());
                }

                return *envVar;
            }

            if (showLogs)
            {
                LOG_ERROR("server.loading", "> Config: Missing property {} in config file {}, add \"{} = {}\" to this file.",
                          name, _filename, name, def);
            }

            return def;
        }

        return itr->second;
    }

    bool dryRun = false;

    std::vector<String /*config variant*/> _moduleConfigFiles;


private:
    String _filename;
    std::vector<String> _additonalFiles;
    std::vector<String> _args;
    std::unordered_map<String /*name*/, String /*value*/> _configOptions;
    std::mutex _configLock;

    // Check system configs like *server.conf*
    bool IsAppConfig(StringView fileName)
    {
        size_t foundConfig = fileName.find("avalon.conf");

        return foundConfig != StringView::npos;
    }

    // Check logging system configs like Appender.* and Logger.*
    bool IsLoggingSystemOptions(StringView optionName)
    {
        size_t foundAppender = optionName.find("Appender.");
        size_t foundLogger = optionName.find("Logger.");

        return foundAppender != std::string_view::npos || foundLogger != std::string_view::npos;
    }

    template<typename Format, typename... Args>
    inline void PrintError(std::string_view filename, Format&& fmt, Args&& ... args)
    {
        std::string message = Avalon::StringFormatFmt(std::forward<Format>(fmt), std::forward<Args>(args)...);

        if (IsAppConfig(filename))
        {
            fmt::print("{}\n", message);
        }
        else
        {
            LOG_ERROR("server.loading", message);
        }
    }

    void AddKey(std::string const& optionName, std::string const& optionKey, std::string_view fileName, bool isOptional, [[maybe_unused]] bool isReload)
    {
        auto const& itr = _configOptions.find(optionName);

        // Check old option
        if (isOptional && itr == _configOptions.end())
        {
            if (!IsLoggingSystemOptions(optionName) && !isReload)
            {
                PrintError(fileName, "> Config::LoadFile: Found incorrect option '{}' in config file '{}'. Skip", optionName, fileName);

#ifdef CONFIG_ABORT_INCORRECT_OPTIONS
                ABORT("> Core can't start if found incorrect options");
#endif

                return;
            }
        }

        // Check exit option
        if (itr != _configOptions.end())
        {
            _configOptions.erase(optionName);
        }

        _configOptions.emplace(optionName, optionKey);
    }

    bool ParseFile(std::string const& file, bool isOptional, bool isReload)
    {
        std::ifstream in(file);

        if (in.fail())
        {
            if (isOptional)
            {
                // No display erorr if file optional
                return false;
            }

            throw ConfigException(Avalon::StringFormatFmt("Config::LoadFile: Failed open {}file '{}'", isOptional ? "optional " : "", file));
        }

        U32 count = 0;
        U32 lineNumber = 0;
        std::unordered_map<std::string /*name*/, std::string /*value*/> fileConfigs;

        auto IsDuplicateOption = [&](std::string const& confOption)
        {
            auto const& itr = fileConfigs.find(confOption);
            if (itr != fileConfigs.end())
            {
                PrintError(file, "> Config::LoadFile: Dublicate key name '{}' in config file '{}'", confOption, file);
                return true;
            }

            return false;
        };

        while (in.good())
        {
            lineNumber++;
            std::string line;
            std::getline(in, line);

            // read line error
            if (!in.good() && !in.eof())
            {
                throw ConfigException(Avalon::StringFormatFmt("> Config::LoadFile: Failure to read line number {} in file '{}'", lineNumber, file));
            }

            // remove whitespace in line
            line = Avalon::Trim(line, in.getloc());

            if (line.empty())
            {
                continue;
            }

            // comments
            if (line[0] == '#' || line[0] == '[')
            {
                continue;
            }

            size_t found = line.find_first_of('#');
            if (found != std::string::npos)
            {
                line = line.substr(0, found);
            }

            auto const equal_pos = line.find('=');

            if (equal_pos == std::string::npos || equal_pos == line.length())
            {
                PrintError(file, "> Config::LoadFile: Failure to read line number {} in file '{}'. Skip this line", lineNumber, file);
                continue;
            }

            auto entry = Avalon::Trim(line.substr(0, equal_pos), in.getloc());
            auto value = Avalon::Trim(line.substr(equal_pos + 1, std::string::npos), in.getloc());

            value.erase(std::remove(value.begin(), value.end(), '"'), value.end());

            // Skip if 2+ same options in one config file
            if (IsDuplicateOption(entry))
            {
                continue;
            }

            // Add to temp container
            fileConfigs.emplace(entry, value);
            count++;
        }

        // No lines read
        if (!count)
        {
            if (isOptional)
            {
                // No display erorr if file optional
                return false;
            }

            throw ConfigException(Avalon::StringFormatFmt("Config::LoadFile: Empty file '{}'", file));
        }

        // Add correct keys if file load without errors
        for (auto const& [entry, key] : fileConfigs)
        {
            AddKey(entry, key, file, isOptional, isReload);
        }

        return true;
    }

    bool LoadFile(std::string const& file, bool isOptional, bool isReload)
    {
        try
        {
            return ParseFile(file, isOptional, isReload);
        }
        catch (const std::exception& e)
        {
            PrintError(file, "> {}", e.what());
        }

        return false;
    }

    // Converts ini keys to the environment variable key (upper snake case).
    // Example of conversions:
    //   SomeConfig => SOME_CONFIG
    //   myNestedConfig.opt1 => MY_NESTED_CONFIG_OPT_1
    //   LogDB.Opt.ClearTime => LOG_DB_OPT_CLEAR_TIME
    std::string IniKeyToEnvVarKey(std::string& key) const
    {
        std::string result;

        const char* str = key.c_str();
        size_t n = key.length();

        char curr;
        bool isEnd;
        bool nextIsUpper;
        bool currIsNumeric;
        bool nextIsNumeric;

        for (size_t i = 0; i < n; ++i)
        {
            curr = str[i];
            if (curr == ' ' || curr == '.' || curr == '-')
            {
                result += '_';
                continue;
            }

            isEnd = i == n - 1;
            if (!isEnd)
            {
                nextIsUpper = isupper(str[i + 1]);

                // handle "aB" to "A_B"
                if (!isupper(curr) && nextIsUpper)
                {
                    result += static_cast<char>(std::toupper(curr));
                    result += '_';
                    continue;
                }

                currIsNumeric = isNumeric(curr);
                nextIsNumeric = isNumeric(str[i + 1]);

                // handle "a1" to "a_1"
                if (!currIsNumeric && nextIsNumeric)
                {
                    result += static_cast<char>(std::toupper(curr));
                    result += '_';
                    continue;
                }

                // handle "1a" to "1_a"
                if (currIsNumeric && !nextIsNumeric)
                {
                    result += static_cast<char>(std::toupper(curr));
                    result += '_';
                    continue;
                }
            }

            result += static_cast<char>(std::toupper(curr));
        }
        return result;
    }

    Optional<std::string> EnvVarForIniKey(std::string const& key) const {
        // std::string envKey = "AV_" + IniKeyToEnvVarKey(key); ORIGINAL
        std::string envKey = "AV_" + IniKeyToEnvVarKey(const_cast<std::string &>(key));
        char* val = std::getenv(envKey.c_str());
        if (!val)
            return std::nullopt;

        return std::string(val);
    }

};

#define sConfigMgr ConfigManager::Instance()
