#include <windows.h>
#include <iostream>
int main() {
    HMODULE h = LoadLibraryA("gns_newcom.dll");
    if (!h) { std::cout << "LoadLibrary failed: " << GetLastError() << std::endl; return 1; }
    std::cout << "Loaded OK" << std::endl;
    auto fn = (void* (*)())GetProcAddress(h, "GNSConnection_Create");
    if (!fn) { std::cout << "GetProcAddress failed" << std::endl; return 1; }
    std::cout << "Got factory" << std::endl;
    auto conn = fn();
    std::cout << "Created: " << conn << std::endl;
    return 0;
}
