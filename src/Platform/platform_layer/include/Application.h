#pragma once

#include <Types.h>
#include <Window.h>

#include <memory>
#include <string>


class Application {
public:
    Application() = delete;
    Application(U16 height, U16 width, const String& title);

    void Destroy();

    bool Start();

    void Update();

    bool IsRunning() const;

    static std::unique_ptr<Application, void(*)(Application*)> Create(U16 height, U16 width, const String& title);

private:
    U16 height;
    U16 width;
    String title;

    std::unique_ptr<Window, void(*)(Window*)> window;
};
