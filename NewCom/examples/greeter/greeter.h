// greeter.h — NewCOM component interface
#pragma once
#include "newcom_abi.h"

NEWCOM_DEFINE_GUID(IID_IGreeter,
    0x12345678, 0x1234, 0x1234,
    0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc, 0xde, 0xf0);

NEWCOM_DEFINE_GUID(CLSID_Greeter,
    0x87654321, 0x4321, 0x4321,
    0xfe, 0xdc, 0xba, 0x98, 0x76, 0x54, 0x32, 0x10);

struct IGreeter;

struct IGreeterVtbl {
    // INewUnknown
    int32_t  (*QueryInterface)(INewUnknown* self, const NewComGUID* iid, void** out);
    uint32_t (*AddRef)(INewUnknown* self);
    uint32_t (*Release)(INewUnknown* self);
    // IGreeter
    int32_t  (*SayHello)(IGreeter* self, const char* name, char** outMessage);
    int32_t  (*GetGreetingCount)(IGreeter* self, int32_t* outCount);
};

struct IGreeter {
    const IGreeterVtbl* vtbl;
};

// Factory (C ABI)
#ifdef _WIN32
#define NEWCOM_EXPORT __declspec(dllexport)
#else
#define NEWCOM_EXPORT __attribute__((visibility("default")))
#endif

extern "C" NEWCOM_EXPORT IGreeter* Greeter_Create();
