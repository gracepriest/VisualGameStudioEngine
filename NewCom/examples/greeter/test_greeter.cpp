// test_greeter.cpp — C++ consumer of NewCOM Greeter
#include "greeter.h"
#include <iostream>
#include <cstring>

int main() {
    IGreeter* g = Greeter_Create();
    char* msg = nullptr;
    g->vtbl->SayHello(g, "World", &msg);
    std::cout << msg << std::endl;
    free(msg);

    int32_t count = 0;
    g->vtbl->GetGreetingCount(g, &count);
    std::cout << "Greeting count: " << count << std::endl;

    g->vtbl->Release(reinterpret_cast<INewUnknown*>(g));
    std::cout << "Released OK" << std::endl;
    return 0;
}
