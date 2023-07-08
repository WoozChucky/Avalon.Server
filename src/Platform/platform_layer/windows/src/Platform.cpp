//
// Created by nunol on 7/8/2023.
//

#include "Platform.h"
#include <iostream>
#include <Windows.h>

#include "GameLayer.h"

// Global variable to store the previous timestamp
FILETIME previousTimestamp;

// Function to check if the game layer DLL file has been modified
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
    }

    return false; // DLL file has not been modified
}

void Initialize() {
    // Initialize the platform layer
    std::cout << "Initializing the platform layer" << std::endl;

    // Set the initial timestamp of the DLL file
    WIN32_FILE_ATTRIBUTE_DATA fileAttributes;
    if (GetFileAttributesEx("GameLayer.dll", GetFileExInfoStandard, &fileAttributes))
    {
        previousTimestamp = fileAttributes.ftLastWriteTime;
    }
    else
    {
        // Handle error if unable to retrieve file attributes
        // Add appropriate error handling
    }

    // Load the game layer dynamically
    HMODULE gameLayerModule = LoadLibrary("GameLayer.dll");
    if (!gameLayerModule)
    {
        // Handle the error, failed to load the game layer
        return;
    }

    // Get the function pointer to the UpdateGame function
    GameUpdateFunction updateFunction = reinterpret_cast<GameUpdateFunction>(GetProcAddress(gameLayerModule, "UpdateGame"));
    if (!updateFunction)
    {
        // Handle the error, failed to get the function pointer
        return;
    }

    // Main loop
    while (true)
    {
        if (IsGameLayerModified())
        {
            // DLL file has been modified, trigger reloading process
            // Unload the existing DLL and reload the updated DLL
            // Update function pointers and references in the platform layer

            // Unload the game layer
            FreeLibrary(gameLayerModule);

            gameLayerModule = LoadLibrary("GameLayer.dll");
            if (!gameLayerModule)
            {
                // Handle the error, failed to load the game layer
                return;
            }

            updateFunction = reinterpret_cast<GameUpdateFunction>(GetProcAddress(gameLayerModule, "UpdateGame"));
            if (!updateFunction)
            {
                // Handle the error, failed to get the function pointer
                return;
            }
        }

        // Other game loop logic

        // Call the game update function
        updateFunction();


    }

    // Unload the game layer
    FreeLibrary(gameLayerModule);

    return;
}

