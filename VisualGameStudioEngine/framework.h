#pragma once
#include "pch.h"

// Callback types
typedef void (*DrawCallback)();

typedef void (*SceneVoidFn)();                   // OnEnter, OnExit, OnResume, OnDraw
typedef void (*SceneUpdateFixedFn)(double dt);   // Fixed step
typedef void (*SceneUpdateFrameFn)(float dt);    // Per-frame

struct SceneCallbacks {
    SceneVoidFn         onEnter;
    SceneVoidFn         onExit;
    SceneVoidFn         onResume;
    SceneUpdateFixedFn  onUpdateFixed;
    SceneUpdateFrameFn  onUpdateFrame;
    SceneVoidFn         onDraw;
};

extern "C" {

    // =======================
    // Window / App lifecycle
    // =======================
    __declspec(dllexport) bool  Framework_Initialize(int width, int height, const char* title);
    __declspec(dllexport) void  Framework_Update();
    __declspec(dllexport) bool  Framework_ShouldClose();
    __declspec(dllexport) void  Framework_Shutdown();

    // ===============
    // Draw callback
    // ===============
    __declspec(dllexport) void  Framework_SetDrawCallback(DrawCallback callback);
    __declspec(dllexport) void  Framework_BeginDrawing();
    __declspec(dllexport) void  Framework_EndDrawing();
    __declspec(dllexport) void  Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // =======
    // Input
    // =======
    // Keyboard
    __declspec(dllexport) bool  Framework_IsKeyPressed(int key);
    __declspec(dllexport) bool  Framework_IsKeyPressedRepeat(int key);
    __declspec(dllexport) bool  Framework_IsKeyDown(int key);
    __declspec(dllexport) bool  Framework_IsKeyReleased(int key);
    __declspec(dllexport) bool  Framework_IsKeyUp(int key);
    __declspec(dllexport) int   Framework_GetKeyPressed();
    __declspec(dllexport) int   Framework_GetCharPressed();
    __declspec(dllexport) void  Framework_SetExitKey(int key);

    // Mouse
    __declspec(dllexport) int   Framework_GetMouseX();
    __declspec(dllexport) int   Framework_GetMouseY();
    __declspec(dllexport) bool  Framework_IsMouseButtonPressed(int button);
    __declspec(dllexport) bool  Framework_IsMouseButtonDown(int button);
    __declspec(dllexport) bool  Framework_IsMouseButtonReleased(int button);
    __declspec(dllexport) bool  Framework_IsMouseButtonUp(int button);
    __declspec(dllexport) Vector2 Framework_GetMousePosition();
    __declspec(dllexport) Vector2 Framework_GetMouseDelta();
    __declspec(dllexport) void  Framework_SetMousePosition(int x, int y);
    __declspec(dllexport) void  Framework_SetMouseOffset(int offsetX, int offsetY);
    __declspec(dllexport) void  Framework_SetMouseScale(float scaleX, float scaleY);
    __declspec(dllexport) float Framework_GetMouseWheelMove();
    __declspec(dllexport) Vector2 Framework_GetMouseWheelMoveV();
    __declspec(dllexport) void  Framework_SetMouseCursor(int cursor);

    // Cursor control
    __declspec(dllexport) void  Framework_ShowCursor();
    __declspec(dllexport) void  Framework_HideCursor();
    __declspec(dllexport) bool  Framework_IsCursorHidden();
    __declspec(dllexport) void  Framework_EnableCursor();
    __declspec(dllexport) void  Framework_DisableCursor();
    __declspec(dllexport) bool  Framework_IsCursorOnScreen();

    // ========
    // Timing
    // ========
    __declspec(dllexport) void   Framework_SetTargetFPS(int fps);
    __declspec(dllexport) float  Framework_GetFrameTime();
    __declspec(dllexport) double Framework_GetTime();
    __declspec(dllexport) int    Framework_GetFPS();

    // Fixed-step helpers
    __declspec(dllexport) void   Framework_SetFixedStep(double seconds);
    __declspec(dllexport) void   Framework_ResetFixedClock();
    __declspec(dllexport) bool   Framework_StepFixed();
    __declspec(dllexport) double Framework_GetFixedStep();
    __declspec(dllexport) double Framework_GetAccumulator();

    // ========
    // Shapes
    // ========
    // Only the shapes actually implemented in framework.cpp are exposed here.
    __declspec(dllexport) void  Framework_DrawPixel(int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ============
    // Collisions
    // ============
    __declspec(dllexport) bool      Framework_CheckCollisionRecs(Rectangle rec1, Rectangle rec2);
    __declspec(dllexport) bool      Framework_CheckCollisionCircles(Vector2 center1, float radius1, Vector2 center2, float radius2);
    __declspec(dllexport) bool      Framework_CheckCollisionCircleRec(Vector2 center, float radius, Rectangle rec);
    __declspec(dllexport) bool      Framework_CheckCollisionCircleLine(Vector2 center, float radius, Vector2 p1, Vector2 p2);
    __declspec(dllexport) bool      Framework_CheckCollisionPointRec(Vector2 point, Rectangle rec);
    __declspec(dllexport) bool      Framework_CheckCollisionPointCircle(Vector2 point, Vector2 center, float radius);
    __declspec(dllexport) bool      Framework_CheckCollisionPointTriangle(Vector2 point, Vector2 p1, Vector2 p2, Vector2 p3);
    __declspec(dllexport) bool      Framework_CheckCollisionPointLine(Vector2 point, Vector2 p1, Vector2 p2, int threshold);
    __declspec(dllexport) bool      Framework_CheckCollisionPointPoly(Vector2 point, const Vector2* points, int pointCount);
    __declspec(dllexport) bool      Framework_CheckCollisionLines(Vector2 startPos1, Vector2 endPos1, Vector2 startPos2, Vector2 endPos2, Vector2* collisionPoint);
    __declspec(dllexport) Rectangle Framework_GetCollisionRec(Rectangle rec1, Rectangle rec2);

    // =============================
    // Textures / Images / Rendering
    // =============================
    // Textures
    __declspec(dllexport) Texture2D Framework_LoadTexture(const char* fileName);
    __declspec(dllexport) void      Framework_UnloadTexture(Texture2D texture);
    __declspec(dllexport) bool      Framework_IsTextureValid(Texture2D texture);

    __declspec(dllexport) void      Framework_UpdateTexture(Texture2D texture, const void* pixels);
    __declspec(dllexport) void      Framework_UpdateTextureRec(Texture2D texture, Rectangle rec, const void* pixels);
    __declspec(dllexport) void      Framework_GenTextureMipmaps(Texture2D* tex);
    __declspec(dllexport) void      Framework_SetTextureFilter(Texture2D tex, int filter);
    __declspec(dllexport) void      Framework_SetTextureWrap(Texture2D tex, int wrap);

    // Texture drawing
    __declspec(dllexport) void      Framework_DrawTexture(Texture2D texture, int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void      Framework_DrawTextureV(Texture2D texture, Vector2 position, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void      Framework_DrawTextureEx(Texture2D texture, Vector2 position, float rotation, float scale, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void      Framework_DrawTextureRec(Texture2D texture, Rectangle source, Vector2 position, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void      Framework_DrawTexturePro(Texture2D texture, Rectangle source, Rectangle dest, Vector2 origin, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void      Framework_DrawTextureNPatch(Texture2D texture, NPatchInfo nPatchInfo, Rectangle dest, Vector2 origin, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Render textures & 2D camera
    __declspec(dllexport) RenderTexture2D Framework_LoadRenderTexture(int w, int h);
    __declspec(dllexport) void           Framework_UnloadRenderTexture(RenderTexture2D rt);
    __declspec(dllexport) bool           Framework_IsRenderTextureValid(RenderTexture2D rt);
    __declspec(dllexport) void           Framework_BeginTextureMode(RenderTexture2D rt);
    __declspec(dllexport) void           Framework_EndTextureMode();
    __declspec(dllexport) void           Framework_BeginMode2D(Camera2D cam);
    __declspec(dllexport) void           Framework_EndMode2D();

    // Images
    __declspec(dllexport) Image     Framework_LoadImage(const char* fileName);
    __declspec(dllexport) void      Framework_UnloadImage(Image img);
    __declspec(dllexport) void      Framework_ImageColorInvert(Image* img);
    __declspec(dllexport) void      Framework_ImageResize(Image* img, int w, int h);
    __declspec(dllexport) void      Framework_ImageFlipVertical(Image* img);

    // Fonts / advanced text
    __declspec(dllexport) Font      Framework_LoadFontEx(const char* fileName, int fontSize, int* glyphs, int glyphCount);
    __declspec(dllexport) void      Framework_UnloadFont(Font font);
    __declspec(dllexport) void      Framework_DrawTextEx(Font font, const char* text, Vector2 pos, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Helpers & debug
    __declspec(dllexport) Rectangle Framework_SpriteFrame(Rectangle sheetArea, int frameW, int frameH, int index, int columns);
    __declspec(dllexport) void      Framework_DrawFPS(int x, int y);
    __declspec(dllexport) void      Framework_DrawGrid(int slices, float spacing);

    // =====
    // Audio
    // =====
    __declspec(dllexport) bool  Framework_InitAudio();
    __declspec(dllexport) void  Framework_CloseAudio();

    // Sounds (handle-based)
    __declspec(dllexport) int   Framework_LoadSoundH(const char* file);
    __declspec(dllexport) void  Framework_UnloadSoundH(int h);
    __declspec(dllexport) void  Framework_PlaySoundH(int h);
    __declspec(dllexport) void  Framework_StopSoundH(int h);
    __declspec(dllexport) void  Framework_PauseSoundH(int h);
    __declspec(dllexport) void  Framework_ResumeSoundH(int h);
    __declspec(dllexport) void  Framework_SetSoundVolumeH(int h, float v);
    __declspec(dllexport) void  Framework_SetSoundPitchH(int h, float p);
    __declspec(dllexport) void  Framework_SetSoundPanH(int h, float pan);

    // Music (handle-based streaming cache)
    __declspec(dllexport) int   Framework_AcquireMusicH(const char* path);
    __declspec(dllexport) void  Framework_ReleaseMusicH(int handle);
    __declspec(dllexport) bool  Framework_IsMusicValidH(int handle);
    __declspec(dllexport) void  Framework_PlayMusicH(int handle);
    __declspec(dllexport) void  Framework_StopMusicH(int handle);
    __declspec(dllexport) void  Framework_PauseMusicH(int handle);
    __declspec(dllexport) void  Framework_ResumeMusicH(int handle);
    __declspec(dllexport) void  Framework_SetMusicVolumeH(int handle, float v);
    __declspec(dllexport) void  Framework_SetMusicPitchH(int handle, float p);
    __declspec(dllexport) void  Framework_UpdateMusicH(int handle);
    __declspec(dllexport) void  Framework_UpdateAllMusic();

    // ========
    // Shaders
    // ========
    __declspec(dllexport) Shader Framework_LoadShaderF(const char* vsPath, const char* fsPath);
    __declspec(dllexport) void   Framework_UnloadShader(Shader sh);
    __declspec(dllexport) void   Framework_BeginShaderMode(Shader sh);
    __declspec(dllexport) void   Framework_EndShaderMode();
    __declspec(dllexport) int    Framework_GetShaderLocation(Shader sh, const char* name);
    __declspec(dllexport) void   Framework_SetShaderValue1f(Shader sh, int loc, float v);
    __declspec(dllexport) void   Framework_SetShaderValue2f(Shader sh, int loc, float x, float y);
    __declspec(dllexport) void   Framework_SetShaderValue3f(Shader sh, int loc, float x, float y, float z);
    __declspec(dllexport) void   Framework_SetShaderValue4f(Shader sh, int loc, float x, float y, float z, float w);
    __declspec(dllexport) void   Framework_SetShaderValue1i(Shader sh, int loc, int v);

    // =========================
    // Texture / Font cache API
    // =========================
    // Textures (handle-based)
    __declspec(dllexport) int   Framework_AcquireTextureH(const char* path);
    __declspec(dllexport) void  Framework_ReleaseTextureH(int handle);
    __declspec(dllexport) bool  Framework_IsTextureValidH(int handle);
    __declspec(dllexport) void  Framework_DrawTextureH(int handle, int x, int y, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureVH(int handle, Vector2 pos, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureExH(int handle, Vector2 pos, float rotation, float scale, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureRecH(int handle, Rectangle src, Vector2 pos, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureProH(int handle, Rectangle src, Rectangle dst, Vector2 origin, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Fonts (handle-based)
    __declspec(dllexport) int   Framework_AcquireFontH(const char* path, int fontSize);
    __declspec(dllexport) void  Framework_ReleaseFontH(int handle);
    __declspec(dllexport) bool  Framework_IsFontValidH(int handle);
    __declspec(dllexport) void  Framework_DrawTextExH(int handle, const char* text, Vector2 pos, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ===============
    // Scene system
    // ===============
    __declspec(dllexport) int   Framework_CreateScriptScene(SceneCallbacks cb);
    __declspec(dllexport) void  Framework_DestroyScene(int sceneHandle);
    __declspec(dllexport) void  Framework_SceneChange(int sceneHandle);
    __declspec(dllexport) void  Framework_ScenePush(int sceneHandle);
    __declspec(dllexport) void  Framework_ScenePop();
    __declspec(dllexport) bool  Framework_SceneHas();
    __declspec(dllexport) void  Framework_SceneTick();

    // ========
    // Cleanup
    // ========
    __declspec(dllexport) void  Framework_ResourcesShutdown();

} // extern "C"
