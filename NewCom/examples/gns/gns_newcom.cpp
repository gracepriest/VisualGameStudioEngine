// gns_newcom.cpp — GameNetworkingSockets NewCOM wrapper
//
// Compile with -DHAVE_GNS and link against GameNetworkingSockets to use real GNS.
// Without HAVE_GNS, provides stub implementations for testing.

#include "newcom_abi.h"
#include "gnsconnection_newcom.h"
#include "gnsserver_newcom.h"
#include "gnsclient_newcom.h"

#ifdef HAVE_GNS
#include "steam/steamnetworkingsockets.h"
#include "steam/isteamnetworkingutils.h"
#endif

#include <cstring>
#include <cstdlib>
#include <atomic>
#include <string>

// ─── GNSConnection ───────────────────────────────────────────────────────────

struct GNSConnectionImpl {
    const IGNSConnectionVtbl* vtbl;
    std::atomic<uint32_t> refCount;

#ifdef HAVE_GNS
    HSteamNetConnection conn;
    ISteamNetworkingSockets* iface;
#else
    int mockState; // 0=None, 1=Connecting, 2=Connected
    char mockAddr[256];
#endif

    GNSConnectionImpl() : refCount(1), vtbl(nullptr)
#ifdef HAVE_GNS
        , conn(k_HSteamNetConnection_Invalid), iface(nullptr)
#else
        , mockState(2)
#endif
    {}
};

// Vtable functions
static int32_t GNSConn_QueryInterface(INewUnknown* self, const NewComGUID* iid, void** out) {
    *out = self;
    reinterpret_cast<GNSConnectionImpl*>(self)->refCount++;
    return NEWCOM_OK;
}
static uint32_t GNSConn_AddRef(INewUnknown* self) {
    return ++reinterpret_cast<GNSConnectionImpl*>(self)->refCount;
}
static uint32_t GNSConn_Release(INewUnknown* self) {
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    uint32_t c = --impl->refCount;
    if (c == 0) delete impl;
    return c;
}

static int GNSConn_Send(IGNSConnection* self, const void* data, int size) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    SteamNetworkingMicroseconds now = SteamNetworkingUtils()->GetLocalTimestamp();
    return impl->iface->SendMessageToConnection(impl->conn, data, size, k_nSteamNetworkingSend_Reliable, nullptr);
#else
    // Stub: pretend we sent all bytes
    (void)self; (void)data;
    return size;
#endif
}

static int GNSConn_Receive(IGNSConnection* self, void* buf, int* outSize) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    ISteamNetworkingMessage* msg = nullptr;
    int numMsgs = impl->iface->ReceiveMessagesOnConnection(impl->conn, &msg, 1);
    if (numMsgs > 0 && msg) {
        int toCopy = (int)msg->m_cbSize;
        if (toCopy > *outSize) toCopy = *outSize;
        memcpy(buf, msg->m_pData, toCopy);
        *outSize = toCopy;
        msg->Release();
        return toCopy;
    }
    *outSize = 0;
    return 0;
#else
    (void)self; (void)buf;
    *outSize = 0;
    return 0;
#endif
}

static void GNSConn_Close(IGNSConnection* self) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    if (impl->iface && impl->conn != k_HSteamNetConnection_Invalid)
        impl->iface->CloseConnection(impl->conn, 0, nullptr, false);
#else
    (void)self;
#endif
}

static int GNSConn_GetState(IGNSConnection* self) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    SteamNetConnectionInfo_t info;
    if (impl->iface->GetConnectionInfo(impl->conn, &info))
        return (int)info.m_eState;
    return 0;
#else
    return reinterpret_cast<GNSConnectionImpl*>(self)->mockState;
#endif
}

static int GNSConn_GetRemoteAddr(IGNSConnection* self, char** outAddr) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSConnectionImpl*>(self);
    SteamNetConnectionInfo_t info;
    if (impl->iface->GetConnectionInfo(impl->conn, &info)) {
        char buf[256];
        info.m_addrRemote.ToString(buf, sizeof(buf), true);
        *outAddr = (char*)malloc(strlen(buf) + 1);
        strcpy(*outAddr, buf);
        return NEWCOM_OK;
    }
    *outAddr = nullptr;
    return NEWCOM_E_FAIL;
#else
    (void)self;
    const char* addr = "127.0.0.1:27015";
    *outAddr = (char*)malloc(strlen(addr) + 1);
    strcpy(*outAddr, addr);
    return NEWCOM_OK;
#endif
}

static const IGNSConnectionVtbl g_GNSConn_vtbl = {
    GNSConn_QueryInterface,
    GNSConn_AddRef,
    GNSConn_Release,
    GNSConn_Send,
    GNSConn_Receive,
    GNSConn_Close,
    GNSConn_GetState,
    GNSConn_GetRemoteAddr,
};

