// greeter.cpp — NewCOM component implementation (C-style vtables)
#include "greeter.h"
#include <cstring>
#include <cstdlib>
#include <atomic>
#include <iostream>

// ─── Implementation struct (defined first) ────────────────────────────────────
struct GreeterImpl {
    const IGreeterVtbl* vtbl;
    std::atomic<uint32_t> refCount;
    int32_t greetingCount;

    GreeterImpl() : refCount(1), greetingCount(0), vtbl(nullptr) {}
};

// ─── Vtable function implementations ─────────────────────────────────────────
static int32_t Greeter_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<GreeterImpl*>(self)->refCount++;
    return NEWCOM_OK;
}

static uint32_t Greeter_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<GreeterImpl*>(self)->refCount;
}

static uint32_t Greeter_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<GreeterImpl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int32_t Greeter_SayHello(IGreeter* self, const char* name, char** outMessage) {
    auto impl = reinterpret_cast<GreeterImpl*>(self);
    impl->greetingCount++;
    std::string msg = std::string("Hello, ") + (name ? name : "World") + "!";
    *outMessage = (char*)malloc(msg.size() + 1);
    strcpy(*outMessage, msg.c_str());
    return NEWCOM_OK;
}

static int32_t Greeter_GetGreetingCount(IGreeter* self, int32_t* outCount) {
    auto impl = reinterpret_cast<GreeterImpl*>(self);
    *outCount = impl->greetingCount;
    return NEWCOM_OK;
}

// ─── Static vtable instance ──────────────────────────────────────────────────
static const IGreeterVtbl g_vtbl = {
    Greeter_QueryInterface,
    Greeter_AddRef,
    Greeter_Release,
    Greeter_SayHello,
    Greeter_GetGreetingCount
};

// ─── Factory (C ABI) ─────────────────────────────────────────────────────────
extern "C" NEWCOM_EXPORT IGreeter* Greeter_Create() {
    auto impl = new GreeterImpl();
    impl->vtbl = &g_vtbl;
    return reinterpret_cast<IGreeter*>(impl);
}

// ─── C API wrappers (for BasicLang P/Invoke) ────────────────────────────────
extern "C" NEWCOM_EXPORT int32_t Greeter_SayHello(void* h, const char* name, char** outMsg) {
    return reinterpret_cast<IGreeter*>(h)->vtbl->SayHello(reinterpret_cast<IGreeter*>(h), name, outMsg);
}

extern "C" NEWCOM_EXPORT int32_t Greeter_GetCount(void* h, int32_t* outCount) {
    return reinterpret_cast<IGreeter*>(h)->vtbl->GetGreetingCount(reinterpret_cast<IGreeter*>(h), outCount);
}

extern "C" NEWCOM_EXPORT uint32_t Greeter_Release(void* h) {
    return reinterpret_cast<IGreeter*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
