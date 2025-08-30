
#include "TitleScene.h"


void TitleScene::OnUpdateFrame(float dt) {
	// Per-frame updates (e.g., input handling)
	if (Framework_IsKeyPressed(32)) { // Space key to switch to GameScene
		std::cout << "Space pressed! Switching to GameScene...\n";
		// Here you would typically notify the scene manager to switch scenes
		// For this example, we'll just print a message
      

           
        
		SetCurrentScene(new MenuScene());
	}
}

void MenuScene::OnUpdateFrame(float dt) {
    dt = Framework_GetFrameTime();

    // --- Physics ---
    vy += g * dt;
    x += vx * dt;
    y += vy * dt;

    if (x < 0) {
        x = 0;
        vx = std::abs(vx);
    }
    if (x > 780) {
        x = 780;
        vx = -std::abs(vx);
    }
    if (y > 430) {
        y = 430;
        vy = -std::abs(vy) * 0.6f;  // bounce with damping


    }
}