extern "C" NEWCOM_EXPORT IGNSConnection* GNSConnection_Create() {
    auto impl = new GNSConnectionImpl();
    impl->vtbl = &g_GNSConn_vtbl;
#ifdef HAVE_GNS
    SteamNetworkingErrMsg errMsg;
    if (!GameNetworkingSockets_Init(nullptr, errMsg)) {
        // Initialization failed
    }
    impl->iface = SteamNetworkingSockets();
    // Parse address
    SteamNetworkingIPAddr addr;
    SteamNetworkingUtils()->SteamNetworkingIPAddr_ParseString(&addr, "127.0.0.1:27015");
    impl->conn = impl->iface->ConnectByIPAddress(addr, 0, nullptr);
#endif
    return reinterpret_cast<IGNSConnection*>(impl);
}

// C API wrappers
extern "C" NEWCOM_EXPORT int GNSConnection_Send(void* h, const void* data, int size) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Send(reinterpret_cast<IGNSConnection*>(h), data, size);
}
extern "C" NEWCOM_EXPORT int GNSConnection_Receive(void* h, void* buf, int* outSize) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Receive(reinterpret_cast<IGNSConnection*>(h), buf, outSize);
}
extern "C" NEWCOM_EXPORT void GNSConnection_Close(void* h) {
    reinterpret_cast<IGNSConnection*>(h)->vtbl->Close(reinterpret_cast<IGNSConnection*>(h));
}
extern "C" NEWCOM_EXPORT int GNSConnection_GetState(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->GetState(reinterpret_cast<IGNSConnection*>(h));
}
extern "C" NEWCOM_EXPORT int GNSConnection_GetRemoteAddr(void* h, char** outAddr) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->GetRemoteAddr(reinterpret_cast<IGNSConnection*>(h), outAddr);
}
extern "C" NEWCOM_EXPORT uint32_t GNSConnection_AddRef(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}
extern "C" NEWCOM_EXPORT uint32_t GNSConnection_Release(void* h) {
    return reinterpret_cast<IGNSConnection*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}

// ─── GNSServer ───────────────────────────────────────────────────────────────

struct GNSServerImpl {
    const IGNSServerVtbl* vtbl;
    std::atomic<uint32_t> refCount;
#ifdef HAVE_GNS
    HSteamListenSocket listenSocket;
    ISteamNetworkingSockets* iface;
#else
    int mockClientCount;
    char mockAddr[256];
#endif

    GNSServerImpl() : refCount(1), vtbl(nullptr)
#ifdef HAVE_GNS
        , listenSocket(k_HSteamListenSocket_Invalid), iface(nullptr)
#else
        , mockClientCount(0)
#endif
    { 
#ifndef HAVE_GNS
        strcpy(mockAddr, "0.0.0.0:0"); 
#endif
    }
};

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

static int GNSServer_Start(IGNSServer* self, const char* addr) {
#ifdef HAVE_GNS
    (void)self; (void)addr;
    return 0; // TODO: implement with real GNS
#else
    auto impl = reinterpret_cast<GNSServerImpl*>(self);
    strncpy(impl->mockAddr, addr ? addr : "0.0.0.0:27015", sizeof(impl->mockAddr) - 1);
    return 0; // success
#endif
}

static int GNSServer_Accept(IGNSServer* self, void** outConn) {
#ifdef HAVE_GNS
    (void)self; (void)outConn;
    return -1; // no pending connections
#else
    (void)self;
    *outConn = nullptr;
    return -1; // no pending connections (stub)
#endif
}

static void GNSServer_Stop(IGNSServer* self) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSServerImpl*>(self);
    if (impl->iface && impl->listenSocket != k_HSteamListenSocket_Invalid)
        impl->iface->CloseListenSocket(impl->listenSocket);
#else
    (void)self;
#endif
}

static int GNSServer_GetClientCount(IGNSServer* self) {
#ifdef HAVE_GNS
    (void)self;
    return 0;
#else
    return reinterpret_cast<GNSServerImpl*>(self)->mockClientCount;
#endif
}

static const IGNSServerVtbl g_GNSServer_vtbl = {
    GNSServer_QueryInterface,
    GNSServer_AddRef,
    GNSServer_Release,
    GNSServer_Start,
    GNSServer_Accept,
    GNSServer_Stop,
    GNSServer_GetClientCount,
};

extern "C" NEWCOM_EXPORT IGNSServer* GNSServer_Create() {
    auto impl = new GNSServerImpl();
    impl->vtbl = &g_GNSServer_vtbl;
#ifdef HAVE_GNS
    // impl->iface = SteamNetworkingSockets();
#endif
    return reinterpret_cast<IGNSServer*>(impl);
}

// C API wrappers
extern "C" NEWCOM_EXPORT int GNSServer_Start(void* h, const char* addr) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Start(reinterpret_cast<IGNSServer*>(h), addr);
}
extern "C" NEWCOM_EXPORT int GNSServer_Accept(void* h, void** outConn) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Accept(reinterpret_cast<IGNSServer*>(h), outConn);
}
extern "C" NEWCOM_EXPORT void GNSServer_Stop(void* h) {
    reinterpret_cast<IGNSServer*>(h)->vtbl->Stop(reinterpret_cast<IGNSServer*>(h));
}
extern "C" NEWCOM_EXPORT int GNSServer_GetClientCount(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->GetClientCount(reinterpret_cast<IGNSServer*>(h));
}
extern "C" NEWCOM_EXPORT uint32_t GNSServer_AddRef(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}
extern "C" NEWCOM_EXPORT uint32_t GNSServer_Release(void* h) {
    return reinterpret_cast<IGNSServer*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}

