#include <Common/Logging/AppenderFile.h>
#include <Common/Logging/Log.h>
#include <Common/Logging/LogMessage.h>
#include <Common/Utilities/StringConvert.h>
#include <Common/Utilities/Timer.h>

#include <algorithm>

AppenderFile::AppenderFile(U8 id, std::string const& name, LogLevel level, AppenderFlags flags, std::vector<std::string_view> const& args) :
    Appender(id, name, level, flags),
    logfile(nullptr),
    _logDir(sLog->GetLogsDir()),
    _maxFileSize(0),
    _fileSize(0)
{
    if (args.size() < 4)
    {
        throw InvalidAppenderArgsException(Avalon::StringFormatFmt("Log::CreateAppenderFromConfig: Missing file name for appender {}", name));
    }

    _fileName.assign(args[3]);

    std::string mode = "a";
    if (4 < args.size())
    {
        mode.assign(args[4]);
    }

    if (flags & APPENDER_FLAGS_USE_TIMESTAMP)
    {
        size_t dot_pos = _fileName.find_last_of('.');
        if (dot_pos != std::string::npos)
        {
            _fileName.insert(dot_pos, sLog->GetLogsTimestamp());
        }
        else
        {
            _fileName += sLog->GetLogsTimestamp();
        }
    }

    if (5 < args.size())
    {
        if (Optional<U32> size = Avalon::StringTo<U32>(args[5]))
        {
            _maxFileSize = *size;
        }
        else
        {
            throw InvalidAppenderArgsException(Avalon::StringFormatFmt("Log::CreateAppenderFromConfig: Invalid size '{}' for appender {}", args[5], name));
        }
    }

    _dynamicName = std::string::npos != _fileName.find("%s");
    _backup = (flags & APPENDER_FLAGS_MAKE_FILE_BACKUP) != 0;

    if (!_dynamicName)
    {
        logfile = OpenFile(_fileName, mode, (mode == "w") && _backup);
    }
}

AppenderFile::~AppenderFile()
{
    CloseFile();
}

void AppenderFile::_write(LogMessage const* message)
{
    bool exceedMaxSize = _maxFileSize > 0 && (_fileSize.load() + message->Size()) > _maxFileSize;

    if (_dynamicName)
    {
        char namebuf[1024];
        snprintf(namebuf, 1024, _fileName.c_str(), message->param1.c_str());

        // always use "a" with dynamic name otherwise it could delete the log we wrote in last _write() call
        FILE* file = OpenFile(namebuf, "a", _backup || exceedMaxSize);
        if (!file)
        {
            return;
        }

        fprintf(file, "%s%s\n", message->prefix.c_str(), message->text.c_str());
        fflush(file);
        _fileSize += U64(message->Size());
        fclose(file);

        return;
    }
    else if (exceedMaxSize)
    {
        logfile = OpenFile(_fileName, "w", true);
    }

    if (!logfile)
    {
        return;
    }

    fprintf(logfile, "%s%s\n", message->prefix.c_str(), message->text.c_str());
    fflush(logfile);
    _fileSize += U64(message->Size());
}

FILE* AppenderFile::OpenFile(std::string const& filename, std::string const& mode, bool backup)
{
    std::string fullName(_logDir + filename);
    if (backup)
    {
        CloseFile();
        std::string newName(fullName);
        newName.push_back('.');
        newName.append(LogMessage::getTimeStr(GetEpochTime()));
        std::replace(newName.begin(), newName.end(), ':', '-');
        rename(fullName.c_str(), newName.c_str()); // no error handling... if we couldn't make a backup, just ignore
    }

    if (FILE* ret = fopen(fullName.c_str(), mode.c_str()))
    {
        _fileSize = ftell(ret);
        return ret;
    }

    return nullptr;
}

void AppenderFile::CloseFile()
{
    if (logfile)
    {
        fclose(logfile);
        logfile = nullptr;
    }
}
