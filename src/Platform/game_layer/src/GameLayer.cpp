//
// Created by nunol on 7/8/2023.
//

#include "GameLayer.h"

#include <iostream>

extern "C" __declspec(dllexport) void UpdateGame()
{
    // Your game update logic
    std::cout << "Updating the game" << std::endl;
}
