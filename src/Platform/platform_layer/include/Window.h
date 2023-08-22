#pragma once

#include <Types.h>
#include <DLLLoader.h>

#include <memory>

class Window {
public:
    Window() = delete;
    Window(U16 height, U16 width, const String& title);

    void Destroy();

    bool IsRunning() const;

    virtual void Update();

    static std::unique_ptr<Window, void(*)(Window*)> Create(U16 height, U16 width, const String& title);

private:
    int height;
    int width;
    std::string title;

    DLLLoader dllLoader;

    bool running = false;

    GLFWwindow *handle;
    GLFWcursor *cursor;

    double previousTime = glfwGetTime();
    double accumulatedTime = 0;
    double currentTime = 0;

    double targetFPS = 60;
    double timePerFrame = 1.0 / targetFPS;

    int frameCount = 0;
    double fpsUpdateTime = 0.5;
    double fpsAccumulatedTime = 0.0;

};
