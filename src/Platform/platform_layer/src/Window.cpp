#include <Window.h>

#include <GameLayer.h>

#include <memory>

#include <iostream>

Window::Window(const U16 width, const U16 height, const String& title) : dllLoader("GameLayer") {
    this->height = height;
    this->width = width;
    this->title = title;
    this->cursor = glfwCreateStandardCursor(GLFW_ARROW_CURSOR);
    this->gameLayer = nullptr;

    handle = glfwCreateWindow(width, height, title.c_str(), nullptr, nullptr);
    glfwMakeContextCurrent(handle);

    gladLoadGLLoader((GLADloadproc) glfwGetProcAddress);

    glfwSetKeyCallback(handle, [](GLFWwindow *window, int key, int scancode, int action, int mods) {
        if (key == GLFW_KEY_ESCAPE && action == GLFW_PRESS) {
            glfwSetWindowShouldClose(window, GLFW_TRUE);
        } else {
            //std::cout << "Key: " << key << std::endl;
            //std::cout << "Scancode: " << scancode << std::endl;
            //std::cout << "Action: " << action << std::endl;
            //std::cout << "Mods: " << mods << std::endl;
        }
    });

    glfwSwapInterval(1);

    this->gameLayer = new GameLayer {
            reinterpret_cast<GameUpdateFunction>(dllLoader.GetFunction("Update")),
            reinterpret_cast<GameRenderFunction>(dllLoader.GetFunction("Render")),
            glfwGetTime()
    };
}

std::unique_ptr<Window, void(*)(Window*)> Window::Create(U16 width, U16 height, const String& title) {
    return {
            new Window(width, height, title),
            [](Window *window) {
                window->Destroy();
            }
    };
}

void Window::Destroy() {
    std::cout << "Destroying window" << std::endl;
    glfwDestroyWindow(handle);
    glfwDestroyCursor(cursor);
}

void Window::Update() {
    glfwGetFramebufferSize(handle, &width, &height);

    glViewport(0, 0, width, height);

    currentTime = glfwGetTime();
    auto deltaTime = currentTime - previousTime;
    previousTime = currentTime;

    accumulatedTime += deltaTime;
    fpsAccumulatedTime += deltaTime;

    // Process input events
    glfwPollEvents();

    while (accumulatedTime >= timePerFrame) {
        // Update game state

        this->gameLayer->update(deltaTime);

        accumulatedTime -= timePerFrame;
    }

    // Clear the screen
    glClear(GL_COLOR_BUFFER_BIT);

    // Render game state

    this->gameLayer->render();

    // Swap front and back buffers
    glfwSwapBuffers(handle);

    // Update FPS counter
    frameCount++;
    if (fpsAccumulatedTime >= fpsUpdateTime) {
        double fps = static_cast<double>(frameCount) / fpsAccumulatedTime;
        String x = "Avalon FPS: " + std::to_string(fps);
        glfwSetWindowTitle(handle, x.c_str());

        frameCount = 0;
        fpsAccumulatedTime = 0.0;
    }
}

bool Window::IsRunning() const {
    return glfwWindowShouldClose(handle) == 0;
}

void Window::EnsureLatestGameLayer() {


    if (this->gameLayer == nullptr) {
        throw std::runtime_error("GameLayer is null");
    }

    auto lastLoadTime = this->gameLayer->loadTime;

    if (IsGameLayerModified()) {
        std::cout << "Reloading GameLayer" << std::endl;

        this->gameLayer = nullptr;

        // CopyFileToDir();

        dllLoader.Reload();
        this->gameLayer = new GameLayer {
                reinterpret_cast<GameUpdateFunction>(dllLoader.GetFunction("Update")),
                reinterpret_cast<GameRenderFunction>(dllLoader.GetFunction("Render")),
                glfwGetTime()
        };
    }

}
