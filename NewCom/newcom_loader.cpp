// newcom_loader.cpp — Manifest-based component loader implementation
#include "newcom_loader.h"
#include <fstream>
#include <sstream>
#include <iostream>
#include <filesystem>


static bool endsWith(const std::string& s, const std::string& suffix) {
    if (suffix.size() > s.size()) return false;
    return s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0;
}

namespace fs = std::filesystem;

// ─── Simple JSON field extractor ─────────────────────────────────────────────
static std::string extractJsonField(const std::string& json, const std::string& field) {
    std::string key = "\"" + field + "\"";
    size_t pos = json.find(key);
    if (pos == std::string::npos) return "";
    pos = json.find(':', pos);
    if (pos == std::string::npos) return "";
    pos++;
    while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\n' || json[pos] == '\r')) pos++;
    if (pos >= json.size()) return "";
    if (json[pos] == '"') {
        pos++;
        size_t end = json.find('"', pos);
        if (end == std::string::npos) return "";
        return json.substr(pos, end - pos);
    }
    // Numeric or boolean
    size_t end = json.find_first_of(",}\n", pos);
    if (end == std::string::npos) end = json.size();
    std::string val = json.substr(pos, end - pos);
    // trim
    while (!val.empty() && (val.back() == ' ' || val.back() == '\t' || val.back() == '\r')) val.pop_back();
    return val;
}

// ─── NewComLoader implementation ─────────────────────────────────────────────
NewComLoader::NewComLoader() {
    // Default search paths
    AddSearchPath(".");
    AddSearchPath("./components");
#ifdef _WIN32
    const char* env = std::getenv("NEWCOM_PATH");
#else
    const char* env = std::getenv("NEWCOM_PATH");
#endif
    if (env) AddSearchPath(env);
    const char* home = std::getenv("HOME");
    if (home) {
        std::string homePath = std::string(home) + "/.newcom";
        AddSearchPath(homePath);
    }
}

void NewComLoader::AddSearchPath(const std::string& path) {
    if (std::find(searchPaths.begin(), searchPaths.end(), path) == searchPaths.end())
        searchPaths.push_back(path);
}

bool NewComLoader::ParseManifest(const std::string& path, NewComManifest& out) {
    std::ifstream f(path);
    if (!f.is_open()) return false;
    std::stringstream ss;
    ss << f.rdbuf();
    std::string json = ss.str();

    out.name = extractJsonField(json, "name");
    out.clsid = extractJsonField(json, "clsid");
    out.library = extractJsonField(json, "library");
    out.factory = extractJsonField(json, "factory");

    return !out.name.empty() && !out.library.empty() && !out.factory.empty();
}

int NewComLoader::LoadFromDirectory(const std::string& dir) {
    int count = 0;
    if (!fs::exists(dir) || !fs::is_directory(dir)) return 0;

    for (const auto& entry : fs::directory_iterator(dir)) {
        if (!entry.is_regular_file()) continue;
        std::string filename = entry.path().filename().string();
        if (filename == "newcom.json" || filename.find("_newcom.json") != std::string::npos || endsWith(filename, ".newcom.json")) {
            NewComManifest manifest;
            if (ParseManifest(entry.path().string(), manifest)) {
                INewUnknown* comp = Load(manifest.name);
                if (comp) count++;
            }
        }
    }
    return count;
}

INewUnknown* NewComLoader::Load(const std::string& nameOrPath) {
    // Check if already loaded
    auto it = loaded.find(nameOrPath);
    if (it != loaded.end()) {
        it->second.factory();
        return nullptr; // already loaded, caller should use cached
    }

    // Try to find manifest
    NewComManifest manifest;
    std::string manifestPath;
    std::string libPath;

    // If nameOrPath is a file path to a manifest
    if (fs::exists(nameOrPath) && nameOrPath.find(".json") != std::string::npos) {
        if (!ParseManifest(nameOrPath, manifest)) return nullptr;
        manifestPath = nameOrPath;
        // Resolve library relative to manifest dir
        fs::path manifestDir = fs::path(nameOrPath).parent_path();
        libPath = (manifestDir / manifest.library).string();
    } else {
        // Search for manifest by component name
        for (const auto& sp : searchPaths) {
            // Try <searchpath>/<name>/newcom.json
            std::string candidate = sp + "/" + nameOrPath + "/newcom.json";
            if (fs::exists(candidate)) {
                if (ParseManifest(candidate, manifest) && manifest.name == nameOrPath) {
                    manifestPath = candidate;
                    libPath = sp + "/" + nameOrPath + "/" + manifest.library;
                    break;
                }
            }
            // Try <searchpath>/newcom.json with matching name
            candidate = sp + "/newcom.json";
            if (fs::exists(candidate)) {
                NewComManifest m;
                if (ParseManifest(candidate, m) && m.name == nameOrPath) {
                    manifest = m;
                    manifestPath = candidate;
                    fs::path dir = fs::path(candidate).parent_path();
                    libPath = (dir / m.library).string();
                    break;
                }
            }
        }
    }

    if (manifest.name.empty()) {
        errors.push_back("Cannot find manifest for: " + nameOrPath);
        return nullptr;
    }

    // Resolve library path
    if (!fs::exists(libPath)) {
        // Search all search paths
        for (const auto& sp : searchPaths) {
            std::string candidate = sp + "/" + manifest.library;
            if (fs::exists(candidate)) {
                libPath = candidate;
                break;
            }
        }
    }

    if (!fs::exists(libPath)) {
        errors.push_back("Cannot find library: " + manifest.library);
        return nullptr;
    }

    // Load the library
    LibHandle handle = LOAD_LIB(libPath.c_str());
    if (!handle) {
        errors.push_back("Failed to load library: " + libPath);
        return nullptr;
    }

    // Get factory function
    auto factory = reinterpret_cast<INewUnknown* (*)()>(GET_SYMBOL(handle, manifest.factory.c_str()));
    if (!factory) {
        errors.push_back("Cannot find factory function: " + manifest.factory + " in " + libPath);
        CLOSE_LIB(handle);
        return nullptr;
    }

    // Cache
    NewComComponent comp;
    comp.manifest = manifest;
    comp.libHandle = handle;
    comp.factory = factory;
    comp.resolvedPath = libPath;
    loaded[manifest.name] = comp;

    // Create instance
    return factory();
}

std::vector<std::string> NewComLoader::GetLoadedComponents() const {
    std::vector<std::string> names;
    for (const auto& kv : loaded) names.push_back(kv.first);
    return names;
}

void NewComLoader::UnloadAll() {
    for (auto& kv : loaded) {
        CLOSE_LIB(kv.second.libHandle);
    }
    loaded.clear();
}
