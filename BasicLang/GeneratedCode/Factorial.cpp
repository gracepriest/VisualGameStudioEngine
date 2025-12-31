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
int32_t Factorial(int32_t n);
void Main();

// Function implementations
int32_t Factorial(int32_t n)
{
    bool t0 = {};
    int32_t t1 = {};
    int32_t t2 = {};
    int32_t t3 = {};

    t0 = n <= 1;
    if (t0) {
        goto if.then;
    }
    else {
        goto if.else;
    }
if.then:
    return 1;
if.else:
    t1 = n - 1;
    t2 = Factorial(t1);
    t3 = n * t2;
    return t3;
}

void Main()
{
    int32_t result = 0;

    result = Factorial(5);
    cout << result << endl;
    return;
}

int main(int argc, char* argv[])
{
    Main();
    return 0;
}
