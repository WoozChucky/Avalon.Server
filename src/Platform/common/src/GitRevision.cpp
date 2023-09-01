#include <Common/GitRevision.h>

#include "revision.h"

char const* GitRevision::GetHash()
{
    return _HASH;
}

char const* GitRevision::GetDate()
{
    return _DATE;
}

char const* GitRevision::GetBranch()
{
    return _BRANCH;
}

char const* GitRevision::GetCMakeCommand()
{
    return _CMAKE_COMMAND;
}

char const* GitRevision::GetCMakeVersion()
{
    return _CMAKE_VERSION;
}

char const* GitRevision::GetHostOSVersion()
{
    return _CMAKE_HOST_SYSTEM;
}

char const* GitRevision::GetBuildDirectory()
{
    return _BUILD_DIRECTORY;
}

char const* GitRevision::GetSourceDirectory()
{
    return _SOURCE_DIRECTORY;
}

char const* GitRevision::GetMySQLExecutable()
{
    return _MYSQL_EXECUTABLE;
}

#if AV_PLATFORM_WIN
#  ifdef _WIN64
#    define AVALON_PLATFORM_STR "Win64"
#  else
#    define AVALON_PLATFORM_STR "Win32"
#  endif
#else // AV_PLATFORM_WIN
#  define AVALON_PLATFORM_STR "Unix"
#endif

#ifndef AVALON_API_USE_DYNAMIC_LINKING
#  define AVALON_LINKAGE_TYPE_STR "Static"
#else
#  define AVALON_LINKAGE_TYPE_STR "Dynamic"
#endif

char const* GitRevision::GetFullVersion()
{
    return VER_COMPANYNAME_STR " rev. " VER_PRODUCTVERSION_STR " (" AVALON_PLATFORM_STR ", " _BUILD_DIRECTIVE ", " AVALON_LINKAGE_TYPE_STR ")";
}

char const* GitRevision::GetCompanyNameStr()
{
    return VER_COMPANYNAME_STR;
}

char const* GitRevision::GetLegalCopyrightStr()
{
    return VER_LEGALCOPYRIGHT_STR;
}

char const* GitRevision::GetFileVersionStr()
{
    return VER_FILEVERSION_STR;
}

char const* GitRevision::GetProductVersionStr()
{
    return VER_PRODUCTVERSION_STR;
}
