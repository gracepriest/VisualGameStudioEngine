#pragma once


// title scene example
#include "SceneBase.h"
#include "framework.h"
#include <iostream>
#include <string>



class TitleScene : public Scene
{
public:
	
	TitleScene() : sceneID(-1) {}
	virtual ~TitleScene() {}
	virtual void OnEnter() override {
		std::cout << "TitleScene: OnEnter\n";
	}
	virtual void OnExit() override {
		std::cout << "TitleScene: OnExit\n";
	}
	virtual void OnResume() override {
		std::cout << "TitleScene: OnResume\n";
	}
	virtual void OnUpdateFixed(double dt) override {
		// Fixed-step updates (e.g., physics)
	}
	virtual void OnUpdateFrame(float dt) override;
	virtual void OnDraw() override {
		Framework_ClearBackground(100, 149, 237, 255); // Cornflower blue
		std::string text = "Title Scene - Press SPACE to Start";
		int textWidth = 10 * static_cast<int>(text.length()); // Approximate width
		int x = (800 - textWidth) / 2; // Centered horizontally
		int y = 200; // Fixed vertical position
		Framework_DrawText(text.c_str(), x, y, 20, 255, 255, 255, 255); // White text
	}
	int sceneID; // ID assigned by the framework
};

class MenuScene : public Scene
{
public:
	// Physics state
	float x = 100.0f;
	float y = 150.0f;
	float vx = 120.0f;
	float vy = 0.0f;
	float g = 800.0f;
	MenuScene() : sceneID(-1) {}
	virtual ~MenuScene() {}
	virtual void OnEnter() override {
		std::cout << "TitleScene: OnEnter\n";
	}
	virtual void OnExit() override {
		std::cout << "TitleScene: OnExit\n";
	}
	virtual void OnResume() override {
		std::cout << "TitleScene: OnResume\n";
	}
	virtual void OnUpdateFixed(double dt) override {
		// Fixed-step updates (e.g., physics)
	}
	virtual void OnUpdateFrame(float dt) override;
	virtual void OnDraw() override {
		Framework_ClearBackground(10, 10, 20, 255);
		Framework_DrawText("GAME SCENE (Backspace to Title)", 20, 14, 20, 255, 255, 255, 255);
		Framework_DrawRectangle((int)x, (int)y, 20, 20, 120, 220, 255, 255);
		Framework_DrawFPS(700, 10);
	}
	int sceneID; // ID assigned by the framework
};