// framework.cpp
#include "pch.h"
#include "Framework.h"




extern "C" {
    bool Framework_Initialize(int width, int height, const char* title) {
        InitWindow(width, height, title);
        SetTargetFPS(60);
        return true;
    }

    // Store the user's draw callback
    static DrawCallback userDrawCallback = nullptr;

    void Framework_SetDrawCallback(DrawCallback callback) {
        userDrawCallback = callback;
    }
    void Framework_Update() {
        BeginDrawing();
        ClearBackground(RAYWHITE);

        // Call user's custom draw function if set
        if (userDrawCallback != nullptr) {
            userDrawCallback();
        }
        EndDrawing();
    }
    void Framework_BeginDrawing() {
        BeginDrawing();
	}
    void Framework_EndDrawing() {
        EndDrawing();
	}
    void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        ClearBackground(color);
	}
    bool Framework_ShouldClose() {
        return WindowShouldClose();
	}
    void Framework_Shutdown() {
		CloseWindow();  
	}
    void Framework_SetTargetFPS(int fps) {
        SetTargetFPS(fps);
	}
    float Framework_GetFrameTime() {
		return GetFrameTime();
	}
	double Framework_GetTime() {
		return GetTime();
	}
	int Framework_GetFPS(){
		return GetTime();
	}
	// Window management functions
	void Framework_SetWindowTitle(const char* title) {
		SetWindowTitle(title);
	}
	void Framework_SetWindowIcon(Image image) {
		SetWindowIcon(image);
	}
	void Framework_SetWindowPosition(int x, int y) {
		SetWindowPosition(x, y);
	}
	void Framework_SetWindowMonitor(int monitor) {
		SetWindowMonitor(monitor);
	}
	void Framework_SetWindowMinSize(int width, int height) {
		SetWindowMinSize(width, height);
	}
	void Framework_SetWindowSize(int width, int height) {
		SetWindowSize(width, height);
	}
	Vector2 Framework_GetScreenToWorld2D(Vector2 position, Camera2D camera) {
		return GetScreenToWorld2D(position, camera);
	}
	// Keyboard input functions
    bool Framework_IsKeyPressed(int key) {
		return IsKeyPressed(key);       
	}
    bool Framework_IsKeyPressedRepeat(int key) {
        return IsKeyPressedRepeat(key);
	}
    bool Framework_IsKeyDown(int key) {
		return IsKeyDown(key);  
	}
	bool Framework_IsKeyReleased(int key) {
		return IsKeyReleased(key);
	}
	bool Framework_IsKeyUp(int key) {               
		return IsKeyUp(key);
	}
    int Framework_GetKeyPressed() {
		return GetKeyPressed();
	}
	int Framework_GetCharPressed() {    
		return GetCharPressed();
	}
    void Framework_SetExitKey(int key) {
		SetExitKey(key);
	}
	int Framework_GetMouseX() { 
		return GetMouseX();
	}
	int Framework_GetMouseY() {
		return GetMouseY();
	}
    bool Framework_IsMouseButtonPressed(int button) {
        return IsMouseButtonPressed(button);
	}
    bool Framework_IsMouseButtonDown(int button) {
		return IsMouseButtonDown(button);
	}
	bool Framework_IsMouseButtonReleased(int button) {
		return IsMouseButtonReleased(button);   
	}
	bool Framework_IsMouseButtonUp(int button) {
		return IsMouseButtonUp(button);
	}
    Vector2 Framework_GetMousePosition() {
		return GetMousePosition();
	}
	Vector2 Framework_GetMouseDelta() {
		return GetMouseDelta();
	}
    void Framework_SetMousePosition(int x, int y) {
		SetMousePosition(x, y);
	}
    void Framework_SetMouseOffset(int offsetX, int offsetY) {
		SetMouseOffset(offsetX, offsetY);
	}
	void Framework_SetMouseScale(float scaleX, float scaleY) {
		SetMouseScale(scaleX, scaleY);
	}
	float Framework_GetMouseWheelMove() {
		return GetMouseWheelMove();
	}
	Vector2 Framework_GetMouseWheelMoveV() {
		return GetMouseWheelMoveV();
	}
	void Framework_SetMouseCursor(int cursor) {
		SetMouseCursor(cursor);
	}
	void Framework_ShowCursor() {
		ShowCursor();
	}
	void Framework_HideCursor() {
		HideCursor();
	}
	bool Framework_IsCursorHidden() {
		return IsCursorHidden();
	}
	void Framework_EnableCursor() {
		EnableCursor();
	}
	void Framework_DisableCursor() {
		DisableCursor();
	}
	bool Framework_IsCursorOnScreen() {
		return IsCursorOnScreen();
	}
	// Drawing functions
	// These functions use raylib's drawing capabilities to render text and shapes
    void Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawText(text, x, y, fontSize, color);

    }

    void Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawRectangle(x, y, width, height, color);
    }
    void Framework_DrawPixel(int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawPixel(posX, posY, color);
    }
    
	// Collision detection functions
	bool Framework_CheckCollisionRecs(Rectangle rec1, Rectangle rec2) {
		return CheckCollisionRecs(rec1, rec2);
	}
	bool Framework_CheckCollisionCircles(Vector2 center1, float radius1, Vector2 center2, float radius2) {
		return CheckCollisionCircles(center1, radius1, center2, radius2);
	}
	bool Framework_CheckCollisionCircleRec(Vector2 center, float radius, Rectangle rec) {
		return CheckCollisionCircleRec(center, radius, rec);
	}
	bool Framework_CheckCollisionCircleLine(Vector2 center, float radius, Vector2 p1, Vector2 p2) {
		return CheckCollisionCircleLine(center, radius, p1, p2);
	}
	bool Framework_CheckCollisionPointRec(Vector2 point, Rectangle rec) {
		return CheckCollisionPointRec(point, rec);
	}
	bool Framework_CheckCollisionPointCircle(Vector2 point, Vector2 center, float radius) {
		return CheckCollisionPointCircle(point, center, radius);
	}
	bool Framework_CheckCollisionPointTriangle(Vector2 point, Vector2 p1, Vector2 p2
		, Vector2 p3) {
		return CheckCollisionPointTriangle(point, p1, p2, p3);
	}
	bool Framework_CheckCollisionPointLine(Vector2 point, Vector2 p1, Vector2 p2
		, int threshold) {
		return CheckCollisionPointLine(point, p1, p2, threshold);
	}
	bool Framework_CheckCollisionPointPoly(Vector2 point, const Vector2* points
		, int pointCount) {
		return CheckCollisionPointPoly(point, points, pointCount);
	}
	bool Framework_CheckCollisionLines(Vector2 startPos1, Vector2 endPos1
		, Vector2 startPos2, Vector2 endPos2, Vector2* collisionPoint) {
		return CheckCollisionLines(startPos1, endPos1, startPos2, endPos2, collisionPoint);
	}
	Rectangle Framework_GetCollisionRec(Rectangle rec1, Rectangle rec2) {
		return GetCollisionRec(rec1, rec2);
	}
	void Framework_DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
		Color color = { r, g, b, a };
		DrawLine(startPosX, startPosY, endPosX, endPosY, color);
	}
	void Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
		Color color = { r, g, b, a };
		DrawCircle(centerX, centerY, radius, color);
	}
}
// End of extern "C"