#include "Banner.h"

#include "Versioning.h"

#include <Common/Utilities/StringFormat.h>

void Avalon::Banner::Show(std::string_view applicationName, void(*log)(std::string_view text), void(*logExtraInfo)())
{
    log(Avalon::StringFormatFmt("{}.{}.{}.Build({}) ({})", AVALON_VERSION_MAJOR, AVALON_VERSION_MINOR, AVALON_VERSION_PATCH, AVALON_VERSION_BUILD, applicationName));
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
    log(Avalon::StringFormatFmt("     Avalon Core {}.{}.{}  -  https://avalon.io\n", AVALON_VERSION_MAJOR, AVALON_VERSION_MINOR, AVALON_VERSION_PATCH));

    if (logExtraInfo)
    {
        logExtraInfo();
    }

    log(" ");
}
