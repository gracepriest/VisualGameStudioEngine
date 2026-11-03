#include "steam/steamnetworkingsockets.h"
#include "steam/isteamnetworkingutils.h"
#include <iostream>
int main() {
    std::cout << "Getting iface..." << std::endl;
    auto iface = SteamNetworkingSockets();
    std::cout << "iface: " << iface << std::endl;
    SteamNetworkingIPAddr addr;
    bool ok = SteamNetworkingUtils()->SteamNetworkingIPAddr_ParseString(&addr, "127.0.0.1:27015");
    std::cout << "ParseString: " << ok << std::endl;
    auto conn = iface->ConnectByIPAddress(addr, 0, nullptr);
    std::cout << "Connected: " << conn << std::endl;
    return 0;
}
