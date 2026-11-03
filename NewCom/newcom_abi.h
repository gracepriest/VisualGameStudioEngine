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

// Forward declarations
struct INewComControl;
struct INewComHost;

// ─── INewComControl (optional control contract) ──────────────────────────────
// A NewCOM component may additionally implement this interface to be hostable
// as a UI control (ActiveX-style). The host (NewComControlHost) queries for it
// via QueryInterface with IID_INewComControl; if unsupported, the component is
// treated as a plain (non-visual) component.
//
// Vtable layout: INewUnknown base + control methods.
struct INewComControl;

struct INewComControlVtbl {
    // INewUnknown base
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
    // Control contract
    void*    (*GetHwnd)(INewComControl* self);
    void     (*SetParent)(INewComControl* self, void* parentHwnd);
    void     (*SetBounds)(INewComControl* self, int32_t x, int32_t y, int32_t w, int32_t h);
    void     (*SetVisible)(INewComControl* self, int32_t visible);
    void     (*SetHost)(INewComControl* self, INewComHost* host);
    void     (*Paint)(INewComControl* self, void* hdc);
};

struct INewComControl {
    const INewComControlVtbl* vtbl;
};

// IID for INewComControl
// {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
NEWCOM_DEFINE_GUID(IID_INewComControl,
    0xA1B2C3D4, 0xE5F6, 0x7890,
    0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x90);

// ─── INewComHost (callback interface for events) ────────────────────────────
// The host (NewComControlHost) implements this interface and passes it to the
// component via INewComControl::SetHost. The component calls OnEvent to fire
// events back to the host.
struct INewComHost;

struct INewComHostVtbl {
    void (*OnEvent)(INewComHost* self, const char* eventName, const char* argsJson);
};

struct INewComHost {
    const INewComHostVtbl* vtbl;
};

// ─── INewComControl additions for events ─────────────────────────────────────
// (SetHost added to INewComControlVtbl above — see below)

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
