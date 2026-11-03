// gnsconnection_newcom.h — NewCOM component interface
#pragma once
#include "newcom_abi.h"

// Interface: IGNSConnection
struct IGNSConnection;

struct IGNSConnectionVtbl {
    // INewUnknown
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
    int32_t (*Send)(IGNSConnection* self, const void* data, int32_t size);
    int32_t (*Receive)(IGNSConnection* self, void* buf, int* outSize);
    void (*Close)(IGNSConnection* self);
    int32_t (*GetState)(IGNSConnection* self);
    int32_t (*GetRemoteAddr)(IGNSConnection* self, char** outAddr);
};

struct IGNSConnection {
    const IGNSConnectionVtbl* vtbl;
};

extern "C" NEWCOM_EXPORT IGNSConnection* GNSConnection_Create();

extern "C" NEWCOM_EXPORT int32_t GNSConnection_Send(void* h, const void* data, int32_t size);
extern "C" NEWCOM_EXPORT int32_t GNSConnection_Receive(void* h, void* buf, int* outSize);
extern "C" NEWCOM_EXPORT void GNSConnection_Close(void* h);
extern "C" NEWCOM_EXPORT int32_t GNSConnection_GetState(void* h);
extern "C" NEWCOM_EXPORT int32_t GNSConnection_GetRemoteAddr(void* h, char** outAddr);
extern "C" NEWCOM_EXPORT uint32_t GNSConnection_Release(void* h);
