// gns_mock.h — Mock GameNetworkingSockets API for testing without real GNS
// Provides the same API surface as steam/steamnetworkingsockets.h
// Define HAVE_GNS and link against this mock to test the NewCOM wrapper
#pragma once

#include <cstdint>
#include <cstring>

typedef uint32_t HSteamNetConnection;
typedef uint32_t HSteamListenSocket;
typedef int64_t SteamNetworkingMicroseconds;
typedef uint32_t SteamNetConnectionInfo_t; // simplified

constexpr HSteamNetConnection k_HSteamNetConnection_Invalid = 0;
constexpr HSteamListenSocket k_HSteamListenSocket_Invalid = 0;

constexpr int k_nSteamNetworkingSend_Reliable = 1;

enum ESteamNetworkingConnectionState {
    k_ESteamNetworkingConnectionState_None = 0,
    k_ESteamNetworkingConnectionState_Connecting = 1,
    k_ESteamNetworkingConnectionState_Connected = 2,
    k_ESteamNetworkingConnectionState_ProblemDetectedLocally = 3,
    k_ESteamNetworkingConnectionState_ClosedByPeer = 4,
    k_ESteamNetworkingConnectionState_ProblemDetectedRemotely = 5,
};

struct SteamNetConnectionInfo_t_mock {
    ESteamNetworkingConnectionState m_eState;
    char m_addrRemote[256];
};

// Simplified message struct
struct ISteamNetworkingMessage {
    void* m_pData;
    uint32_t m_cbSize;
    void Release() { delete[] (char*)m_pData; delete this; }
};

// ─── Mock interface ──────────────────────────────────────────────────────────
class ISteamNetworkingSockets {
public:
    virtual HSteamNetConnection ConnectByIPAddress(const char* addr) {
        (void)addr;
        static uint32_t nextId = 100;
        return nextId++;
    }
    virtual int SendMessageToConnection(HSteamNetConnection conn, const void* data, uint32_t size, int flags, int64_t* outMsgNum) {
        (void)conn; (void)data; (void)flags; (void)outMsgNum;
        return (int)size; // pretend we sent all bytes
    }
    virtual int ReceiveMessagesOnConnection(HSteamNetConnection conn, ISteamNetworkingMessage** outMsg, int maxMsgs) {
        (void)conn; (void)maxMsgs;
        // Simulate receiving "Hello from peer!" on first call
        static bool sent = false;
        if (!sent) {
            sent = true;
            auto msg = new ISteamNetworkingMessage();
            const char* data = "Hello from peer!";
            msg->m_cbSize = (uint32_t)strlen(data);
            msg->m_pData = new char[msg->m_cbSize + 1];
            strcpy((char*)msg->m_pData, data);
            *outMsg = msg;
            return 1;
        }
        *outMsg = nullptr;
        return 0;
    }
    virtual bool GetConnectionInfo(HSteamNetConnection conn, SteamNetConnectionInfo_t_mock* info) {
        (void)conn;
        info->m_eState = k_ESteamNetworkingConnectionState_Connected;
        strcpy(info->m_addrRemote, "127.0.0.1:27015");
        return true;
    }
    virtual void CloseConnection(HSteamNetConnection conn, int reason, const char* debug, bool enableLinger) {
        (void)conn; (void)reason; (void)debug; (void)enableLinger;
    }
    virtual HSteamListenSocket CreateListenSocketIP(const char* addr, int options) {
        (void)addr; (void)options;
        static uint32_t nextId = 200;
        return nextId++;
    }
    virtual void CloseListenSocket(HSteamListenSocket sock) {
        (void)sock;
    }
};

// ─── Global instance ─────────────────────────────────────────────────────────
inline ISteamNetworkingSockets* SteamNetworkingSockets() {
    static ISteamNetworkingSockets instance;
    return &instance;
}

// ─── Utils ───────────────────────────────────────────────────────────────────
class ISteamNetworkingUtils {
public:
    SteamNetworkingMicroseconds GetLocalTimestamp() { return 1000000; }
};

inline ISteamNetworkingUtils* SteamNetworkingUtils() {
    static ISteamNetworkingUtils instance;
    return &instance;
}
