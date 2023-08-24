#include <Application.h>

#include <memory>
#include <iostream>

Application::Application(U16 width, U16 height, const String& title) : window(nullptr, nullptr) {
    this->height = height;
    this->width = width;
    this->title = title;

    glfwInit();

    glfwSetErrorCallback([](int error, const char* description) {
        std::cerr << "GLFW Error: " << description << std::endl;
    });

    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 2);

    glfwDefaultWindowHints();

    this->window = Window::Create(width, height, title);

    gl2d::init();
}

void Application::Destroy() {
    std::cout << "Destroying application" << std::endl;
    window->Destroy();
    glfwTerminate();
}

bool Application::Start() {

    while (window->IsRunning()) {
        window->EnsureLatestGameLayer();
        window->Update();
    }

    return true;
}

std::unique_ptr<Application, Application::Deleter> Application::Create(U16 width, U16 height, const String& title) {
    return {
            new Application(width, height, title),
            [](Application *app) {
                app->Destroy();
            }
    };
}


