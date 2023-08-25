#include "MySQLThreading.h"

#include "MySQLWorkaround.h"

void MySQL::Library_Init()
{
    mysql_library_init(-1, nullptr, nullptr);
}

void MySQL::Library_End()
{
    mysql_library_end();
}

U32 MySQL::GetLibraryVersion()
{
    return MYSQL_VERSION_ID;
}
