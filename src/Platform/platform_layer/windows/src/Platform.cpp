//
// Created by nunol on 7/8/2023.
//

#include "Platform.h"
#include <iostream>
#include <Windows.h>

#include "GameLayer.h"

void Initialize() {
    // Initialize the platform layer
    std::cout << "Initializing the platform layer" << std::endl;

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

    // Call the game update function
    updateFunction();

    // Unload the game layer
    FreeLibrary(gameLayerModule);

    return;
}

