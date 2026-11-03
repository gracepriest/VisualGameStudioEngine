#include "gnsconnection_newcom.h"
#include <iostream>

int main() {
    std::cout << "Creating..." << std::endl;
    auto conn = GNSConnection_Create();
    std::cout << "Created: " << conn << std::endl;
    
    int state = conn->vtbl->GetState(conn);
    std::cout << "State: " << state << std::endl;
    
    conn->vtbl->Release(reinterpret_cast<INewUnknown*>(conn));
    std::cout << "Released OK" << std::endl;
    return 0;
}
