#pragma once
#include "pch.h"

// ============================================================================
// VISUAL GAME STUDIO ENGINE - Framework v1.0 / Engine v0.5
// Stable C ABI for VB.NET consumption via P/Invoke
// ============================================================================

// ============================================================================
// CONSTANTS
// ============================================================================
#define FW_NAME_MAX 64
#define FW_PATH_MAX 128
#define FW_TAG_MAX 32
#define FW_MAX_ENTITIES_QUERY 4096

// ============================================================================
// ENGINE STATE
// ============================================================================
enum EngineState {
    ENGINE_STOPPED = 0,
    ENGINE_RUNNING = 1,
    ENGINE_PAUSED = 2,
    ENGINE_QUITTING = 3
};

// ============================================================================
// COMPONENT TYPES (stable IDs for introspection)
// ============================================================================
enum ComponentType {
    COMP_NONE = 0,
    COMP_TRANSFORM2D = 1,
    COMP_SPRITE2D = 2,
    COMP_NAME = 3,
    COMP_TAG = 4,
    COMP_HIERARCHY = 5,
    COMP_VELOCITY2D = 6,
    COMP_BOXCOLLIDER2D = 7,
    COMP_ENABLED = 8,
    COMP_TILEMAP = 9,
    COMP_ANIMATOR = 10,
    COMP_PARTICLE_EMITTER = 11,
    COMP_COUNT // Keep last
};

// ============================================================================
// ANIMATION LOOP MODES
// ============================================================================
enum AnimLoopMode {
    ANIM_LOOP_NONE = 0,      // Play once and stop
    ANIM_LOOP_REPEAT = 1,    // Loop continuously
    ANIM_LOOP_PINGPONG = 2   // Play forward then backward
};

// ============================================================================
// UI ELEMENT TYPES
// ============================================================================
enum UIElementType {
    UI_LABEL = 0,
    UI_BUTTON = 1,
    UI_PANEL = 2,
    UI_SLIDER = 3,
    UI_CHECKBOX = 4,
    UI_TEXTINPUT = 5,
    UI_PROGRESSBAR = 6,
    UI_IMAGE = 7
};

// ============================================================================
// UI ANCHOR TYPES
// ============================================================================
enum UIAnchor {
    UI_ANCHOR_TOP_LEFT = 0,
    UI_ANCHOR_TOP_CENTER = 1,
    UI_ANCHOR_TOP_RIGHT = 2,
    UI_ANCHOR_CENTER_LEFT = 3,
    UI_ANCHOR_CENTER = 4,
    UI_ANCHOR_CENTER_RIGHT = 5,
    UI_ANCHOR_BOTTOM_LEFT = 6,
    UI_ANCHOR_BOTTOM_CENTER = 7,
    UI_ANCHOR_BOTTOM_RIGHT = 8
};

// ============================================================================
// UI ELEMENT STATE
// ============================================================================
enum UIState {
    UI_STATE_NORMAL = 0,
    UI_STATE_HOVERED = 1,
    UI_STATE_PRESSED = 2,
    UI_STATE_DISABLED = 3,
    UI_STATE_FOCUSED = 4
};

// ============================================================================
// CALLBACK TYPES
// ============================================================================
typedef void (*DrawCallback)();
typedef void (*SceneVoidFn)();
typedef void (*SceneUpdateFixedFn)(double dt);
typedef void (*SceneUpdateFrameFn)(float dt);

// UI Callbacks
typedef void (*UICallback)(int elementId);
typedef void (*UIValueCallback)(int elementId, float value);
typedef void (*UITextCallback)(int elementId, const char* text);

struct SceneCallbacks {
    SceneVoidFn         onEnter;
    SceneVoidFn         onExit;
    SceneVoidFn         onResume;
    SceneUpdateFixedFn  onUpdateFixed;
    SceneUpdateFrameFn  onUpdateFrame;
    SceneVoidFn         onDraw;
};

// ============================================================================
// ABI-SAFE STRUCTS FOR INTROSPECTION
// ============================================================================
struct Transform2DData {
    float posX, posY;
    float rotation;
    float scaleX, scaleY;
};

struct Velocity2DData {
    float vx, vy;
};

struct BoxCollider2DData {
    float offsetX, offsetY;
    float width, height;
    bool isTrigger;
};

