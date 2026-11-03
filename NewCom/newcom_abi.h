// newcom_abi.h — Universal NewCOM ABI
// Cross-platform, language-agnostic component model.
// No Windows dependency. No registry. No apartments.
//
// Include this header in every NewCOM component and every language adapter.
//
// Usage:
//   - Components implement INewUnknown (and derived interfaces)
//   - Consumers call CreateXxx() factory, then use vtable methods
//   - Reference counting via AddRef/Release
//   - Interface discovery via QueryInterface

#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>

// ─── Export macro ────────────────────────────────────────────────────────────
#ifdef _WIN32
#define NEWCOM_EXPORT __declspec(dllexport)
#else
#define NEWCOM_EXPORT __attribute__((visibility("default")))
#endif

// ─── GUID ────────────────────────────────────────────────────────────────────
struct NewComGUID {
    uint32_t data1;
    uint16_t data2;
    uint16_t data3;
    uint8_t  data4[8];
};

inline bool operator==(const NewComGUID& a, const NewComGUID& b) {
    return a.data1 == b.data1 && a.data2 == b.data2 && a.data3 == b.data3 &&
           memcmp(a.data4, b.data4, 8) == 0;
}
inline bool operator!=(const NewComGUID& a, const NewComGUID& b) { return !(a == b); }

// ─── HRESULT-style error codes ───────────────────────────────────────────────
constexpr int32_t NEWCOM_OK          = 0;
constexpr int32_t NEWCOM_E_NOINTERFACE = -1;
constexpr int32_t NEWCOM_E_FAIL      = -2;
constexpr int32_t NEWCOM_E_OUTOFMEMORY = -3;
constexpr int32_t NEWCOM_E_INVALIDARG = -4;

// ─── INewUnknown (base interface) ────────────────────────────────────────────
struct INewUnknown;

struct INewUnknownVtbl {
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
};

struct INewUnknown {
    const INewUnknownVtbl* vtbl;
};

// ─── Helper macros ───────────────────────────────────────────────────────────
#define NEWCOM_DEFINE_GUID(name, d1, d2, d3, d4a, d4b, d4c, d4d, d4e, d4f, d4g, d4h) \
    static const NewComGUID name = { d1, d2, d3, { d4a, d4b, d4c, d4d, d4e, d4f, d4g, d4h } };

// ─── Component manifest (JSON) ───────────────────────────────────────────────
// Components are discovered via manifest files:
//   {
//     "name": "Greeter",
//     "clsid": "12345678-1234-1234-1234-123456789abc",
//     "library": "libgreeter_newcom.so",   // or greeter_newcom.dll
//     "factory": "CreateGreeter"
//   }
//
// Search paths: ./components/, ~/.newcom/, $NEWCOM_PATH/
