#pragma once
#include <iostream>
#include <vector>
#include <string>
#include <cstdint>
#include <cmath>
#include <algorithm>
#include <cstdlib>
#include <ctime>
#include <functional>
#include <memory>

using namespace std;

// Function declarations
void Main();

// Function implementations
void Main()
{
    int32_t result = 0;

    result = MessageBoxA("Hello from BasicLang!");
    cout << "Message result: " << endl;
    cout << result << endl;
    return;
}

int main(int argc, char* argv[])
{
    Main();
    return 0;
}
