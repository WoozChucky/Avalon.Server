#include "CliRunnable.h"
#include "../Game/World/IWorld.h"
#include <Common/Configuration/ConfigManager.h>
#include <Common/Debugging/Errors.h>
// #include "ObjectMgr.h"
#include "../Game/World/World.h"
#include <fmt/core.h>


#if AV_PLATFORM_UNIX
//#include "Chat.h"
//#include "ChatCommand.h"
#include <cstring>
#include <readline/history.h>
#include <readline/readline.h>
#else
#include <Windows.h>
#endif

static constexpr char CLI_PREFIX[] = "AV> ";

static inline void PrintCliPrefix()
{
    fmt::print(CLI_PREFIX);
}

#if AV_PLATFORM != AV_PLATFORM_WIN
namespace Avalon::Impl::Readline
{
    static std::vector<std::string> vec;
    char* cli_unpack_vector(char const*, int state)
    {
        static size_t i=0;
        if (!state)
            i = 0;
        if (i < vec.size())
            return strdup(vec[i++].c_str());
        else
            return nullptr;
    }

    char** cli_completion(char const* text, int /*start*/, int /*end*/)
    {
        ::rl_attempted_completion_over = 1;
        vec = Acore::ChatCommands::GetAutoCompletionsFor(CliHandler(nullptr,nullptr), text);
        return ::rl_completion_matches(text, &cli_unpack_vector);
    }

    int cli_hook_func()
    {
           if (World::IsStopped())
               ::rl_done = 1;
           return 0;
    }
}
#endif

void utf8print(void* /*arg*/, std::string_view str)
{
#if AV_PLATFORM == AV_PLATFORM_WIN
    fmt::print(str);
#else
{
    fmt::print(str);
    fflush(stdout);
}
#endif
}

void commandFinished(void*, bool /*success*/)
{
    PrintCliPrefix();
    fflush(stdout);
}

#ifdef linux
// Non-blocking keypress detector, when return pressed, return 1, else always return 0
int kb_hit_return()
{
    struct timeval tv;
    fd_set fds;
    tv.tv_sec = 0;
    tv.tv_usec = 0;
    FD_ZERO(&fds);
    FD_SET(STDIN_FILENO, &fds);
    select(STDIN_FILENO+1, &fds, nullptr, nullptr, &tv);
    return FD_ISSET(STDIN_FILENO, &fds);
}
#endif

/// %Thread start
void CliThread()
{
#if AV_PLATFORM == AV_PLATFORM_WIN
    // print this here the first time
    // later it will be printed after command queue updates
    PrintCliPrefix();
#else
    ::rl_attempted_completion_function = &Acore::Impl::Readline::cli_completion;
    {
        static char BLANK = '\0';
        ::rl_completer_word_break_characters = &BLANK;
    }
    ::rl_event_hook = &Acore::Impl::Readline::cli_hook_func;
#endif

    if (sConfigMgr->GetOption<bool>("BeepAtStart", true))
        printf("\a"); // \a = Alert

#if AV_PLATFORM_WIN
    if (sConfigMgr->GetOption<bool>("FlashAtStart", true))
    {
        FLASHWINFO fInfo;
        fInfo.cbSize = sizeof(FLASHWINFO);
        fInfo.dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG;
        fInfo.hwnd = GetConsoleWindow();
        fInfo.uCount = 0;
        fInfo.dwTimeout = 0;
        FlashWindowEx(&fInfo);
    }
#endif

    ///- As long as the World is running (no World::m_stopEvent), get the command line and handle it
    while (!World::IsStopped())
    {
        fflush(stdout);

        std::string command;

#if AV_PLATFORM_WIN
        wchar_t commandbuf[256];
        if (fgetws(commandbuf, sizeof(commandbuf), stdin))
        {
            if (!WStrToUtf8(commandbuf, wcslen(commandbuf), command))
            {
                PrintCliPrefix();
                continue;
            }
        }
#else
        char* command_str = readline(CLI_PREFIX);
        ::rl_bind_key('\t', ::rl_complete);
        if (command_str != nullptr)
        {
            command = command_str;
            free(command_str);
        }
#endif

        if (!command.empty())
        {
            std::size_t nextLineIndex = command.find_first_of("\r\n");
            if (nextLineIndex != std::string::npos)
            {
                if (nextLineIndex == 0)
                {
#if AV_PLATFORM_WIN
                    PrintCliPrefix();
#endif
                    continue;
                }

                command.erase(nextLineIndex);
            }

            fflush(stdout);
            sWorld->QueueCliCommand(new CliCommandHolder(nullptr, command.c_str(), &utf8print, &commandFinished));
#if AV_PLATFORM_UNIX
            add_history(command.c_str());
#endif
        }
        else if (feof(stdin))
        {
            World::StopNow(SHUTDOWN_EXIT_CODE);
        }
    }
}
