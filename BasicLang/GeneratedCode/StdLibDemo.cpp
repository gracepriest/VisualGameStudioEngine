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
    double x = 0.0;
    double result = 0.0;

    x = 16;
    result = sqrt(16);
    cout << result << endl;
    result = abs(-5.5);
    cout << result << endl;
    result = pow(2, 8);
    cout << result << endl;
    return;
}

int main(int argc, char* argv[])
{
    Main();
    return 0;
}
