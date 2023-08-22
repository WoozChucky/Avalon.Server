#include <DLLLoader.h>

#include <Windows.h>

DLLLoader::DLLLoader(const String& path) {
    this->path = path;
    String fullPath = path + ".dll";
    this->handle = LoadLibraryA(fullPath.c_str());
    if (!handle) {
        throw std::runtime_error("Failed to load DLL: " + fullPath);
    }
}

DLLLoader::~DLLLoader() {
    if (this->handle) {
        FreeLibrary((HMODULE)this->handle);
    }
}

bool DLLLoader::Reload() {

    if (this->handle) {
        FreeLibrary((HMODULE)this->handle);
    }

    String fullPath = path + ".dll";
    this->handle = LoadLibraryA(fullPath.c_str());

    if (!handle) {
        throw std::runtime_error("Failed to reload DLL: " + fullPath);
    }

    return true;
}

void *DLLLoader::GetFunction(const String &name) {
    if (!this->handle) {
        return nullptr;
    }

    return GetProcAddress((HMODULE)this->handle, name.c_str());
}
