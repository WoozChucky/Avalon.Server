#pragma once

#include <memory>

class Logger;
struct LogMessage;

class LogOperation
{
public:
    LogOperation(Logger const* _logger, std::unique_ptr<LogMessage>&& _msg);

    ~LogOperation();

    int call();

protected:
    Logger const* logger;
    std::unique_ptr<LogMessage> msg;
};
