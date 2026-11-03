// newcom_loader.h — Manifest-based component loader
// Cross-platform: uses LoadLibrary on Windows, dlopen on Linux/macOS
#pragma once
#include "newcom_abi.h"
#include <string>
#include <vector>
#include <map>

#ifdef _WIN32
#include <windows.h>
typedef HMODULE LibHandle;
#define LOAD_LIB(path) LoadLibraryA(path)
#define GET_SYMBOL(handle, name) GetProcAddress(handle, name)
#define CLOSE_LIB(handle) FreeLibrary(handle)
#else
#include <dlfcn.h>
typedef void* LibHandle;
#define LOAD_LIB(path) dlopen(path, RTLD_NOW | RTLD_LOCAL)
#define GET_SYMBOL(handle, name) dlsym(handle, name)
#define CLOSE_LIB(handle) dlclose(handle)
#endif

// ─── Manifest ────────────────────────────────────────────────────────────────
struct NewComManifest {
    std::string name;       // Component name (e.g., "Greeter")
    std::string clsid;      // CLSID (GUID string)
    std::string library;    // Library filename (e.g., "greeter_newcom.dll")
    std::string factory;    // Factory function name (e.g., "Greeter_Create")
};

// ─── Loaded component ─────────────────────────────────────────────────────────
struct NewComComponent {
    NewComManifest manifest;
    LibHandle libHandle;
    INewUnknown* (*factory)();
    std::string resolvedPath;
};

// ─── Loader ──────────────────────────────────────────────────────────────────
class NewComLoader {
    std::vector<std::string> searchPaths;
    std::map<std::string, NewComComponent> loaded;
    std::vector<std::string> errors;

public:
    NewComLoader();

    // Add a directory to the search path
    void AddSearchPath(const std::string& path);

    // Load a component by name or manifest path
    // Returns the factory function pointer, or nullptr on failure
    INewUnknown* Load(const std::string& nameOrPath);

    // Load all components from a directory containing newcomer.json files
    int LoadFromDirectory(const std::string& dir);

    // Parse a manifest file
    static bool ParseManifest(const std::string& path, NewComManifest& out);

    // Get list of loaded component names
    std::vector<std::string> GetLoadedComponents() const;

    // Get errors
    const std::vector<std::string>& GetErrors() const { return errors; }

    // Unload all components
    void UnloadAll();
};
