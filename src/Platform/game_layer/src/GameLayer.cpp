//
// Created by nunol on 7/8/2023.
//

#include "GameLayer.h"

#include <iostream>

#if defined(_MSC_VER)
#define GAME_API __declspec(dllexport) // Microsoft
#elif defined(__GNUC__)
#define GAME_API __attribute__((visibility("default"))) // GCC
#else
#define GAME_API // Most compilers export all the symbols by default. We hope for the best here.
    #pragma warning Unknown dynamic link import/export semantics.
#endif

extern "C" GAME_API void UpdateGame()
{
    // Your game update logic
    std::cout << "Updating the game" << std::endl;
}
