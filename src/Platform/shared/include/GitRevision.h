#pragma once

#include <Common/Types.h>

namespace GitRevision
{
    char const* GetHash();
    char const* GetDate();
    char const* GetBranch();
    char const* GetCMakeCommand();
    char const* GetCMakeVersion();
    char const* GetHostOSVersion();
    char const* GetBuildDirectory();
    char const* GetSourceDirectory();
    char const* GetMySQLExecutable();
    char const* GetFullVersion();
    char const* GetCompanyNameStr();
    char const* GetLegalCopyrightStr();
    char const* GetFileVersionStr();
    char const* GetProductVersionStr();
}
