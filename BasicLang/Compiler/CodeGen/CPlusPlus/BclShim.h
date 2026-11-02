// BclShim.h — C++ shim implementations of common .NET BCL APIs
// Used by the IL2CPP backend to provide native implementations of .NET methods
// that are called from IL-compiled code.
//
// This is the "Option A" (incremental shim) approach: provide C++ implementations
// for BCL APIs as programs need them, rather than compiling the entire .NET BCL from IL.
//
// Phase 4 (full BCL compilation) will eventually compile mscorlib/System.Runtime from IL,
// but this shim provides the immediate runtime support needed for VGS programs.

#ifndef BCL_SHIM_H
#define BCL_SHIM_H

#include <iostream>
#include <string>
#include <vector>
#include <algorithm>
#include <cmath>
#include <chrono>
#include <thread>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <ctime>

namespace BclShim {

// === Console ===
inline void WriteLine(const std::string& s) { std::cout << s << std::endl; }
inline void Write(const std::string& s) { std::cout << s; }
inline std::string ReadLine() {
    std::string s;
    std::getline(std::cin, s);
    return s;
}

// === Math ===
inline double Sqrt(double x) { return std::sqrt(x); }
inline double Abs(double x) { return std::fabs(x); }
inline int Abs(int x) { return std::abs(x); }
inline long Abs(long x) { return std::llabs(x); }
inline double Floor(double x) { return std::floor(x); }
inline double Ceiling(double x) { return std::ceil(x); }
inline double Pow(double x, double y) { return std::pow(x, y); }
inline double Min(double a, double b) { return std::min(a, b); }
inline int Min(int a, int b) { return std::min(a, b); }
inline double Max(double a, double b) { return std::max(a, b); }
inline int Max(int a, int b) { return std::max(a, b); }
inline double Round(double x) { return std::round(x); }

// === String helpers ===
inline size_t StringLength(const std::string& s) { return s.length(); }
inline std::string Substring(const std::string& s, size_t start, size_t len) {
    return s.substr(start, len);
}
inline std::string Substring(const std::string& s, size_t start) {
    return s.substr(start);
}
inline bool Contains(const std::string& s, const std::string& search) {
    return s.find(search) != std::string::npos;
}
inline std::string ToUpper(const std::string& s) {
    std::string r = s;
    std::transform(r.begin(), r.end(), r.begin(), ::toupper);
    return r;
}
inline std::string ToLower(const std::string& s) {
    std::string r = s;
    std::transform(r.begin(), r.end(), r.begin(), ::tolower);
    return r;
}

// === Convert ===
inline int ToInt32(const std::string& s) { return std::stoi(s); }
inline int ToInt32(double x) { return static_cast<int>(x); }
inline double ToDouble(const std::string& s) { return std::stod(s); }
inline double ToDouble(int x) { return static_cast<double>(x); }
inline long ToInt64(const std::string& s) { return std::stoll(s); }
inline float ToSingle(const std::string& s) { return std::stof(s); }
inline bool ToBoolean(const std::string& s) { return s == "true" || s == "True" || s == "1"; }

// === Environment ===
inline std::string GetEnvironmentVariable(const std::string& name) {
    const char* val = std::getenv(name.c_str());
    return val ? std::string(val) : "";
}
inline const char* NewLine() { return "\n"; }

// === Array helpers ===
template<typename T>
inline void Sort(std::vector<T>& v) { std::sort(v.begin(), v.end()); }
template<typename T>
inline void Reverse(std::vector<T>& v) { std::reverse(v.begin(), v.end()); }

// === StringBuilder helpers ===
// (StringBuilder is just std::string in VGS C++ backend)
inline void Append(std::string& sb, const std::string& s) { sb += s; }
inline void Append(std::string& sb, int x) { sb += std::to_string(x); }
inline void Append(std::string& sb, double x) { sb += std::to_string(x); }

// === File I/O ===
inline std::string ReadAllText(const std::string& path) {
    std::ifstream f(path);
    std::stringstream ss;
    ss << f.rdbuf();
    return ss.str();
}
inline void WriteAllText(const std::string& path, const std::string& content) {
    std::ofstream f(path);
    f << content;
}
inline bool FileExists(const std::string& path) {
    std::ifstream f(path);
    return f.good();
}

// === Threading ===
inline void Sleep(int ms) {
    std::this_thread::sleep_for(std::chrono::milliseconds(ms));
}

// === DateTime helpers (basic) ===
inline long DateTimeNow() {
    auto now = std::chrono::system_clock::now();
    auto epoch = now.time_since_epoch();
    return std::chrono::duration_cast<std::chrono::milliseconds>(epoch).count();
}

} // namespace BclShim

#endif // BCL_SHIM_H