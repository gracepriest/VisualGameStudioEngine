// CPPengineTest.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>
#include "Framework.h"

// Link to your DLL's import library
#pragma comment(lib, "VisualGameStudioEngine.lib")  // Change this to your actual DLL name

void MyCustomDraw() {
    Framework_DrawText("Hello from Callback!", 100, 10, 20, 255, 0, 0, 255);
    Framework_DrawText("Score: 1000", 100, 40, 16, 0, 255, 0, 255);
    Framework_DrawText("Level: 5", 100, 60, 16, 0, 0, 255, 255);
	Framework_DrawRectangle(50, 100, 200, 100, 255, 255, 0, 128);
	Framework_DrawLine(50, 100, 250, 200, 255, 0, 255, 255);
	Framework_DrawCircle(400, 200, 50, 0, 255, 255, 128);
}

int main()
{
    std::cout << "Testing Framework DLL...\n";

    // Initialize the framework
    if (!Framework_Initialize(800, 450, "Framework Test Window"))
    {
        std::cout << "Failed to initialize framework!\n";
        return -1;
    }

    std::cout << "Framework initialized successfully!\n";
    std::cout << "Window created - you should see a white window with 'Hello from Framework!' text\n";
    std::cout << "Close the window to exit...\n";
    // Register your custom draw function
    Framework_SetDrawCallback(MyCustomDraw);

    // Main game loop
    while (!Framework_ShouldClose())
    {
        Framework_Update();
       
    }

    // Cleanup
    Framework_Shutdown();

    std::cout << "Framework shut down successfully!\n";
    std::cout << "Press Enter to exit...";
    std::cin.get();

    return 0;
}
