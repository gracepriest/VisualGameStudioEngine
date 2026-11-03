// gnsclient_newcom.cpp — NewCOM implementation
#include "gnsclient_newcom.h"
#include <cstring>
#include <cstdlib>
#include <atomic>

// ─── Implementation struct ────────────────────────────────────────────────────
struct GNSClientImpl {
    const IGNSClientVtbl* vtbl;
    std::atomic<uint32_t> refCount;

    GNSClientImpl() : refCount(1), vtbl(nullptr) {}
};

// ─── Vtable function implementations ─────────────────────────────────────────
static int32_t GNSClient_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<GNSClientImpl*>(self)->refCount++;
    return NEWCOM_OK;
}

static uint32_t GNSClient_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<GNSClientImpl*>(self)->refCount;
}

static uint32_t GNSClient_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int32_t GNSClient_Connect(IGNSClient* self, const char* addr) {
    // TODO: implement Connect
    return 0;
}

static int32_t GNSClient_Send(IGNSClient* self, const void* data, int32_t size) {
    // TODO: implement Send
    return 0;
}

static int32_t GNSClient_Receive(IGNSClient* self, void* buf, int* outSize) {
    // TODO: implement Receive
    return 0;
}

static void GNSClient_Disconnect(IGNSClient* self) {
    // TODO: implement Disconnect
}

static int32_t GNSClient_GetState(IGNSClient* self) {
    // TODO: implement GetState
    return 0;
}

// ─── Static vtable instance ──────────────────────────────────────────────────
static const IGNSClientVtbl g_vtbl = {
    GNSClient_QueryInterface,
    GNSClient_AddRef,
    GNSClient_Release,
    GNSClient_Connect,
    GNSClient_Send,
    GNSClient_Receive,
    GNSClient_Disconnect,
    GNSClient_GetState,
};

// ─── Factory (C ABI) ─────────────────────────────────────────────────────────
extern "C" IGNSClient* GNSClient_Create() {
    auto impl = new GNSClientImpl();
    impl->vtbl = &g_vtbl;
    return reinterpret_cast<IGNSClient*>(impl);
}

// ─── C API wrappers (for BasicLang P/Invoke) ────────────────────────────────
extern "C" int32_t GNSClient_Connect(void* h, const char* addr) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Connect(reinterpret_cast<IGNSClient*>(h), addr);
}

extern "C" int32_t GNSClient_Send(void* h, const void* data, int32_t size) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Send(reinterpret_cast<IGNSClient*>(h), data, size);
}

extern "C" int32_t GNSClient_Receive(void* h, void* buf, int* outSize) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Receive(reinterpret_cast<IGNSClient*>(h), buf, outSize);
}

extern "C" void GNSClient_Disconnect(void* h) {
    reinterpret_cast<IGNSClient*>(h)->vtbl->Disconnect(reinterpret_cast<IGNSClient*>(h));
}

extern "C" int32_t GNSClient_GetState(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->GetState(reinterpret_cast<IGNSClient*>(h));
}

extern "C" uint32_t GNSClient_AddRef(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}

extern "C" NEWCOM_EXPORT uint32_t GNSClient_Release(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
