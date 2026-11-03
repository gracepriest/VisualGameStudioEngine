// gnsconnection_newcom.cpp — NewCOM implementation
#include "gnsconnection_newcom.h"
#include <cstring>
#include <cstdlib>
#include <atomic>

// ─── Implementation struct ────────────────────────────────────────────────────
struct GNSConnectionImpl {
    const IGNSConnectionVtbl* vtbl;
    std::atomic<uint32_t> refCount;

    GNSConnectionImpl() : refCount(1), vtbl(nullptr) {}
};

// ─── Vtable function implementations ─────────────────────────────────────────
static int32_t GNSConnection_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<GNSConnectionImpl*>(self)->refCount++;
    return NEWCOM_OK;
}

static uint32_t GNSConnection_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<GNSConnectionImpl*>(self)->refCount;
}

static uint32_t GNSConnection_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int32_t GNSConnection_Send(IGNSConnection* self, const void* data, int32_t size) {
    // TODO: implement Send
    return 0;
}

static int32_t GNSConnection_Receive(IGNSConnection* self, void* buf, int* outSize) {
    // TODO: implement Receive
    return 0;
}

static void GNSConnection_Close(IGNSConnection* self) {
    // TODO: implement Close
}

static int32_t GNSConnection_GetState(IGNSConnection* self) {
    // TODO: implement GetState
    return 0;
}

static int32_t GNSConnection_GetRemoteAddr(IGNSConnection* self, char** outAddr) {
    // TODO: implement GetRemoteAddr
    return 0;
}

// ─── Static vtable instance ──────────────────────────────────────────────────
static const IGNSConnectionVtbl g_vtbl = {
    GNSConnection_QueryInterface,
    GNSConnection_AddRef,
    GNSConnection_Release,
    GNSConnection_Send,
    GNSConnection_Receive,
    GNSConnection_Close,
    GNSConnection_GetState,
    GNSConnection_GetRemoteAddr,
};

// ─── Factory (C ABI) ─────────────────────────────────────────────────────────
extern "C" IGNSConnection* GNSConnection_Create() {
    auto impl = new GNSConnectionImpl();
    impl->vtbl = &g_vtbl;
    return reinterpret_cast<IGNSConnection*>(impl);
}

// ─── C API wrappers (for BasicLang P/Invoke) ────────────────────────────────
extern "C" int32_t GNSConnection_Send(void* h, const void* data, int32_t size) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Send(reinterpret_cast<IGNSConnection*>(h), data, size);
}

extern "C" int32_t GNSConnection_Receive(void* h, void* buf, int* outSize) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Receive(reinterpret_cast<IGNSConnection*>(h), buf, outSize);
}

extern "C" void GNSConnection_Close(void* h) {
    reinterpret_cast<IGNSConnection*>(h)->vtbl->Close(reinterpret_cast<IGNSConnection*>(h));
}

extern "C" int32_t GNSConnection_GetState(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->GetState(reinterpret_cast<IGNSConnection*>(h));
}

extern "C" int32_t GNSConnection_GetRemoteAddr(void* h, char** outAddr) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->GetRemoteAddr(reinterpret_cast<IGNSConnection*>(h), outAddr);
}

extern "C" uint32_t GNSConnection_AddRef(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}

extern "C" NEWCOM_EXPORT uint32_t GNSConnection_Release(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
