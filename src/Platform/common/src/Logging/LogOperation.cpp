#include <Common/Logging/LogOperation.h>

#include <Common/Logging/LogMessage.h>
#include <Common/Logging/Logger.h>

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
