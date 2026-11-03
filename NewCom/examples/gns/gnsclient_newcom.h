// gnsclient_newcom.h — NewCOM component interface
#pragma once
#include "newcom_abi.h"

// Interface: IGNSClient
struct IGNSClient;

struct IGNSClientVtbl {
    // INewUnknown
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
    int32_t (*Connect)(IGNSClient* self, const char* addr);
    int32_t (*Send)(IGNSClient* self, const void* data, int32_t size);
    int32_t (*Receive)(IGNSClient* self, void* buf, int* outSize);
    void (*Disconnect)(IGNSClient* self);
    int32_t (*GetState)(IGNSClient* self);
};

struct IGNSClient {
    const IGNSClientVtbl* vtbl;
};

extern "C" NEWCOM_EXPORT IGNSClient* GNSClient_Create();

extern "C" NEWCOM_EXPORT int32_t GNSClient_Connect(void* h, const char* addr);
extern "C" NEWCOM_EXPORT int32_t GNSClient_Send(void* h, const void* data, int32_t size);
extern "C" NEWCOM_EXPORT int32_t GNSClient_Receive(void* h, void* buf, int* outSize);
extern "C" NEWCOM_EXPORT void GNSClient_Disconnect(void* h);
extern "C" NEWCOM_EXPORT int32_t GNSClient_GetState(void* h);
extern "C" NEWCOM_EXPORT uint32_t GNSClient_Release(void* h);
