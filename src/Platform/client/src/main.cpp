#include <iostream>

#include <Application.h>

#if !defined(__FMA__) && defined(__AVX2__)
#define __FMA__ 1
#endif

#include <raudio.h>
#include <csignal>

static std::unique_ptr<Application, Application::Deleter> app = {nullptr, nullptr};

int main(int argc, char** argv) {

    app = Application::Create(600, 400, "Test");

    signal(SIGABRT, [](int) {
        std::cout << "Caught SIGABRT" << std::endl;
        app->Destroy();
    });
    signal(SIGINT, [](int) {
        std::cout << "Caught SIGINT" << std::endl;
        app->Destroy();
    });

    std::cout << "Hello, World!" << std::endl;

    if (!app->Start()) {
        std::cerr << "Failed to start application" << std::endl;
        return -1;
    }

    std::cout << "Exiting normally" << std::endl;

    return 0;
}
