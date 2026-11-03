// $safeprojectname$.cpp - NewCOM component implementation
#include "component.h"
#include <cstring>
#include <cstdlib>
#include <atomic>

struct ${safeprojectname$}Impl {
    const I$safeprojectname$Vtbl* vtbl;
    std::atomic<uint32_t> refCount;

    ${safeprojectname$}Impl() : refCount(1), vtbl(nullptr) {}
};

static int32_t ${safeprojectname$}_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<${safeprojectname$}Impl*>(self)->refCount++;
    return NEWCOM_OK;
}

static uint32_t ${safeprojectname$}_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<${safeprojectname$}Impl*>(self)->refCount;
}

static uint32_t ${safeprojectname$}_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<${safeprojectname$}Impl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int ${safeprojectname$}_GetValue(I$safeprojectname$* self) {
    // TODO: implement
    return 42;
}

static const I$safeprojectname$Vtbl g_vtbl = {
    ${safeprojectname$}_QueryInterface,
    ${safeprojectname$}_AddRef,
    ${safeprojectname$}_Release,
    ${safeprojectname$}_GetValue,
};

extern "C" __declspec(dllexport) I$safeprojectname$* ${safeprojectname$}_Create() {
    auto impl = new ${safeprojectname$}Impl();
    impl->vtbl = &g_vtbl;
    return reinterpret_cast<I$safeprojectname$*>(impl);
}

extern "C" __declspec(dllexport) int ${safeprojectname$}_GetValue(void* h) {
    return reinterpret_cast<I$safeprojectname$*>(h)->vtbl->GetValue(reinterpret_cast<I$safeprojectname$*>(h));
}

extern "C" __declspec(dllexport) uint32_t ${safeprojectname$}_Release(void* h) {
    return reinterpret_cast<I$safeprojectname$*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
