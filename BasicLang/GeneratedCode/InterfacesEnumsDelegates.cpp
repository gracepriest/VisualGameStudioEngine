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

// Forward declarations
class Circle;

// Enums
enum class Color
{
    Red = 0,
    Green = 5,
    Blue = 6
};

enum class DayOfWeek : int32_t
{
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6
};

// Delegate types
using MathOperation = std::function<int32_t(int32_t, int32_t)>;
using EventHandler = std::function<void(void*, std::string)>;

// Interfaces (abstract classes)
class IShape
{
public:
    virtual ~IShape() = default;

    virtual double GetArea() = 0;
    virtual double GetPerimeter() = 0;
};

// Classes
class Circle
{
private:
    double _radius;

public:
    Circle(double radius)
    {
        _radius = radius;
        return;
    }

    double GetArea()
    {
        double t0 = {};
        double t1 = {};

        t0 = 3.14159 * _radius;
        t1 = t0 * _radius;
        return t1;
    }

    double GetPerimeter()
    {
        double t0 = {};
        double t1 = {};

        t0 = 2 * 3.14159;
        t1 = t0 * _radius;
        return t1;
    }

};

// Function declarations
void Main();

// Function implementations
void Main()
{
    Circle circle = {};
    Circle t0 = {};
    double t1 = {};
    double t2 = {};
    std::string t3 = {};
    std::string t4 = {};

    Circle t0 = Circle(5);
    circle = t0;
    auto t1 = circle.GetArea();
    t3 = "Circle area: " + t1;
    cout << t3 << endl;
    auto t3 = circle.GetPerimeter();
    t4 = "Circle perimeter: " + t2;
    cout << t4 << endl;
    return;
}

int main(int argc, char* argv[])
{
    Main();
    return 0;
}
