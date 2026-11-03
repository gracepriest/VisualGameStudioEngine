// gnsserver_newcom.h — NewCOM component interface
#pragma once
#include "newcom_abi.h"

// Interface: IGNSServer
struct IGNSServer;

struct IGNSServerVtbl {
    // INewUnknown
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
    int32_t (*Start)(IGNSServer* self, const char* addr);
    int32_t (*Accept)(IGNSServer* self, void** outConn);
    void (*Stop)(IGNSServer* self);
    int32_t (*GetClientCount)(IGNSServer* self);
};

struct IGNSServer {
    const IGNSServerVtbl* vtbl;
};

extern "C" NEWCOM_EXPORT IGNSServer* GNSServer_Create();

extern "C" NEWCOM_EXPORT int32_t GNSServer_Start(void* h, const char* addr);
extern "C" NEWCOM_EXPORT int32_t GNSServer_Accept(void* h, void** outConn);
extern "C" NEWCOM_EXPORT void GNSServer_Stop(void* h);
extern "C" NEWCOM_EXPORT int32_t GNSServer_GetClientCount(void* h);
extern "C" NEWCOM_EXPORT uint32_t GNSServer_Release(void* h);
