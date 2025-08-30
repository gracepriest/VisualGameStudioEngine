// CPPengineTest.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>
#include "Framework.h"
#include "TitleScene.h"

// Link to your DLL's import library
#pragma comment(lib, "VisualGameStudioEngine.lib")  // Change this to your actual DLL name






int main()
{
    std::cout << "Testing Framework DLL...\n";
    
    
    // Initialize the framework
    if (!Framework_Initialize(800, 450, "Framework Test Window"))
    {
        std::cout << "Failed to initialize framework!\n";
        return -1;
    }
	WireEngineDraw(); // set the draw callback
	TitleScene titleScene;
	SetCurrentScene(&titleScene); // set initial scene

    std::cout << "Framework initialized successfully!\n";
    std::cout << "Window created - you should see a white window with 'Hello from Framework!' text\n";
    std::cout << "Close the window to exit...\n";
    Framework_InitAudio();
    //int m = Framework_AcquireMusicH("music.mp3");
	int s = Framework_LoadSoundH("paddle_hit.wav");
    /*Framework_SetMusicVolumeH(m, 0.8f);
    Framework_PlayMusicH(m);*/

	

    while (!Framework_ShouldClose()) {
        Framework_Update(); // internally calls Framework_UpdateAllMusic()
        
    }

    //Framework_ReleaseMusicH(m);
    Framework_CloseAudio();

    Framework_Shutdown();


    std::cout << "Framework shut down successfully!\n";
    std::cout << "Press Enter to exit...";
    std::cin.get();

    return 0;
}
