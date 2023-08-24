#include <Logging/LogOperation.h>
#include <Logging/LogMessage.h>
#include <Logging/Logger.h>

LogOperation::LogOperation(Logger const* _logger, std::unique_ptr<LogMessage>&& _msg) : logger(_logger), msg(std::forward<std::unique_ptr<LogMessage>>(_msg))
{
}

LogOperation::~LogOperation()
{
}

int LogOperation::call()
{
    logger->write(msg.get());
    return 0;
}
