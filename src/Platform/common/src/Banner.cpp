#include <Common/Banner.h>

#include <Common/GitRevision.h>
#include <Common/Utilities/StringFormat.h>

void Avalon::Banner::Show(std::string_view applicationName, void(*log)(std::string_view text), void(*logExtraInfo)())
{
    log(Avalon::StringFormatFmt("{} ({})", GitRevision::GetFullVersion(), applicationName));
    log("<Ctrl-C> to stop.\n");
    log("    ___                    __               \n"
        "   /   | _   __  ____ _   / /  ____    ____ \n"
        "  / /| || | / / / __ `/  / /  / __ \\  / __ \\\n"
        " / ___ || |/ / / /_/ /  / /  / /_/ / / / / /\n"
        "/_/__|_||___/  \\__,_/  /_/   \\____/ /_/ /_/ \n"
        "  / ___/  ___    _____ _   __  ___    _____ \n"
        "  \\__ \\  / _ \\  / ___/| | / / / _ \\  / ___/ \n"
        " ___/ / /  __/ / /    | |/ / /  __/ / /     \n"
        "/____/  \\___/ /_/     |___/  \\___/ /_/      \n"
        "                                           \n");
    log(Avalon::StringFormatFmt("     Avalon Core | {}  -  https://avalon.io\n", GitRevision::GetFileVersionStr()));

    if (logExtraInfo)
    {
        logExtraInfo();
    }

    log(" ");
}
