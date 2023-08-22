#include <iostream>

#include <Application.h>

#if !defined(__FMA__) && defined(__AVX2__)
#define __FMA__ 1
#endif

#include <raudio.h>

int main(int argc, char** argv) {
    std::cout << "Hello, World!" << std::endl;

    auto app = Application::Create(600, 400, "Test");

    if (!app->Start()) {
        std::cerr << "Failed to start application" << std::endl;
        return -1;
    }

    return 0;
}
