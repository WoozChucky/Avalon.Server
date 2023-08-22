#pragma once

#include <Types.h>

class DLLLoader {
public:
    DLLLoader() = delete;
    DLLLoader(const String& path);
    ~DLLLoader();

    bool Reload();

    void* GetFunction(const String& name);

private:
    String path;
    void* handle;
};
