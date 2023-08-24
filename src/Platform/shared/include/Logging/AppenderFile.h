#pragma once

#include "Appender.h"
#include <atomic>

class AppenderFile : public Appender
{
public:
    static constexpr AppenderType type = APPENDER_FILE;

    AppenderFile(U8 id, std::string const& name, LogLevel level, AppenderFlags flags, std::vector<std::string_view> const& args);
    ~AppenderFile();
    FILE* OpenFile(std::string const& name, std::string const& mode, bool backup);
    AppenderType getType() const override { return type; }

private:
    void CloseFile();
    void _write(LogMessage const* message) override;
    FILE* logfile;
    std::string _fileName;
    std::string _logDir;
    bool _dynamicName;
    bool _backup;
    U64 _maxFileSize;
    std::atomic<U64> _fileSize;
};
