// gns.h — GameNetworkingSockets (annotated for NewCOM generation)
#pragma once

NEWCOM_COMPONENT(GNSConnection)
    NEWCOM_METHOD(int, Send, const void* data, int size)
    NEWCOM_METHOD(int, Receive, void* buf, int* outSize)
    NEWCOM_METHOD(void, Close)
NEWCOM_END

NEWCOM_COMPONENT(GNSServer)
    NEWCOM_METHOD(int, Start, const char* addr)
    NEWCOM_METHOD(int, Accept, void** outConn)
    NEWCOM_METHOD(void, Stop)
NEWCOM_END
