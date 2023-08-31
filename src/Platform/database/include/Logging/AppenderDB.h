#pragma once

#include <Common/Types.h>
#include <Logging/Appender.h>

class AppenderDB : public Appender
{
public:
    static constexpr AppenderType type = APPENDER_DB;

    AppenderDB(U8 id, std::string const& name, LogLevel level, AppenderFlags flags, std::vector<std::string_view> const& args);
    ~AppenderDB();

    void setRealmId(U32 realmId) override;
    AppenderType getType() const override { return type; }

private:
    U32 realmId;
    bool enabled;
    void _write(LogMessage const* message) override;
};
