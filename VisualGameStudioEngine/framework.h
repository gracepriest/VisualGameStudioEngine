#pragma once
#include "pch.h"

typedef void (*DrawCallback)();

extern "C" {
    // Framework DLL interface
    // =======================

    // Window management
    __declspec(dllexport) bool Framework_Initialize(int width, int height, const char* title);
    __declspec(dllexport) void Framework_Update();
    __declspec(dllexport) bool Framework_ShouldClose();
    __declspec(dllexport) void Framework_Shutdown();

    // Callback functions
    __declspec(dllexport) void Framework_SetDrawCallback(DrawCallback callback);
    __declspec(dllexport) void Framework_BeginDrawing();
    __declspec(dllexport) void Framework_EndDrawing();
    __declspec(dllexport) void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Keyboard input
    __declspec(dllexport) bool Framework_IsKeyPressed(int key);
    __declspec(dllexport) bool Framework_IsKeyPressedRepeat(int key);
    __declspec(dllexport) bool Framework_IsKeyDown(int key);
    __declspec(dllexport) bool Framework_IsKeyReleased(int key);
    __declspec(dllexport) bool Framework_IsKeyUp(int key);
    __declspec(dllexport) int Framework_GetKeyPressed();
    __declspec(dllexport) int Framework_GetCharPressed();
    __declspec(dllexport) void Framework_SetExitKey(int key);

    // Mouse input
    __declspec(dllexport) int Framework_GetMouseX();
    __declspec(dllexport) int Framework_GetMouseY();
    __declspec(dllexport) bool Framework_IsMouseButtonPressed(int button);
    __declspec(dllexport) bool Framework_IsMouseButtonDown(int button);
    __declspec(dllexport) bool Framework_IsMouseButtonReleased(int button);
    __declspec(dllexport) bool Framework_IsMouseButtonUp(int button);
    __declspec(dllexport) Vector2 Framework_GetMousePosition();
    __declspec(dllexport) Vector2 Framework_GetMouseDelta();
    __declspec(dllexport) void Framework_SetMousePosition(int x, int y);
    __declspec(dllexport) void Framework_SetMouseOffset(int offsetX, int offsetY);
    __declspec(dllexport) void Framework_SetMouseScale(float scaleX, float scaleY);
    __declspec(dllexport) float Framework_GetMouseWheelMove();
    __declspec(dllexport) Vector2 Framework_GetMouseWheelMoveV();
    __declspec(dllexport) void Framework_SetMouseCursor(int cursor);

    // Cursor-related functions
    __declspec(dllexport) void Framework_ShowCursor();
    __declspec(dllexport) void Framework_HideCursor();
    __declspec(dllexport) bool Framework_IsCursorHidden();
    __declspec(dllexport) void Framework_EnableCursor();
    __declspec(dllexport) void Framework_DisableCursor();
    __declspec(dllexport) bool Framework_IsCursorOnScreen();

    // Timing-related functions
    __declspec(dllexport) void Framework_SetTargetFPS(int fps);
    __declspec(dllexport) float Framework_GetFrameTime();
    __declspec(dllexport) double Framework_GetTime();
    __declspec(dllexport) int Framework_GetFPS();

    // Collision detection functions
    __declspec(dllexport) bool Framework_CheckCollisionRecs(Rectangle rec1, Rectangle rec2);
    __declspec(dllexport) bool Framework_CheckCollisionCircles(Vector2 center1, float radius1, Vector2 center2, float radius2);
    __declspec(dllexport) bool Framework_CheckCollisionCircleRec(Vector2 center, float radius, Rectangle rec);
    __declspec(dllexport) bool Framework_CheckCollisionCircleLine(Vector2 center, float radius, Vector2 p1, Vector2 p2);
    __declspec(dllexport) bool Framework_CheckCollisionPointRec(Vector2 point, Rectangle rec);
    __declspec(dllexport) bool Framework_CheckCollisionPointCircle(Vector2 point, Vector2 center, float radius);
    __declspec(dllexport) bool Framework_CheckCollisionPointTriangle(Vector2 point, Vector2 p1, Vector2 p2, Vector2 p3);
    __declspec(dllexport) bool Framework_CheckCollisionPointLine(Vector2 point, Vector2 p1, Vector2 p2, int threshold);
    __declspec(dllexport) bool Framework_CheckCollisionPointPoly(Vector2 point, const Vector2 *points, int pointCount);
    __declspec(dllexport) bool Framework_CheckCollisionLines(Vector2 startPos1, Vector2 endPos1, Vector2 startPos2, Vector2 endPos2, Vector2 *collisionPoint);
    __declspec(dllexport) Rectangle Framework_GetCollisionRec(Rectangle rec1, Rectangle rec2);

    // Basic shapes drawing functions
    __declspec(dllexport) void Framework_DrawPixel(int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawPixelV(Vector2 position, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawLineV(Vector2 startPos, Vector2 endPos, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawLineEx(Vector2 startPos, Vector2 endPos, float thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawLineStrip(const Vector2 *points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawLineBezier(Vector2 startPos, Vector2 endPos, float thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircleSector(Vector2 center, float radius, float startAngle, float endAngle, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircleSectorLines(Vector2 center, float radius, float startAngle, float endAngle, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircleGradient(int centerX, int centerY, float radius, unsigned char rIn, unsigned char gIn, unsigned char bIn, unsigned char aIn, unsigned char rOut, unsigned char gOut, unsigned char bOut, unsigned char aOut);
    __declspec(dllexport) void Framework_DrawCircleV(Vector2 center, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircleLines(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawCircleLinesV(Vector2 center, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawEllipse(int centerX, int centerY, float radiusH, float radiusV, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawEllipseLines(int centerX, int centerY, float radiusH, float radiusV, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRing(Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRingLines(Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleV(Vector2 position, Vector2 size, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleRec(Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectanglePro(Rectangle rec, Vector2 origin, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleGradientV(int posX, int posY, int width, int height, unsigned char rTop, unsigned char gTop, unsigned char bTop, unsigned char aTop, unsigned char rBottom, unsigned char gBottom, unsigned char bBottom, unsigned char aBottom);
    __declspec(dllexport) void Framework_DrawRectangleGradientH(int posX, int posY, int width, int height, unsigned char rLeft, unsigned char gLeft, unsigned char bLeft, unsigned char aLeft, unsigned char rRight, unsigned char gRight, unsigned char bRight, unsigned char aRight);
    __declspec(dllexport) void Framework_DrawRectangleGradientEx(Rectangle rec, unsigned char rTL, unsigned char gTL, unsigned char bTL, unsigned char aTL, unsigned char rBL, unsigned char gBL, unsigned char bBL, unsigned char aBL, unsigned char rTR, unsigned char gTR, unsigned char bTR, unsigned char aTR, unsigned char rBR, unsigned char gBR, unsigned char bBR, unsigned char aBR);
    __declspec(dllexport) void Framework_DrawRectangleLines(int posX, int posY, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleLinesEx(Rectangle rec, float lineThick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleRounded(Rectangle rec, float roundness, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleRoundedLines(Rectangle rec, float roundness, int segments, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawRectangleRoundedLinesEx(Rectangle rec, float roundness, int segments, float lineThick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawTriangle(Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawTriangleLines(Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawTriangleFan(const Vector2 *points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawTriangleStrip(const Vector2 *points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawPoly(Vector2 center, int sides, float radius, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawPolyLines(Vector2 center, int sides, float radius, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void Framework_DrawPolyLinesEx(Vector2 center, int sides, float radius, float rotation, float lineThick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
}
