#pragma once

#include <Common/Types.h>
#include <string>

/// Provides helper functions to access built-in values
/// which can be overwritten in config
namespace BuiltInConfig
{
    /// Returns the CMake command when any is specified in the config,
    /// returns the built-in path otherwise
    std::string GetCMakeCommand();

    /// Returns the build directory path when any is specified in the config,
    /// returns the built-in one otherwise
    std::string GetBuildDirectory();

    /// Returns the source directory path when any is specified in the config,
    /// returns the built-in one otherwise
    std::string GetSourceDirectory();

    /// Returns the path to the mysql executable (`mysql`) when any is specified
    /// in the config, returns the built-in one otherwise
    std::string GetMySQLExecutable();

} // namespace BuiltInConfig
