#include <Windows.h>

#include <iostream>

FILETIME previousTimestamp = {};

bool IsGameLayerModified() {
    WIN32_FILE_ATTRIBUTE_DATA fileAttributes;
    if (GetFileAttributesEx("GameLayer.dll", GetFileExInfoStandard, &fileAttributes))
    {
        FILETIME currentTimestamp = fileAttributes.ftLastWriteTime;

        if (CompareFileTime(&currentTimestamp, &previousTimestamp) != 0)
        {
            previousTimestamp = currentTimestamp;
            return true; // DLL file has been modified
        }
    }
    else
    {
        // Handle error if unable to retrieve file attributes
        // Add appropriate error handling
        std::cout << "Error retrieving file attributes" << std::endl;
    }

    return false; // DLL file has not been modified
}
