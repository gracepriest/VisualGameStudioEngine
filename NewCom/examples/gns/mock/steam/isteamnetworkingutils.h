// isteamnetworkingutils.h — Mock SteamNetworkingUtils
#pragma once
#include <cstdint>

typedef int64_t SteamNetworkingMicroseconds;

class ISteamNetworkingUtils {
public:
    SteamNetworkingMicroseconds GetLocalTimestamp() { return 1000000; }
};

inline ISteamNetworkingUtils* SteamNetworkingUtils() {
    static ISteamNetworkingUtils instance;
    return &instance;
}
