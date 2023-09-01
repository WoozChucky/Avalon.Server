#include <Logging/AppenderDB.h>

#include <Common/Logging/LogMessage.h>
#include <Database/DatabaseEnv.h>
#include <Database/PreparedStatement.h>

AppenderDB::AppenderDB(U8 id, std::string const& name, LogLevel level, AppenderFlags /*flags*/, std::vector<std::string_view> const& /*args*/)
    : Appender(id, name, level), realmId(0), enabled(false) { }

AppenderDB::~AppenderDB() { }

void AppenderDB::_write(LogMessage const* message)
{
    // Avoid infinite loop, Execute triggers Logging with "sql.sql" type
    if (!enabled || (message->type.find("sql") != std::string::npos))
        return;

    LoginDatabasePreparedStatement* stmt = LoginDatabase.GetPreparedStatement(LOGIN_INS_LOG);
    stmt->SetData(0, message->mtime.count());
    stmt->SetData(1, realmId);
    stmt->SetData(2, message->type);
    stmt->SetData(3, U8(message->level));
    stmt->SetData(4, message->text);
    LoginDatabase.Execute(stmt);
}

void AppenderDB::setRealmId(U32 _realmId)
{
    enabled = true;
    realmId = _realmId;
}
