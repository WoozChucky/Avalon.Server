//
// Created by nunol on 7/8/2023.
//

#include "Platform.h"
#include <iostream>
#include <Windows.h>
/*
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
        std::cout << "Error retrieving file attributes" << std::endl;
    }

    return false; // DLL file has not been modified
}

bool CopyFileToDir() {
    // Copy the newly generated DLL file to the hotreload directory

    // Get the path of the DLL file
    char dllPath[MAX_PATH];
    strcat_s(dllPath, "hotreload\\GameLayer.dll");

    // Copy the DLL file to the directory of the executable
    if (!CopyFileA("GameLayer.dll", dllPath, FALSE))
    {
        DWORD lastError = GetLastError();
        LPSTR errorBuffer = nullptr;

        FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                       nullptr, lastError, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), reinterpret_cast<LPSTR>(&errorBuffer), 0, nullptr);

        if (errorBuffer)
        {
            // Display the error message
            std::cout << "Error copying file: " << errorBuffer << std::endl;

            // Free the error message buffer
            LocalFree(errorBuffer);
        }
        else
        {
            // Unable to retrieve error message
            std::cout << "Error copying file. Code: " << lastError << std::endl;
        }
        return false;
    }

    return true;
}

struct GameLayer {
    HMODULE DLL;
    GameUpdateFunction UpdateFunction;
    bool IsValid;
};

GameLayer LoadGameLayer(char *dllName) {

    GameLayer gameLayer = {};

    gameLayer.UpdateFunction = nullptr;

    gameLayer.DLL = LoadLibrary(dllName);
    if (gameLayer.DLL)
    {
        gameLayer.UpdateFunction = reinterpret_cast<GameUpdateFunction>(GetProcAddress(gameLayer.DLL, "UpdateGame"));

        gameLayer.IsValid = gameLayer.UpdateFunction != nullptr;
    }

    if (!gameLayer.IsValid)
    {
        // Handle the error, failed to load the game layer
        return gameLayer;
    }

    return gameLayer;
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
    switch (uMsg)
    {
        case WM_CLOSE:
            DestroyWindow(hwnd);
            break;

        case WM_DESTROY:
            PostQuitMessage(0);
            break;

        default:
            return DefWindowProc(hwnd, uMsg, wParam, lParam);
    }

    return 0;
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_DESTROY) {
        PostQuitMessage(0);
        return 0;
    }
    else if (message == WM_CLOSE) {
        DestroyWindow(hWnd);
        return 0;
    }
    return DefWindowProc(hWnd, message, wParam, lParam);
}

void Initialize() {
    // Initialize the platform layer
    std::cout << "Initializing the platform layer" << std::endl;

    // Register window class
    WNDCLASS wc{};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = "Hot Reload";
    RegisterClass(&wc);

    // Calculate window size based on desired client area size
    RECT windowRect = { 0, 0, 800, 600 };
    AdjustWindowRect(&windowRect, WS_OVERLAPPEDWINDOW, FALSE);

    // Create window
    HWND m_hWnd = CreateWindow(wc.lpszClassName, "Hot Reload", WS_OVERLAPPEDWINDOW,
                          CW_USEDEFAULT, CW_USEDEFAULT, windowRect.right - windowRect.left,
                          windowRect.bottom - windowRect.top, nullptr, nullptr, GetModuleHandle(nullptr), nullptr);

    if (m_hWnd == nullptr) {
        return;
    }

    ShowWindow(m_hWnd, SW_SHOW);
    UpdateWindow(m_hWnd);

    MSG msg{};
    while (GetMessage(&msg, nullptr, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    GameLayer gameLayer = LoadGameLayer("hotreload/GameLayer.dll");
    if (!gameLayer.IsValid)
    {
        // Handle the error, failed to load the game layer
        std::cout << "Failed to load the game layer" << std::endl;
        return;
    }

    WIN32_FILE_ATTRIBUTE_DATA fileAttributes;
    if (GetFileAttributesEx("hotreload/GameLayer.dll", GetFileExInfoStandard, &fileAttributes))
    {
        previousTimestamp = fileAttributes.ftLastWriteTime;
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
            FreeLibrary(gameLayer.DLL);

            gameLayer.DLL = nullptr;
            gameLayer.UpdateFunction = nullptr;

            // Copy the newly generated DLL file to the hotreload directory
            CopyFileToDir();

            gameLayer = LoadGameLayer("hotreload/GameLayer.dll");

        }

        gameLayer.UpdateFunction();


    }

    // Unload the game layer
    FreeLibrary(gameLayer.DLL);

    return;
}
*/