// ============================================================================
// EXPORTED API
// ============================================================================
extern "C" {

    // ========================================================================
    // ENGINE STATE & LIFECYCLE
    // ========================================================================
    __declspec(dllexport) bool  Framework_Initialize(int width, int height, const char* title);
    __declspec(dllexport) void  Framework_Update();
    __declspec(dllexport) bool  Framework_ShouldClose();
    __declspec(dllexport) void  Framework_Shutdown();

    // State control
    __declspec(dllexport) int   Framework_GetState();          // Returns EngineState
    __declspec(dllexport) void  Framework_Pause();
    __declspec(dllexport) void  Framework_Resume();
    __declspec(dllexport) void  Framework_Quit();              // Request graceful shutdown
    __declspec(dllexport) bool  Framework_IsPaused();

    // ========================================================================
    // DRAW CONTROL
    // ========================================================================
    __declspec(dllexport) void  Framework_SetDrawCallback(DrawCallback callback);
    __declspec(dllexport) void  Framework_BeginDrawing();
    __declspec(dllexport) void  Framework_EndDrawing();
    __declspec(dllexport) void  Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ========================================================================
    // TIMING
    // ========================================================================
    __declspec(dllexport) void    Framework_SetTargetFPS(int fps);
    __declspec(dllexport) float   Framework_GetFrameTime();        // Raw frame time
    __declspec(dllexport) float   Framework_GetDeltaTime();        // Scaled frame time
    __declspec(dllexport) double  Framework_GetTime();
    __declspec(dllexport) int     Framework_GetFPS();
    __declspec(dllexport) unsigned long long Framework_GetFrameCount();

    // Time scale
    __declspec(dllexport) void    Framework_SetTimeScale(float scale);
    __declspec(dllexport) float   Framework_GetTimeScale();

    // Fixed timestep
    __declspec(dllexport) void    Framework_SetFixedStep(double seconds);
    __declspec(dllexport) void    Framework_ResetFixedClock();
    __declspec(dllexport) bool    Framework_StepFixed();
    __declspec(dllexport) double  Framework_GetFixedStep();
    __declspec(dllexport) double  Framework_GetAccumulator();

    // ========================================================================
    // INPUT - KEYBOARD
    // ========================================================================
    __declspec(dllexport) bool  Framework_IsKeyPressed(int key);
    __declspec(dllexport) bool  Framework_IsKeyPressedRepeat(int key);
    __declspec(dllexport) bool  Framework_IsKeyDown(int key);
    __declspec(dllexport) bool  Framework_IsKeyReleased(int key);
    __declspec(dllexport) bool  Framework_IsKeyUp(int key);
    __declspec(dllexport) int   Framework_GetKeyPressed();
    __declspec(dllexport) int   Framework_GetCharPressed();
    __declspec(dllexport) void  Framework_SetExitKey(int key);

    // ========================================================================
    // INPUT - MOUSE
    // ========================================================================
    __declspec(dllexport) int     Framework_GetMouseX();
    __declspec(dllexport) int     Framework_GetMouseY();
    __declspec(dllexport) bool    Framework_IsMouseButtonPressed(int button);
    __declspec(dllexport) bool    Framework_IsMouseButtonDown(int button);
    __declspec(dllexport) bool    Framework_IsMouseButtonReleased(int button);
    __declspec(dllexport) bool    Framework_IsMouseButtonUp(int button);
    __declspec(dllexport) Vector2 Framework_GetMousePosition();
    __declspec(dllexport) Vector2 Framework_GetMouseDelta();
    __declspec(dllexport) void    Framework_SetMousePosition(int x, int y);
    __declspec(dllexport) void    Framework_SetMouseOffset(int offsetX, int offsetY);
    __declspec(dllexport) void    Framework_SetMouseScale(float scaleX, float scaleY);
    __declspec(dllexport) float   Framework_GetMouseWheelMove();
    __declspec(dllexport) Vector2 Framework_GetMouseWheelMoveV();
    __declspec(dllexport) void    Framework_SetMouseCursor(int cursor);

    // Cursor control
    __declspec(dllexport) void  Framework_ShowCursor();
    __declspec(dllexport) void  Framework_HideCursor();
    __declspec(dllexport) bool  Framework_IsCursorHidden();
    __declspec(dllexport) void  Framework_EnableCursor();
    __declspec(dllexport) void  Framework_DisableCursor();
    __declspec(dllexport) bool  Framework_IsCursorOnScreen();

    // ========================================================================
    // SHAPES
    // ========================================================================
    __declspec(dllexport) void  Framework_DrawPixel(int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawLine(int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawCircleLines(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawRectangleLines(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ========================================================================
    // COLLISIONS
    // ========================================================================
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

    // ========================================================================
    // TEXTURES / IMAGES / RENDERING
    // ========================================================================
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
    __declspec(dllexport) void            Framework_UnloadRenderTexture(RenderTexture2D rt);
    __declspec(dllexport) bool            Framework_IsRenderTextureValid(RenderTexture2D rt);
    __declspec(dllexport) void            Framework_BeginTextureMode(RenderTexture2D rt);
    __declspec(dllexport) void            Framework_EndTextureMode();
    __declspec(dllexport) void            Framework_BeginMode2D(Camera2D cam);
    __declspec(dllexport) void            Framework_EndMode2D();

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

    // ========================================================================
    // CAMERA 2D (Managed)
    // ========================================================================
    __declspec(dllexport) void    Framework_Camera_SetPosition(float x, float y);
    __declspec(dllexport) void    Framework_Camera_SetTarget(float x, float y);
    __declspec(dllexport) void    Framework_Camera_SetRotation(float rotation);
    __declspec(dllexport) void    Framework_Camera_SetZoom(float zoom);
    __declspec(dllexport) void    Framework_Camera_SetOffset(float x, float y);
    __declspec(dllexport) Vector2 Framework_Camera_GetPosition();
    __declspec(dllexport) float   Framework_Camera_GetZoom();
    __declspec(dllexport) float   Framework_Camera_GetRotation();
    __declspec(dllexport) void    Framework_Camera_FollowEntity(int entity);
    __declspec(dllexport) void    Framework_Camera_BeginMode();
    __declspec(dllexport) void    Framework_Camera_EndMode();
    __declspec(dllexport) Vector2 Framework_Camera_ScreenToWorld(float screenX, float screenY);
    __declspec(dllexport) Vector2 Framework_Camera_WorldToScreen(float worldX, float worldY);

    // ========================================================================
    // AUDIO
    // ========================================================================
    __declspec(dllexport) bool  Framework_InitAudio();
    __declspec(dllexport) void  Framework_CloseAudio();
    __declspec(dllexport) void  Framework_SetMasterVolume(float volume);
    __declspec(dllexport) float Framework_GetMasterVolume();
    __declspec(dllexport) void  Framework_PauseAllAudio();
    __declspec(dllexport) void  Framework_ResumeAllAudio();

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

    // ========================================================================
    // SHADERS
    // ========================================================================
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

    // ========================================================================
    // ASSET CACHE (Handle-based)
    // ========================================================================
    __declspec(dllexport) void  Framework_SetAssetRoot(const char* path);
    __declspec(dllexport) const char* Framework_GetAssetRoot();

    // Textures
    __declspec(dllexport) int   Framework_AcquireTextureH(const char* path);
    __declspec(dllexport) void  Framework_ReleaseTextureH(int handle);
    __declspec(dllexport) bool  Framework_IsTextureValidH(int handle);
    __declspec(dllexport) void  Framework_DrawTextureH(int handle, int x, int y, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureVH(int handle, Vector2 pos, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureExH(int handle, Vector2 pos, float rotation, float scale, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureRecH(int handle, Rectangle src, Vector2 pos, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTextureProH(int handle, Rectangle src, Rectangle dst, Vector2 origin, float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) int   Framework_GetTextureWidth(int handle);
    __declspec(dllexport) int   Framework_GetTextureHeight(int handle);

    // Fonts
    __declspec(dllexport) int   Framework_AcquireFontH(const char* path, int fontSize);
    __declspec(dllexport) void  Framework_ReleaseFontH(int handle);
    __declspec(dllexport) bool  Framework_IsFontValidH(int handle);
    __declspec(dllexport) void  Framework_DrawTextExH(int handle, const char* text, Vector2 pos, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ========================================================================
    // SCENE SYSTEM
    // ========================================================================
    __declspec(dllexport) int   Framework_CreateScriptScene(SceneCallbacks cb);
    __declspec(dllexport) void  Framework_DestroyScene(int sceneHandle);
    __declspec(dllexport) void  Framework_SceneChange(int sceneHandle);
    __declspec(dllexport) void  Framework_ScenePush(int sceneHandle);
    __declspec(dllexport) void  Framework_ScenePop();
    __declspec(dllexport) bool  Framework_SceneHas();
    __declspec(dllexport) void  Framework_SceneTick();
    __declspec(dllexport) int   Framework_SceneGetCurrent();

    // ========================================================================
    // ECS - ENTITIES
    // ========================================================================
    __declspec(dllexport) int   Framework_Ecs_CreateEntity();
    __declspec(dllexport) void  Framework_Ecs_DestroyEntity(int entity);
    __declspec(dllexport) bool  Framework_Ecs_IsAlive(int entity);
    __declspec(dllexport) void  Framework_Ecs_ClearAll();
    __declspec(dllexport) int   Framework_Ecs_GetEntityCount();
    __declspec(dllexport) int   Framework_Ecs_GetAllEntities(int* buffer, int bufferSize);

    // ========================================================================
    // ECS - NAME COMPONENT
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_SetName(int entity, const char* name);
    __declspec(dllexport) const char* Framework_Ecs_GetName(int entity);
    __declspec(dllexport) bool  Framework_Ecs_HasName(int entity);
    __declspec(dllexport) int   Framework_Ecs_FindByName(const char* name);  // Returns first match or -1

    // ========================================================================
    // ECS - TAG COMPONENT
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_SetTag(int entity, const char* tag);
    __declspec(dllexport) const char* Framework_Ecs_GetTag(int entity);
    __declspec(dllexport) bool  Framework_Ecs_HasTag(int entity);
    __declspec(dllexport) int   Framework_Ecs_FindAllByTag(const char* tag, int* buffer, int bufferSize);

    // ========================================================================
    // ECS - ENABLED COMPONENT
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_SetEnabled(int entity, bool enabled);
    __declspec(dllexport) bool  Framework_Ecs_IsEnabled(int entity);
    __declspec(dllexport) bool  Framework_Ecs_IsActiveInHierarchy(int entity);  // Checks parent chain

    // ========================================================================
    // ECS - HIERARCHY COMPONENT
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_SetParent(int entity, int parent);  // -1 = root
    __declspec(dllexport) int   Framework_Ecs_GetParent(int entity);
    __declspec(dllexport) int   Framework_Ecs_GetFirstChild(int entity);
    __declspec(dllexport) int   Framework_Ecs_GetNextSibling(int entity);
    __declspec(dllexport) int   Framework_Ecs_GetChildCount(int entity);
    __declspec(dllexport) int   Framework_Ecs_GetChildren(int entity, int* buffer, int bufferSize);
    __declspec(dllexport) void  Framework_Ecs_DetachFromParent(int entity);

    // ========================================================================
    // ECS - TRANSFORM2D COMPONENT
    // ========================================================================
    __declspec(dllexport) void    Framework_Ecs_AddTransform2D(int entity, float x, float y, float rotation, float sx, float sy);
    __declspec(dllexport) bool    Framework_Ecs_HasTransform2D(int entity);
    __declspec(dllexport) void    Framework_Ecs_SetTransformPosition(int entity, float x, float y);
    __declspec(dllexport) void    Framework_Ecs_SetTransformRotation(int entity, float rotation);
    __declspec(dllexport) void    Framework_Ecs_SetTransformScale(int entity, float sx, float sy);
    __declspec(dllexport) Vector2 Framework_Ecs_GetTransformPosition(int entity);       // Local
    __declspec(dllexport) Vector2 Framework_Ecs_GetTransformScale(int entity);
    __declspec(dllexport) float   Framework_Ecs_GetTransformRotation(int entity);
    __declspec(dllexport) Vector2 Framework_Ecs_GetWorldPosition(int entity);           // World (hierarchical)
    __declspec(dllexport) float   Framework_Ecs_GetWorldRotation(int entity);
    __declspec(dllexport) Vector2 Framework_Ecs_GetWorldScale(int entity);

    // ========================================================================
    // ECS - VELOCITY2D COMPONENT
    // ========================================================================
    __declspec(dllexport) void    Framework_Ecs_AddVelocity2D(int entity, float vx, float vy);
    __declspec(dllexport) bool    Framework_Ecs_HasVelocity2D(int entity);
    __declspec(dllexport) void    Framework_Ecs_SetVelocity(int entity, float vx, float vy);
    __declspec(dllexport) Vector2 Framework_Ecs_GetVelocity(int entity);
    __declspec(dllexport) void    Framework_Ecs_RemoveVelocity2D(int entity);

    // ========================================================================
    // ECS - BOXCOLLIDER2D COMPONENT
    // ========================================================================
    __declspec(dllexport) void    Framework_Ecs_AddBoxCollider2D(int entity, float offsetX, float offsetY, float width, float height, bool isTrigger);
    __declspec(dllexport) bool    Framework_Ecs_HasBoxCollider2D(int entity);
    __declspec(dllexport) void    Framework_Ecs_SetBoxCollider(int entity, float offsetX, float offsetY, float width, float height);
    __declspec(dllexport) void    Framework_Ecs_SetBoxColliderTrigger(int entity, bool isTrigger);
    __declspec(dllexport) Rectangle Framework_Ecs_GetBoxColliderWorldBounds(int entity);
    __declspec(dllexport) void    Framework_Ecs_RemoveBoxCollider2D(int entity);

    // ========================================================================
    // ECS - SPRITE2D COMPONENT
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_AddSprite2D(int entity, int textureHandle,
        float srcX, float srcY, float srcW, float srcH,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a,
        int layer);
    __declspec(dllexport) bool  Framework_Ecs_HasSprite2D(int entity);
    __declspec(dllexport) void  Framework_Ecs_SetSpriteTint(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_Ecs_SetSpriteVisible(int entity, bool visible);
    __declspec(dllexport) void  Framework_Ecs_SetSpriteLayer(int entity, int layer);
    __declspec(dllexport) void  Framework_Ecs_SetSpriteSource(int entity, float srcX, float srcY, float srcW, float srcH);
    __declspec(dllexport) void  Framework_Ecs_SetSpriteTexture(int entity, int textureHandle);
    __declspec(dllexport) void  Framework_Ecs_RemoveSprite2D(int entity);

    // ========================================================================
    // ECS - SYSTEMS
    // ========================================================================
    __declspec(dllexport) void  Framework_Ecs_UpdateVelocities(float dt);  // Apply velocity to transforms
    __declspec(dllexport) void  Framework_Ecs_DrawSprites();               // Render all sprites

    // ========================================================================
    // PHYSICS - OVERLAP QUERIES
    // ========================================================================
    __declspec(dllexport) int   Framework_Physics_OverlapBox(float x, float y, float w, float h, int* buffer, int bufferSize);
    __declspec(dllexport) int   Framework_Physics_OverlapCircle(float x, float y, float radius, int* buffer, int bufferSize);
    __declspec(dllexport) bool  Framework_Physics_CheckEntityOverlap(int entityA, int entityB);
    __declspec(dllexport) int   Framework_Physics_GetOverlappingEntities(int entity, int* buffer, int bufferSize);

    // ========================================================================
    // INTROSPECTION (Editor-ready)
    // ========================================================================
    __declspec(dllexport) int   Framework_Entity_GetComponentCount(int entity);
    __declspec(dllexport) int   Framework_Entity_GetComponentTypeAt(int entity, int index);
    __declspec(dllexport) bool  Framework_Entity_HasComponent(int entity, int compType);

    // Component field info (static, doesn't need entity)
    __declspec(dllexport) int   Framework_Component_GetFieldCount(int compType);
    __declspec(dllexport) const char* Framework_Component_GetFieldName(int compType, int fieldIndex);
    __declspec(dllexport) int   Framework_Component_GetFieldType(int compType, int fieldIndex);  // 0=float, 1=int, 2=bool, 3=string

    // Field get/set (by entity + component type + field index)
    __declspec(dllexport) float Framework_Component_GetFieldFloat(int entity, int compType, int fieldIndex);
    __declspec(dllexport) int   Framework_Component_GetFieldInt(int entity, int compType, int fieldIndex);
    __declspec(dllexport) bool  Framework_Component_GetFieldBool(int entity, int compType, int fieldIndex);
    __declspec(dllexport) const char* Framework_Component_GetFieldString(int entity, int compType, int fieldIndex);
    __declspec(dllexport) void  Framework_Component_SetFieldFloat(int entity, int compType, int fieldIndex, float value);
    __declspec(dllexport) void  Framework_Component_SetFieldInt(int entity, int compType, int fieldIndex, int value);
    __declspec(dllexport) void  Framework_Component_SetFieldBool(int entity, int compType, int fieldIndex, bool value);
    __declspec(dllexport) void  Framework_Component_SetFieldString(int entity, int compType, int fieldIndex, const char* value);

    // ========================================================================
    // DEBUG OVERLAY
    // ========================================================================
    __declspec(dllexport) void  Framework_Debug_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Debug_IsEnabled();
    __declspec(dllexport) void  Framework_Debug_DrawEntityBounds(bool enabled);
    __declspec(dllexport) void  Framework_Debug_DrawHierarchy(bool enabled);
    __declspec(dllexport) void  Framework_Debug_DrawStats(bool enabled);
    __declspec(dllexport) void  Framework_Debug_Render();  // Call after scene draw

    // ========================================================================
    // PREFABS & SERIALIZATION
    // ========================================================================
    __declspec(dllexport) bool  Framework_Scene_Save(const char* path);
    __declspec(dllexport) bool  Framework_Scene_Load(const char* path);
    __declspec(dllexport) int   Framework_Prefab_Load(const char* path);       // Returns prefab handle
    __declspec(dllexport) int   Framework_Prefab_Instantiate(int prefabH, int parentEntity, float x, float y);  // Returns root entity
    __declspec(dllexport) void  Framework_Prefab_Unload(int prefabH);
    __declspec(dllexport) bool  Framework_Prefab_SaveEntity(int entity, const char* path);  // Save entity subtree as prefab

    // ========================================================================
    // TILEMAP SYSTEM
    // ========================================================================
    // Tileset management (shared across tilemaps)
    __declspec(dllexport) int   Framework_Tileset_Create(int textureHandle, int tileWidth, int tileHeight, int columns);
    __declspec(dllexport) void  Framework_Tileset_Destroy(int tilesetHandle);
    __declspec(dllexport) bool  Framework_Tileset_IsValid(int tilesetHandle);
    __declspec(dllexport) int   Framework_Tileset_GetTileWidth(int tilesetHandle);
    __declspec(dllexport) int   Framework_Tileset_GetTileHeight(int tilesetHandle);

    // Tilemap component
    __declspec(dllexport) void  Framework_Ecs_AddTilemap(int entity, int tilesetHandle, int mapWidth, int mapHeight);
    __declspec(dllexport) bool  Framework_Ecs_HasTilemap(int entity);
    __declspec(dllexport) void  Framework_Ecs_RemoveTilemap(int entity);
    __declspec(dllexport) void  Framework_Ecs_SetTile(int entity, int x, int y, int tileIndex);  // -1 = empty
    __declspec(dllexport) int   Framework_Ecs_GetTile(int entity, int x, int y);
    __declspec(dllexport) void  Framework_Ecs_FillTiles(int entity, int tileIndex);  // Fill all with same tile
    __declspec(dllexport) void  Framework_Ecs_SetTileCollision(int entity, int tileIndex, bool solid);
    __declspec(dllexport) bool  Framework_Ecs_GetTileCollision(int entity, int tileIndex);
    __declspec(dllexport) int   Framework_Ecs_GetTilemapWidth(int entity);   // In tiles
    __declspec(dllexport) int   Framework_Ecs_GetTilemapHeight(int entity);  // In tiles
    __declspec(dllexport) void  Framework_Ecs_DrawTilemap(int entity);       // Draw single tilemap
    __declspec(dllexport) void  Framework_Tilemaps_Draw();                   // Draw all tilemaps

    // Tilemap collision queries
    __declspec(dllexport) bool  Framework_Tilemap_PointSolid(int entity, float worldX, float worldY);
    __declspec(dllexport) bool  Framework_Tilemap_BoxSolid(int entity, float worldX, float worldY, float w, float h);

    // ========================================================================
    // ANIMATION SYSTEM
    // ========================================================================
    // Animation clip management (reusable animation data)
    __declspec(dllexport) int   Framework_AnimClip_Create(const char* name, int frameCount);
    __declspec(dllexport) void  Framework_AnimClip_Destroy(int clipHandle);
    __declspec(dllexport) bool  Framework_AnimClip_IsValid(int clipHandle);
    __declspec(dllexport) void  Framework_AnimClip_SetFrame(int clipHandle, int frameIndex,
        float srcX, float srcY, float srcW, float srcH, float duration);
    __declspec(dllexport) void  Framework_AnimClip_SetLoopMode(int clipHandle, int loopMode);  // AnimLoopMode
    __declspec(dllexport) int   Framework_AnimClip_GetFrameCount(int clipHandle);
    __declspec(dllexport) float Framework_AnimClip_GetTotalDuration(int clipHandle);
    __declspec(dllexport) int   Framework_AnimClip_FindByName(const char* name);

    // Animator component
    __declspec(dllexport) void  Framework_Ecs_AddAnimator(int entity);
    __declspec(dllexport) bool  Framework_Ecs_HasAnimator(int entity);
    __declspec(dllexport) void  Framework_Ecs_RemoveAnimator(int entity);
    __declspec(dllexport) void  Framework_Ecs_SetAnimatorClip(int entity, int clipHandle);
    __declspec(dllexport) int   Framework_Ecs_GetAnimatorClip(int entity);
    __declspec(dllexport) void  Framework_Ecs_AnimatorPlay(int entity);
    __declspec(dllexport) void  Framework_Ecs_AnimatorPause(int entity);
    __declspec(dllexport) void  Framework_Ecs_AnimatorStop(int entity);  // Stop and reset to frame 0
    __declspec(dllexport) void  Framework_Ecs_AnimatorSetSpeed(int entity, float speed);  // 1.0 = normal
    __declspec(dllexport) bool  Framework_Ecs_AnimatorIsPlaying(int entity);
    __declspec(dllexport) int   Framework_Ecs_AnimatorGetFrame(int entity);
    __declspec(dllexport) void  Framework_Ecs_AnimatorSetFrame(int entity, int frameIndex);
    __declspec(dllexport) void  Framework_Animators_Update(float dt);  // Update all animators

    // ========================================================================
    // PARTICLE SYSTEM
    // ========================================================================
    // Particle emitter component
    __declspec(dllexport) void  Framework_Ecs_AddParticleEmitter(int entity, int textureHandle);
    __declspec(dllexport) bool  Framework_Ecs_HasParticleEmitter(int entity);
    __declspec(dllexport) void  Framework_Ecs_RemoveParticleEmitter(int entity);

    // Emitter configuration
    __declspec(dllexport) void  Framework_Ecs_SetEmitterRate(int entity, float particlesPerSecond);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterLifetime(int entity, float minLife, float maxLife);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterVelocity(int entity, float minVx, float minVy, float maxVx, float maxVy);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterColorStart(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterColorEnd(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterSize(int entity, float startSize, float endSize);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterGravity(int entity, float gx, float gy);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterSpread(int entity, float angleDegrees);  // Cone angle
    __declspec(dllexport) void  Framework_Ecs_SetEmitterDirection(int entity, float dirX, float dirY);  // Base direction
    __declspec(dllexport) void  Framework_Ecs_SetEmitterMaxParticles(int entity, int maxParticles);
    __declspec(dllexport) void  Framework_Ecs_SetEmitterSourceRect(int entity, float srcX, float srcY, float srcW, float srcH);

    // Emitter control
    __declspec(dllexport) void  Framework_Ecs_EmitterStart(int entity);
    __declspec(dllexport) void  Framework_Ecs_EmitterStop(int entity);
    __declspec(dllexport) void  Framework_Ecs_EmitterBurst(int entity, int count);  // Emit burst of particles
    __declspec(dllexport) bool  Framework_Ecs_EmitterIsActive(int entity);
    __declspec(dllexport) int   Framework_Ecs_EmitterGetParticleCount(int entity);  // Current active particles
    __declspec(dllexport) void  Framework_Ecs_EmitterClear(int entity);  // Kill all particles

    // Particle systems update/draw
    __declspec(dllexport) void  Framework_Particles_Update(float dt);  // Update all emitters
    __declspec(dllexport) void  Framework_Particles_Draw();            // Draw all particles

    // ========================================================================
    // UI SYSTEM
    // ========================================================================
    // UI Element lifecycle
    __declspec(dllexport) int   Framework_UI_CreateLabel(const char* text, float x, float y);
    __declspec(dllexport) int   Framework_UI_CreateButton(const char* text, float x, float y, float width, float height);
    __declspec(dllexport) int   Framework_UI_CreatePanel(float x, float y, float width, float height);
    __declspec(dllexport) int   Framework_UI_CreateSlider(float x, float y, float width, float minVal, float maxVal, float initialVal);
    __declspec(dllexport) int   Framework_UI_CreateCheckbox(const char* text, float x, float y, bool initialState);
    __declspec(dllexport) int   Framework_UI_CreateTextInput(float x, float y, float width, float height, const char* placeholder);
    __declspec(dllexport) int   Framework_UI_CreateProgressBar(float x, float y, float width, float height, float initialValue);
    __declspec(dllexport) int   Framework_UI_CreateImage(int textureHandle, float x, float y, float width, float height);
    __declspec(dllexport) void  Framework_UI_Destroy(int elementId);
    __declspec(dllexport) void  Framework_UI_DestroyAll();
    __declspec(dllexport) bool  Framework_UI_IsValid(int elementId);

    // UI Element properties - Common
    __declspec(dllexport) void  Framework_UI_SetPosition(int elementId, float x, float y);
    __declspec(dllexport) void  Framework_UI_SetSize(int elementId, float width, float height);
    __declspec(dllexport) void  Framework_UI_SetAnchor(int elementId, int anchor);  // UIAnchor enum
    __declspec(dllexport) void  Framework_UI_SetVisible(int elementId, bool visible);
    __declspec(dllexport) void  Framework_UI_SetEnabled(int elementId, bool enabled);
    __declspec(dllexport) void  Framework_UI_SetParent(int elementId, int parentId);  // -1 for no parent
    __declspec(dllexport) void  Framework_UI_SetLayer(int elementId, int layer);  // Higher = drawn on top
    __declspec(dllexport) float Framework_UI_GetX(int elementId);
    __declspec(dllexport) float Framework_UI_GetY(int elementId);
    __declspec(dllexport) float Framework_UI_GetWidth(int elementId);
    __declspec(dllexport) float Framework_UI_GetHeight(int elementId);
    __declspec(dllexport) int   Framework_UI_GetState(int elementId);  // UIState enum
    __declspec(dllexport) int   Framework_UI_GetType(int elementId);   // UIElementType enum
    __declspec(dllexport) bool  Framework_UI_IsVisible(int elementId);
    __declspec(dllexport) bool  Framework_UI_IsEnabled(int elementId);

    // UI Element properties - Text/Font
    __declspec(dllexport) void  Framework_UI_SetText(int elementId, const char* text);
    __declspec(dllexport) const char* Framework_UI_GetText(int elementId);
    __declspec(dllexport) void  Framework_UI_SetFont(int elementId, int fontHandle);
    __declspec(dllexport) void  Framework_UI_SetFontSize(int elementId, float size);
    __declspec(dllexport) void  Framework_UI_SetTextColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetTextAlign(int elementId, int anchor);  // Use UIAnchor for alignment

    // UI Element properties - Colors
    __declspec(dllexport) void  Framework_UI_SetBackgroundColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetBorderColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetHoverColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetPressedColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetDisabledColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_UI_SetBorderWidth(int elementId, float width);
    __declspec(dllexport) void  Framework_UI_SetCornerRadius(int elementId, float radius);
    __declspec(dllexport) void  Framework_UI_SetPadding(int elementId, float left, float top, float right, float bottom);

    // UI Element properties - Value-based (Slider, ProgressBar, Checkbox)
    __declspec(dllexport) void  Framework_UI_SetValue(int elementId, float value);
    __declspec(dllexport) float Framework_UI_GetValue(int elementId);
    __declspec(dllexport) void  Framework_UI_SetMinMax(int elementId, float minVal, float maxVal);
    __declspec(dllexport) void  Framework_UI_SetChecked(int elementId, bool checked);
    __declspec(dllexport) bool  Framework_UI_IsChecked(int elementId);

    // UI Element properties - TextInput specific
    __declspec(dllexport) void  Framework_UI_SetPlaceholder(int elementId, const char* text);
    __declspec(dllexport) void  Framework_UI_SetMaxLength(int elementId, int maxLength);
    __declspec(dllexport) void  Framework_UI_SetPasswordMode(int elementId, bool isPassword);
    __declspec(dllexport) void  Framework_UI_SetCursorPosition(int elementId, int position);
    __declspec(dllexport) int   Framework_UI_GetCursorPosition(int elementId);

    // UI Element properties - Image specific
    __declspec(dllexport) void  Framework_UI_SetTexture(int elementId, int textureHandle);
    __declspec(dllexport) void  Framework_UI_SetSourceRect(int elementId, float srcX, float srcY, float srcW, float srcH);
    __declspec(dllexport) void  Framework_UI_SetTint(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // UI Callbacks
    __declspec(dllexport) void  Framework_UI_SetClickCallback(int elementId, UICallback callback);
    __declspec(dllexport) void  Framework_UI_SetHoverCallback(int elementId, UICallback callback);
    __declspec(dllexport) void  Framework_UI_SetValueChangedCallback(int elementId, UIValueCallback callback);
    __declspec(dllexport) void  Framework_UI_SetTextChangedCallback(int elementId, UITextCallback callback);

    // UI System update/draw
    __declspec(dllexport) void  Framework_UI_Update();       // Process input, update states
    __declspec(dllexport) void  Framework_UI_Draw();         // Draw all visible UI elements
    __declspec(dllexport) int   Framework_UI_GetHovered();   // Returns element under mouse, -1 if none
    __declspec(dllexport) int   Framework_UI_GetFocused();   // Returns focused element, -1 if none
    __declspec(dllexport) void  Framework_UI_SetFocus(int elementId);  // -1 to clear focus
    __declspec(dllexport) bool  Framework_UI_HasFocus();     // True if any UI element has focus

    // UI Layout helpers
    __declspec(dllexport) void  Framework_UI_LayoutVertical(int parentId, float spacing, float paddingX, float paddingY);
    __declspec(dllexport) void  Framework_UI_LayoutHorizontal(int parentId, float spacing, float paddingX, float paddingY);

    // ========================================================================
    // CLEANUP
    // ========================================================================
    __declspec(dllexport) void  Framework_ResourcesShutdown();

} // extern "C"
