// gns.h — GameNetworkingSockets (annotated for NewCOM generation)
#pragma once

#include "newcom_abi.h"

#ifdef HAVE_GNS
#include "steam/steamnetworkingsockets.h"
#include "steam/isteamnetworkingutils.h"
#endif

NEWCOM_COMPONENT(GNSConnection)
    NEWCOM_METHOD(int, Send, const void* data, int size)
    NEWCOM_METHOD(int, Receive, void* buf, int* outSize)
    NEWCOM_METHOD(void, Close)
    NEWCOM_METHOD(int, GetState)
    NEWCOM_METHOD(int, GetRemoteAddr, char** outAddr)
NEWCOM_END

NEWCOM_COMPONENT(GNSServer)
    NEWCOM_METHOD(int, Start, const char* addr)
    NEWCOM_METHOD(int, Accept, void** outConn)
    NEWCOM_METHOD(void, Stop)
    NEWCOM_METHOD(int, GetClientCount)
NEWCOM_END

NEWCOM_COMPONENT(GNSClient)
    NEWCOM_METHOD(int, Connect, const char* addr)
    NEWCOM_METHOD(int, Send, const void* data, int size)
    NEWCOM_METHOD(int, Receive, void* buf, int* outSize)
    NEWCOM_METHOD(void, Disconnect)
    NEWCOM_METHOD(int, GetState)
NEWCOM_END