// ─── GNSClient ───────────────────────────────────────────────────────────────

struct GNSClientImpl {
    const IGNSClientVtbl* vtbl;
    std::atomic<uint32_t> refCount;
#ifdef HAVE_GNS
    HSteamNetConnection conn;
    ISteamNetworkingSockets* iface;
#else
    int mockState;
#endif

    GNSClientImpl() : refCount(1), vtbl(nullptr)
#ifdef HAVE_GNS
        , conn(k_HSteamNetConnection_Invalid), iface(nullptr)
#else
        , mockState(0)
#endif
    {}
};

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

static int GNSClient_Connect(IGNSClient* self, const char* addr) {
#ifdef HAVE_GNS
    (void)self; (void)addr;
    return 0;
#else
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    impl->mockState = 2; // Connected
    return 0;
#endif
}

static int GNSClient_Send(IGNSClient* self, const void* data, int size) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    return impl->iface->SendMessageToConnection(impl->conn, data, size, k_nSteamNetworkingSend_Reliable, nullptr);
#else
    (void)self; (void)data;
    return size;
#endif
}

static int GNSClient_Receive(IGNSClient* self, void* buf, int* outSize) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    ISteamNetworkingMessage* msg = nullptr;
    int numMsgs = impl->iface->ReceiveMessagesOnConnection(impl->conn, &msg, 1);
    if (numMsgs > 0 && msg) {
        int toCopy = (int)msg->m_cbSize;
        if (toCopy > *outSize) toCopy = *outSize;
        memcpy(buf, msg->m_pData, toCopy);
        *outSize = toCopy;
        msg->Release();
        return toCopy;
    }
    *outSize = 0;
    return 0;
#else
    (void)self; (void)buf;
    *outSize = 0;
    return 0;
#endif
}

static void GNSClient_Disconnect(IGNSClient* self) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    if (impl->iface && impl->conn != k_HSteamNetConnection_Invalid)
        impl->iface->CloseConnection(impl->conn, 0, nullptr, false);
#else
    reinterpret_cast<GNSClientImpl*>(self)->mockState = 0;
#endif
}

static int GNSClient_GetState(IGNSClient* self) {
#ifdef HAVE_GNS
    auto impl = reinterpret_cast<GNSClientImpl*>(self);
    SteamNetConnectionInfo_t info;
    if (impl->iface->GetConnectionInfo(impl->conn, &info))
        return (int)info.m_eState;
    return 0;
#else
    return reinterpret_cast<GNSClientImpl*>(self)->mockState;
#endif
}

static const IGNSClientVtbl g_GNSClient_vtbl = {
    GNSClient_QueryInterface,
    GNSClient_AddRef,
    GNSClient_Release,
    GNSClient_Connect,
    GNSClient_Send,
    GNSClient_Receive,
    GNSClient_Disconnect,
    GNSClient_GetState,
};

extern "C" NEWCOM_EXPORT IGNSClient* GNSClient_Create() {
    auto impl = new GNSClientImpl();
    impl->vtbl = &g_GNSClient_vtbl;
#ifdef HAVE_GNS
    // impl->iface = SteamNetworkingSockets();
#endif
    return reinterpret_cast<IGNSClient*>(impl);
}

// C API wrappers
extern "C" NEWCOM_EXPORT int GNSClient_Connect(void* h, const char* addr) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Connect(reinterpret_cast<IGNSClient*>(h), addr);
}
extern "C" NEWCOM_EXPORT int GNSClient_Send(void* h, const void* data, int size) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Send(reinterpret_cast<IGNSClient*>(h), data, size);
}
extern "C" NEWCOM_EXPORT int GNSClient_Receive(void* h, void* buf, int* outSize) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Receive(reinterpret_cast<IGNSClient*>(h), buf, outSize);
}
extern "C" NEWCOM_EXPORT void GNSClient_Disconnect(void* h) {
    reinterpret_cast<IGNSClient*>(h)->vtbl->Disconnect(reinterpret_cast<IGNSClient*>(h));
}
extern "C" NEWCOM_EXPORT int GNSClient_GetState(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->GetState(reinterpret_cast<IGNSClient*>(h));
}
extern "C" NEWCOM_EXPORT uint32_t GNSClient_AddRef(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->AddRef(reinterpret_cast<INewUnknown*>(h));
}
extern "C" NEWCOM_EXPORT uint32_t GNSClient_Release(void* h) {
    return reinterpret_cast<IGNSClient*>(h)->vtbl->Release(reinterpret_cast<INewUnknown*>(h));
}
