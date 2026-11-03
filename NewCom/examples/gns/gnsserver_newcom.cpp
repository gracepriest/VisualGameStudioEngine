// gnsserver_newcom.cpp — NewCOM implementation
#include "gnsserver_newcom.h"
#include <cstring>
#include <cstdlib>
#include <atomic>

// ─── Implementation struct ────────────────────────────────────────────────────
struct GNSServerImpl {
    const IGNSServerVtbl* vtbl;
    std::atomic<uint32_t> refCount;

    GNSServerImpl() : refCount(1), vtbl(nullptr) {}
};

// ─── Vtable function implementations ─────────────────────────────────────────
static int32_t GNSServer_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<GNSServerImpl*>(self)->refCount++;
    return NEWCOM_OK;
}

static uint32_t GNSServer_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<GNSServerImpl*>(self)->refCount;
}

static uint32_t GNSServer_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<GNSServerImpl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int32_t GNSServer_Start(IGNSServer* self, const char* addr) {
    // TODO: implement Start
    return 0;
}

static int32_t GNSServer_Accept(IGNSServer* self, void** outConn) {
    // TODO: implement Accept
    return 0;
}

static void GNSServer_Stop(IGNSServer* self) {
    // TODO: implement Stop
}

static int32_t GNSServer_GetClientCount(IGNSServer* self) {
    // TODO: implement GetClientCount
    return 0;
}

// ─── Static vtable instance ──────────────────────────────────────────────────
static const IGNSServerVtbl g_vtbl = {
    GNSServer_QueryInterface,
    GNSServer_AddRef,
    GNSServer_Release,
    GNSServer_Start,
    GNSServer_Accept,
    GNSServer_Stop,
    GNSServer_GetClientCount,
};

// ─── Factory (C ABI) ─────────────────────────────────────────────────────────
extern "C" IGNSServer* GNSServer_Create() {
    auto impl = new GNSServerImpl();
    impl->vtbl = &g_vtbl;
    return reinterpret_cast<IGNSServer*>(impl);
}

// ─── C API wrappers (for BasicLang P/Invoke) ────────────────────────────────
extern "C" int32_t GNSServer_Start(void* h, const char* addr) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Start(reinterpret_cast<IGNSServer*>(h), addr);
}

extern "C" int32_t GNSServer_Accept(void* h, void** outConn) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Accept(reinterpret_cast<IGNSServer*>(h), outConn);
}

extern "C" void GNSServer_Stop(void* h) {
    reinterpret_cast<IGNSServer*>(h)->vtbl->Stop(reinterpret_cast<IGNSServer*>(h));
}

extern "C" int32_t GNSServer_GetClientCount(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->GetClientCount(reinterpret_cast<IGNSServer*>(h));
}

extern "C" uint32_t GNSServer_AddRef(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}

extern "C" NEWCOM_EXPORT uint32_t GNSServer_Release(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
