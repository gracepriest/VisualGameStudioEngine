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
// PHYSICS BODY TYPES
// ============================================================================
enum PhysicsBodyType {
    BODY_STATIC = 0,     // Never moves (walls, platforms)
    BODY_DYNAMIC = 1,    // Fully simulated (players, objects)
    BODY_KINEMATIC = 2   // Moved by code, affects dynamics (moving platforms)
};

// ============================================================================
// COLLISION SHAPE TYPES
// ============================================================================
enum CollisionShapeType {
    SHAPE_CIRCLE = 0,
    SHAPE_BOX = 1,       // AABB
    SHAPE_POLYGON = 2
};

// ============================================================================
// AUDIO GROUPS
// ============================================================================
enum AudioGroup {
    AUDIO_GROUP_MASTER = 0,
    AUDIO_GROUP_MUSIC = 1,
    AUDIO_GROUP_SFX = 2,
    AUDIO_GROUP_VOICE = 3,
    AUDIO_GROUP_AMBIENT = 4,
    AUDIO_GROUP_COUNT
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
// SCENE TRANSITION TYPES
// ============================================================================
enum SceneTransitionType {
    TRANSITION_NONE = 0,
    TRANSITION_FADE = 1,           // Fade to black then fade in
    TRANSITION_FADE_WHITE = 2,     // Fade to white then fade in
    TRANSITION_SLIDE_LEFT = 3,     // New scene slides in from right
    TRANSITION_SLIDE_RIGHT = 4,    // New scene slides in from left
    TRANSITION_SLIDE_UP = 5,       // New scene slides in from bottom
    TRANSITION_SLIDE_DOWN = 6,     // New scene slides in from top
    TRANSITION_WIPE_LEFT = 7,      // Wipe effect from right to left
    TRANSITION_WIPE_RIGHT = 8,     // Wipe effect from left to right
    TRANSITION_WIPE_UP = 9,        // Wipe effect from bottom to top
    TRANSITION_WIPE_DOWN = 10,     // Wipe effect from top to bottom
    TRANSITION_CIRCLE_IN = 11,     // Circular iris close
    TRANSITION_CIRCLE_OUT = 12,    // Circular iris open
    TRANSITION_PIXELATE = 13,      // Pixelation effect
    TRANSITION_DISSOLVE = 14       // Random dissolve
};

// ============================================================================
// SCENE TRANSITION EASING
// ============================================================================
enum TransitionEasing {
    EASE_LINEAR = 0,
    EASE_IN_QUAD = 1,
    EASE_OUT_QUAD = 2,
    EASE_IN_OUT_QUAD = 3,
    EASE_IN_CUBIC = 4,
    EASE_OUT_CUBIC = 5,
    EASE_IN_OUT_CUBIC = 6,
    EASE_IN_EXPO = 7,
    EASE_OUT_EXPO = 8,
    EASE_IN_OUT_EXPO = 9
};

// ============================================================================
// SCENE TRANSITION STATE
// ============================================================================
enum TransitionState {
    TRANS_STATE_NONE = 0,
    TRANS_STATE_OUT = 1,      // Transitioning out of current scene
    TRANS_STATE_LOADING = 2,  // Loading screen active
    TRANS_STATE_IN = 3        // Transitioning into new scene
};

// ============================================================================
// TWEEN EASING (extends TransitionEasing with more options)
// ============================================================================
enum TweenEasing {
    TWEEN_LINEAR = 0,
    TWEEN_IN_QUAD = 1,
    TWEEN_OUT_QUAD = 2,
    TWEEN_IN_OUT_QUAD = 3,
    TWEEN_IN_CUBIC = 4,
    TWEEN_OUT_CUBIC = 5,
    TWEEN_IN_OUT_CUBIC = 6,
    TWEEN_IN_EXPO = 7,
    TWEEN_OUT_EXPO = 8,
    TWEEN_IN_OUT_EXPO = 9,
    TWEEN_IN_SINE = 10,
    TWEEN_OUT_SINE = 11,
    TWEEN_IN_OUT_SINE = 12,
    TWEEN_IN_BACK = 13,
    TWEEN_OUT_BACK = 14,
    TWEEN_IN_OUT_BACK = 15,
    TWEEN_IN_ELASTIC = 16,
    TWEEN_OUT_ELASTIC = 17,
    TWEEN_IN_OUT_ELASTIC = 18,
    TWEEN_IN_BOUNCE = 19,
    TWEEN_OUT_BOUNCE = 20,
    TWEEN_IN_OUT_BOUNCE = 21
};

// ============================================================================
// TWEEN LOOP MODE
// ============================================================================
enum TweenLoopMode {
    TWEEN_LOOP_NONE = 0,      // Play once
    TWEEN_LOOP_RESTART = 1,   // Loop from start
    TWEEN_LOOP_YOYO = 2,      // Ping-pong back and forth
    TWEEN_LOOP_INCREMENT = 3  // Loop and increment values each time
};

// ============================================================================
// TWEEN STATE
// ============================================================================
enum TweenState {
    TWEEN_STATE_IDLE = 0,
    TWEEN_STATE_PLAYING = 1,
    TWEEN_STATE_PAUSED = 2,
    TWEEN_STATE_COMPLETED = 3
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

// Physics Callbacks
typedef void (*PhysicsCollisionCallback)(int bodyA, int bodyB, float normalX, float normalY, float depth);

// Scene Manager Callbacks
typedef void (*LoadingCallback)(float progress);  // Called during loading with 0-1 progress
typedef void (*LoadingDrawCallback)();            // Custom loading screen draw function

// Tween Callbacks
typedef void (*TweenCallback)(int tweenId);                    // Called on complete/start/stop
typedef void (*TweenUpdateCallback)(int tweenId, float value); // Called each update with current value

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
    __declspec(dllexport) void  Framework_DrawTriangle(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DrawTriangleLines(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

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
    // CAMERA 2D (Enhanced)
    // ========================================================================
    // Smooth follow
    __declspec(dllexport) void  Framework_Camera_SetFollowTarget(float x, float y);
    __declspec(dllexport) void  Framework_Camera_SetFollowLerp(float lerpSpeed);  // 0-1, higher = faster
    __declspec(dllexport) float Framework_Camera_GetFollowLerp();
    __declspec(dllexport) void  Framework_Camera_SetFollowEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Camera_IsFollowEnabled();

    // Deadzone (area where target can move without camera moving)
    __declspec(dllexport) void  Framework_Camera_SetDeadzone(float width, float height);
    __declspec(dllexport) void  Framework_Camera_GetDeadzone(float* width, float* height);
    __declspec(dllexport) void  Framework_Camera_SetDeadzoneEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Camera_IsDeadzoneEnabled();

    // Look-ahead (camera leads target based on velocity)
    __declspec(dllexport) void  Framework_Camera_SetLookahead(float distance, float smoothing);
    __declspec(dllexport) void  Framework_Camera_SetLookaheadEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Camera_SetLookaheadVelocity(float vx, float vy);

    // Screen shake
    __declspec(dllexport) void  Framework_Camera_Shake(float intensity, float duration);
    __declspec(dllexport) void  Framework_Camera_ShakeEx(float intensity, float duration, float frequency, float decay);
    __declspec(dllexport) void  Framework_Camera_StopShake();
    __declspec(dllexport) bool  Framework_Camera_IsShaking();
    __declspec(dllexport) float Framework_Camera_GetShakeIntensity();

    // Bounds/constraints (camera won't show beyond these world coordinates)
    __declspec(dllexport) void  Framework_Camera_SetBounds(float minX, float minY, float maxX, float maxY);
    __declspec(dllexport) void  Framework_Camera_GetBounds(float* minX, float* minY, float* maxX, float* maxY);
    __declspec(dllexport) void  Framework_Camera_SetBoundsEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Camera_IsBoundsEnabled();
    __declspec(dllexport) void  Framework_Camera_ClearBounds();

    // Zoom controls
    __declspec(dllexport) void  Framework_Camera_SetZoomLimits(float minZoom, float maxZoom);
    __declspec(dllexport) void  Framework_Camera_ZoomTo(float targetZoom, float duration);
    __declspec(dllexport) void  Framework_Camera_ZoomAt(float targetZoom, float worldX, float worldY, float duration);

    // Smooth rotation
    __declspec(dllexport) void  Framework_Camera_RotateTo(float targetRotation, float duration);

    // Pan/move
    __declspec(dllexport) void  Framework_Camera_PanTo(float worldX, float worldY, float duration);
    __declspec(dllexport) void  Framework_Camera_PanBy(float deltaX, float deltaY, float duration);
    __declspec(dllexport) bool  Framework_Camera_IsPanning();
    __declspec(dllexport) void  Framework_Camera_StopPan();

    // Flash effect (screen flash for impacts, etc.)
    __declspec(dllexport) void  Framework_Camera_Flash(unsigned char r, unsigned char g, unsigned char b, unsigned char a, float duration);
    __declspec(dllexport) bool  Framework_Camera_IsFlashing();
    __declspec(dllexport) void  Framework_Camera_DrawFlash();

    // Camera update (call each frame for smooth follow, shake, transitions)
    __declspec(dllexport) void  Framework_Camera_Update(float dt);

    // Reset camera to defaults
    __declspec(dllexport) void  Framework_Camera_Reset();

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
    // AUDIO MANAGER - Advanced Audio System
    // ========================================================================
    // Group volume control
    __declspec(dllexport) void  Framework_Audio_SetGroupVolume(int group, float volume);  // AudioGroup enum
    __declspec(dllexport) float Framework_Audio_GetGroupVolume(int group);
    __declspec(dllexport) void  Framework_Audio_SetGroupMuted(int group, bool muted);
    __declspec(dllexport) bool  Framework_Audio_IsGroupMuted(int group);
    __declspec(dllexport) void  Framework_Audio_FadeGroupVolume(int group, float targetVolume, float duration);

    // Sound with group assignment
    __declspec(dllexport) int   Framework_Audio_LoadSound(const char* path, int group);
    __declspec(dllexport) void  Framework_Audio_UnloadSound(int handle);
    __declspec(dllexport) void  Framework_Audio_PlaySound(int handle);
    __declspec(dllexport) void  Framework_Audio_PlaySoundEx(int handle, float volume, float pitch, float pan);
    __declspec(dllexport) void  Framework_Audio_StopSound(int handle);
    __declspec(dllexport) void  Framework_Audio_SetSoundGroup(int handle, int group);
    __declspec(dllexport) int   Framework_Audio_GetSoundGroup(int handle);

    // Spatial audio (2D positioning)
    __declspec(dllexport) void  Framework_Audio_SetListenerPosition(float x, float y);
    __declspec(dllexport) void  Framework_Audio_GetListenerPosition(float* x, float* y);
    __declspec(dllexport) void  Framework_Audio_PlaySoundAt(int handle, float x, float y);
    __declspec(dllexport) void  Framework_Audio_PlaySoundAtEx(int handle, float x, float y, float volume, float pitch);
    __declspec(dllexport) void  Framework_Audio_SetSpatialFalloff(float minDist, float maxDist);  // Distance for volume falloff
    __declspec(dllexport) void  Framework_Audio_SetSpatialEnabled(bool enabled);

    // Sound pooling (for frequent sounds like footsteps, gunshots)
    __declspec(dllexport) int   Framework_Audio_CreatePool(const char* path, int poolSize, int group);
    __declspec(dllexport) void  Framework_Audio_DestroyPool(int poolHandle);
    __declspec(dllexport) void  Framework_Audio_PlayFromPool(int poolHandle);
    __declspec(dllexport) void  Framework_Audio_PlayFromPoolAt(int poolHandle, float x, float y);
    __declspec(dllexport) void  Framework_Audio_PlayFromPoolEx(int poolHandle, float volume, float pitch, float pan);
    __declspec(dllexport) void  Framework_Audio_StopPool(int poolHandle);

    // Music with group (streaming)
    __declspec(dllexport) int   Framework_Audio_LoadMusic(const char* path);
    __declspec(dllexport) void  Framework_Audio_UnloadMusic(int handle);
    __declspec(dllexport) void  Framework_Audio_PlayMusic(int handle);
    __declspec(dllexport) void  Framework_Audio_StopMusic(int handle);
    __declspec(dllexport) void  Framework_Audio_PauseMusic(int handle);
    __declspec(dllexport) void  Framework_Audio_ResumeMusic(int handle);
    __declspec(dllexport) void  Framework_Audio_SetMusicVolume(int handle, float volume);
    __declspec(dllexport) void  Framework_Audio_SetMusicPitch(int handle, float pitch);
    __declspec(dllexport) void  Framework_Audio_SetMusicLooping(int handle, bool looping);
    __declspec(dllexport) bool  Framework_Audio_IsMusicPlaying(int handle);
    __declspec(dllexport) float Framework_Audio_GetMusicLength(int handle);
    __declspec(dllexport) float Framework_Audio_GetMusicPosition(int handle);
    __declspec(dllexport) void  Framework_Audio_SeekMusic(int handle, float position);

    // Music crossfading
    __declspec(dllexport) void  Framework_Audio_CrossfadeTo(int newMusicHandle, float duration);
    __declspec(dllexport) void  Framework_Audio_FadeOutMusic(int handle, float duration);
    __declspec(dllexport) void  Framework_Audio_FadeInMusic(int handle, float duration, float targetVolume);
    __declspec(dllexport) bool  Framework_Audio_IsCrossfading();

    // Playlist system
    __declspec(dllexport) int   Framework_Audio_CreatePlaylist();
    __declspec(dllexport) void  Framework_Audio_DestroyPlaylist(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistAdd(int playlistHandle, int musicHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistRemove(int playlistHandle, int index);
    __declspec(dllexport) void  Framework_Audio_PlaylistClear(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistPlay(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistStop(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistNext(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistPrev(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistSetShuffle(int playlistHandle, bool shuffle);
    __declspec(dllexport) void  Framework_Audio_PlaylistSetRepeat(int playlistHandle, int mode);  // 0=none, 1=all, 2=one
    __declspec(dllexport) int   Framework_Audio_PlaylistGetCurrent(int playlistHandle);
    __declspec(dllexport) int   Framework_Audio_PlaylistGetCount(int playlistHandle);
    __declspec(dllexport) void  Framework_Audio_PlaylistSetCrossfade(int playlistHandle, float duration);

    // Audio manager update (call each frame)
    __declspec(dllexport) void  Framework_Audio_Update(float dt);

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
    // SCENE MANAGER - Transitions & Loading Screens
    // ========================================================================
    // Transition configuration
    __declspec(dllexport) void  Framework_Scene_SetTransition(int transitionType, float duration);  // SceneTransitionType enum
    __declspec(dllexport) void  Framework_Scene_SetTransitionEx(int transitionType, float duration, int easing);  // With easing
    __declspec(dllexport) void  Framework_Scene_SetTransitionColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) int   Framework_Scene_GetTransitionType();
    __declspec(dllexport) float Framework_Scene_GetTransitionDuration();
    __declspec(dllexport) int   Framework_Scene_GetTransitionEasing();

    // Scene change with transition
    __declspec(dllexport) void  Framework_Scene_ChangeWithTransition(int sceneHandle);
    __declspec(dllexport) void  Framework_Scene_ChangeWithTransitionEx(int sceneHandle, int transitionType, float duration);
    __declspec(dllexport) void  Framework_Scene_PushWithTransition(int sceneHandle);
    __declspec(dllexport) void  Framework_Scene_PopWithTransition();

    // Transition state
    __declspec(dllexport) bool  Framework_Scene_IsTransitioning();
    __declspec(dllexport) int   Framework_Scene_GetTransitionState();  // TransitionState enum
    __declspec(dllexport) float Framework_Scene_GetTransitionProgress();  // 0-1 progress
    __declspec(dllexport) void  Framework_Scene_SkipTransition();  // Skip current transition

    // Loading screen
    __declspec(dllexport) void  Framework_Scene_SetLoadingEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Scene_IsLoadingEnabled();
    __declspec(dllexport) void  Framework_Scene_SetLoadingMinDuration(float seconds);  // Minimum loading screen time
    __declspec(dllexport) float Framework_Scene_GetLoadingMinDuration();
    __declspec(dllexport) void  Framework_Scene_SetLoadingCallback(LoadingCallback callback);
    __declspec(dllexport) void  Framework_Scene_SetLoadingDrawCallback(LoadingDrawCallback callback);
    __declspec(dllexport) void  Framework_Scene_SetLoadingProgress(float progress);  // Set by game code during load
    __declspec(dllexport) float Framework_Scene_GetLoadingProgress();
    __declspec(dllexport) bool  Framework_Scene_IsLoading();

    // Scene stack queries
    __declspec(dllexport) int   Framework_Scene_GetStackSize();
    __declspec(dllexport) int   Framework_Scene_GetSceneAt(int index);  // 0 = bottom of stack
    __declspec(dllexport) int   Framework_Scene_GetPreviousScene();  // Scene below current, or -1

    // Scene update (handles transitions, loading, and scene ticks)
    __declspec(dllexport) void  Framework_Scene_Update(float dt);
    __declspec(dllexport) void  Framework_Scene_Draw();  // Draw transition effects

    // Preloading scenes (for async loading)
    __declspec(dllexport) void  Framework_Scene_PreloadStart(int sceneHandle);
    __declspec(dllexport) bool  Framework_Scene_IsPreloading();
    __declspec(dllexport) void  Framework_Scene_PreloadCancel();

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
    // PROFILING & PERFORMANCE - Frame timing, metrics, and profiling scopes
    // ========================================================================

    // Log levels for console output
    enum LogLevel {
        LOG_LEVEL_TRACE = 0,
        LOG_LEVEL_DEBUG = 1,
        LOG_LEVEL_INFO = 2,
        LOG_LEVEL_WARNING = 3,
        LOG_LEVEL_ERROR = 4,
        LOG_LEVEL_FATAL = 5
    };

    // Frame timing
    __declspec(dllexport) float Framework_Perf_GetFPS();
    __declspec(dllexport) float Framework_Perf_GetFrameTime();           // Current frame time in ms
    __declspec(dllexport) float Framework_Perf_GetFrameTimeAvg();        // Average over last N frames
    __declspec(dllexport) float Framework_Perf_GetFrameTimeMin();        // Min over last N frames
    __declspec(dllexport) float Framework_Perf_GetFrameTimeMax();        // Max over last N frames
    __declspec(dllexport) void  Framework_Perf_SetSampleCount(int count);// How many frames to average
    __declspec(dllexport) int   Framework_Perf_GetFrameCount();          // Total frames since start

    // Draw call tracking
    __declspec(dllexport) int   Framework_Perf_GetDrawCalls();
    __declspec(dllexport) int   Framework_Perf_GetTriangleCount();
    __declspec(dllexport) void  Framework_Perf_ResetDrawStats();         // Call at frame start

    // Memory tracking
    __declspec(dllexport) int   Framework_Perf_GetEntityCount();
    __declspec(dllexport) int   Framework_Perf_GetTextureCount();
    __declspec(dllexport) int   Framework_Perf_GetSoundCount();
    __declspec(dllexport) int   Framework_Perf_GetFontCount();
    __declspec(dllexport) long long Framework_Perf_GetTextureMemory();   // Bytes used by textures

    // Profiling scopes (manual markers)
    __declspec(dllexport) void  Framework_Perf_BeginScope(const char* name);
    __declspec(dllexport) void  Framework_Perf_EndScope();
    __declspec(dllexport) float Framework_Perf_GetScopeTime(const char* name);  // Last recorded time in ms
    __declspec(dllexport) float Framework_Perf_GetScopeTimeAvg(const char* name);
    __declspec(dllexport) int   Framework_Perf_GetScopeCallCount(const char* name);
    __declspec(dllexport) void  Framework_Perf_ResetScopes();

    // Performance graphs
    __declspec(dllexport) void  Framework_Perf_SetGraphEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Perf_SetGraphPosition(float x, float y);
    __declspec(dllexport) void  Framework_Perf_SetGraphSize(float width, float height);
    __declspec(dllexport) void  Framework_Perf_DrawGraph();              // Call to render FPS/frame time graph

    // Console/Logging
    __declspec(dllexport) void  Framework_Log(int level, const char* message);
    __declspec(dllexport) void  Framework_Log_SetMinLevel(int level);    // Filter logs below this level
    __declspec(dllexport) int   Framework_Log_GetMinLevel();
    __declspec(dllexport) void  Framework_Log_SetFileOutput(const char* filename);  // Log to file
    __declspec(dllexport) void  Framework_Log_CloseFile();

    // On-screen console
    __declspec(dllexport) void  Framework_Console_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Console_IsEnabled();
    __declspec(dllexport) void  Framework_Console_SetPosition(float x, float y);
    __declspec(dllexport) void  Framework_Console_SetSize(float width, float height);
    __declspec(dllexport) void  Framework_Console_SetMaxLines(int maxLines);
    __declspec(dllexport) void  Framework_Console_Clear();
    __declspec(dllexport) void  Framework_Console_Print(const char* message);
    __declspec(dllexport) void  Framework_Console_PrintColored(const char* message, unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Console_Draw();                // Call to render console

    // Debug drawing (world space)
    __declspec(dllexport) void  Framework_DebugDraw_Line(float x1, float y1, float x2, float y2, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Rect(float x, float y, float w, float h, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_RectFilled(float x, float y, float w, float h, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Circle(float x, float y, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_CircleFilled(float x, float y, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Point(float x, float y, float size, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Arrow(float x1, float y1, float x2, float y2, float headSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Text(float x, float y, const char* text, unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_DebugDraw_Grid(float cellSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    __declspec(dllexport) void  Framework_DebugDraw_Cross(float x, float y, float size, unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Debug draw settings
    __declspec(dllexport) void  Framework_DebugDraw_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_DebugDraw_IsEnabled();
    __declspec(dllexport) void  Framework_DebugDraw_SetPersistent(bool persistent);  // Shapes persist across frames
    __declspec(dllexport) void  Framework_DebugDraw_Clear();             // Clear all debug shapes
    __declspec(dllexport) void  Framework_DebugDraw_Flush();             // Render and clear

    // System overlays
    __declspec(dllexport) void  Framework_Debug_SetShowFPS(bool show);
    __declspec(dllexport) void  Framework_Debug_SetShowFrameTime(bool show);
    __declspec(dllexport) void  Framework_Debug_SetShowDrawCalls(bool show);
    __declspec(dllexport) void  Framework_Debug_SetShowEntityCount(bool show);
    __declspec(dllexport) void  Framework_Debug_SetShowMemory(bool show);
    __declspec(dllexport) void  Framework_Debug_SetShowPhysics(bool show);       // Draw physics shapes
    __declspec(dllexport) void  Framework_Debug_SetShowColliders(bool show);     // Draw collision boxes
    __declspec(dllexport) void  Framework_Debug_SetOverlayPosition(float x, float y);
    __declspec(dllexport) void  Framework_Debug_SetOverlayColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // Update profiling (call each frame)
    __declspec(dllexport) void  Framework_Perf_BeginFrame();
    __declspec(dllexport) void  Framework_Perf_EndFrame();

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
    // PHYSICS SYSTEM - 2D Rigid Body Physics
    // ========================================================================
    // World settings
    __declspec(dllexport) void  Framework_Physics_SetGravity(float gx, float gy);
    __declspec(dllexport) void  Framework_Physics_GetGravity(float* gx, float* gy);
    __declspec(dllexport) void  Framework_Physics_SetIterations(int velocityIterations, int positionIterations);
    __declspec(dllexport) void  Framework_Physics_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Physics_IsEnabled();

    // Physics body creation/destruction
    __declspec(dllexport) int   Framework_Physics_CreateBody(int bodyType, float x, float y);  // Returns body handle
    __declspec(dllexport) void  Framework_Physics_DestroyBody(int bodyHandle);
    __declspec(dllexport) bool  Framework_Physics_IsBodyValid(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_DestroyAllBodies();

    // Body type
    __declspec(dllexport) void  Framework_Physics_SetBodyType(int bodyHandle, int bodyType);  // PhysicsBodyType enum
    __declspec(dllexport) int   Framework_Physics_GetBodyType(int bodyHandle);

    // Body transform
    __declspec(dllexport) void  Framework_Physics_SetBodyPosition(int bodyHandle, float x, float y);
    __declspec(dllexport) void  Framework_Physics_GetBodyPosition(int bodyHandle, float* x, float* y);
    __declspec(dllexport) void  Framework_Physics_SetBodyRotation(int bodyHandle, float radians);
    __declspec(dllexport) float Framework_Physics_GetBodyRotation(int bodyHandle);

    // Body dynamics
    __declspec(dllexport) void  Framework_Physics_SetBodyVelocity(int bodyHandle, float vx, float vy);
    __declspec(dllexport) void  Framework_Physics_GetBodyVelocity(int bodyHandle, float* vx, float* vy);
    __declspec(dllexport) void  Framework_Physics_SetBodyAngularVelocity(int bodyHandle, float omega);
    __declspec(dllexport) float Framework_Physics_GetBodyAngularVelocity(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_ApplyForce(int bodyHandle, float fx, float fy);
    __declspec(dllexport) void  Framework_Physics_ApplyForceAtPoint(int bodyHandle, float fx, float fy, float px, float py);
    __declspec(dllexport) void  Framework_Physics_ApplyImpulse(int bodyHandle, float ix, float iy);
    __declspec(dllexport) void  Framework_Physics_ApplyTorque(int bodyHandle, float torque);

    // Body properties
    __declspec(dllexport) void  Framework_Physics_SetBodyMass(int bodyHandle, float mass);
    __declspec(dllexport) float Framework_Physics_GetBodyMass(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_SetBodyRestitution(int bodyHandle, float restitution);  // Bounciness 0-1
    __declspec(dllexport) float Framework_Physics_GetBodyRestitution(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_SetBodyFriction(int bodyHandle, float friction);  // 0-1
    __declspec(dllexport) float Framework_Physics_GetBodyFriction(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_SetBodyGravityScale(int bodyHandle, float scale);  // 0 = no gravity
    __declspec(dllexport) float Framework_Physics_GetBodyGravityScale(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_SetBodyLinearDamping(int bodyHandle, float damping);
    __declspec(dllexport) void  Framework_Physics_SetBodyAngularDamping(int bodyHandle, float damping);
    __declspec(dllexport) void  Framework_Physics_SetBodyFixedRotation(int bodyHandle, bool fixed);
    __declspec(dllexport) bool  Framework_Physics_IsBodyFixedRotation(int bodyHandle);
    __declspec(dllexport) void  Framework_Physics_SetBodySleepingAllowed(int bodyHandle, bool allowed);
    __declspec(dllexport) void  Framework_Physics_WakeBody(int bodyHandle);
    __declspec(dllexport) bool  Framework_Physics_IsBodyAwake(int bodyHandle);

    // Collision shapes - attach to body
    __declspec(dllexport) void  Framework_Physics_SetBodyCircle(int bodyHandle, float radius);
    __declspec(dllexport) void  Framework_Physics_SetBodyCircleOffset(int bodyHandle, float radius, float offsetX, float offsetY);
    __declspec(dllexport) void  Framework_Physics_SetBodyBox(int bodyHandle, float width, float height);
    __declspec(dllexport) void  Framework_Physics_SetBodyBoxOffset(int bodyHandle, float width, float height, float offsetX, float offsetY);
    __declspec(dllexport) void  Framework_Physics_SetBodyPolygon(int bodyHandle, const float* vertices, int vertexCount);  // pairs of x,y
    __declspec(dllexport) int   Framework_Physics_GetBodyShapeType(int bodyHandle);  // CollisionShapeType enum

    // Collision filtering
    __declspec(dllexport) void  Framework_Physics_SetBodyLayer(int bodyHandle, unsigned int layer);      // Bitmask layer
    __declspec(dllexport) void  Framework_Physics_SetBodyMask(int bodyHandle, unsigned int mask);        // Bitmask of layers to collide with
    __declspec(dllexport) void  Framework_Physics_SetBodyTrigger(int bodyHandle, bool isTrigger);        // Triggers don't resolve collisions
    __declspec(dllexport) bool  Framework_Physics_IsBodyTrigger(int bodyHandle);

    // Entity binding - link physics body to ECS entity
    __declspec(dllexport) void  Framework_Physics_BindToEntity(int bodyHandle, int entityId);
    __declspec(dllexport) int   Framework_Physics_GetBoundEntity(int bodyHandle);  // Returns -1 if not bound
    __declspec(dllexport) int   Framework_Physics_GetEntityBody(int entityId);     // Returns -1 if no body

    // User data
    __declspec(dllexport) void  Framework_Physics_SetBodyUserData(int bodyHandle, int userData);
    __declspec(dllexport) int   Framework_Physics_GetBodyUserData(int bodyHandle);

    // Collision callbacks
    __declspec(dllexport) void  Framework_Physics_SetCollisionEnterCallback(PhysicsCollisionCallback callback);
    __declspec(dllexport) void  Framework_Physics_SetCollisionStayCallback(PhysicsCollisionCallback callback);
    __declspec(dllexport) void  Framework_Physics_SetCollisionExitCallback(PhysicsCollisionCallback callback);
    __declspec(dllexport) void  Framework_Physics_SetTriggerEnterCallback(PhysicsCollisionCallback callback);
    __declspec(dllexport) void  Framework_Physics_SetTriggerExitCallback(PhysicsCollisionCallback callback);

    // Physics queries
    __declspec(dllexport) int   Framework_Physics_RaycastFirst(float startX, float startY, float dirX, float dirY, float maxDist,
                                                                float* hitX, float* hitY, float* hitNormalX, float* hitNormalY);  // Returns body handle or -1
    __declspec(dllexport) int   Framework_Physics_RaycastAll(float startX, float startY, float dirX, float dirY, float maxDist,
                                                              int* bodyBuffer, int bufferSize);  // Returns count
    __declspec(dllexport) int   Framework_Physics_QueryCircle(float x, float y, float radius, int* bodyBuffer, int bufferSize);
    __declspec(dllexport) int   Framework_Physics_QueryBox(float x, float y, float width, float height, int* bodyBuffer, int bufferSize);
    __declspec(dllexport) bool  Framework_Physics_TestOverlap(int bodyA, int bodyB);

    // Simulation
    __declspec(dllexport) void  Framework_Physics_Step(float dt);  // Advance physics simulation
    __declspec(dllexport) void  Framework_Physics_SyncToEntities();  // Copy body positions to bound entities

    // Debug rendering
    __declspec(dllexport) void  Framework_Physics_SetDebugDraw(bool enabled);
    __declspec(dllexport) bool  Framework_Physics_IsDebugDrawEnabled();
    __declspec(dllexport) void  Framework_Physics_DrawDebug();  // Draw all collision shapes

    // ========================================================================
    // INPUT MANAGER - Action-based Input System
    // ========================================================================
    // Input source types for binding
    enum InputSourceType {
        INPUT_SOURCE_KEYBOARD = 0,
        INPUT_SOURCE_MOUSE_BUTTON = 1,
        INPUT_SOURCE_MOUSE_AXIS = 2,
        INPUT_SOURCE_GAMEPAD_BUTTON = 3,
        INPUT_SOURCE_GAMEPAD_AXIS = 4,
        INPUT_SOURCE_GAMEPAD_TRIGGER = 5
    };

    // Mouse axes
    enum MouseAxis {
        MOUSE_AXIS_X = 0,
        MOUSE_AXIS_Y = 1,
        MOUSE_AXIS_WHEEL = 2,
        MOUSE_AXIS_WHEEL_H = 3
    };

    // Gamepad axes (FW_ prefix to avoid conflict with raylib)
    enum FW_GamepadAxis {
        FW_GAMEPAD_AXIS_LEFT_X = 0,
        FW_GAMEPAD_AXIS_LEFT_Y = 1,
        FW_GAMEPAD_AXIS_RIGHT_X = 2,
        FW_GAMEPAD_AXIS_RIGHT_Y = 3,
        FW_GAMEPAD_AXIS_LEFT_TRIGGER = 4,
        FW_GAMEPAD_AXIS_RIGHT_TRIGGER = 5
    };

    // Action management
    __declspec(dllexport) int   Framework_Input_CreateAction(const char* name);  // Returns action handle
    __declspec(dllexport) void  Framework_Input_DestroyAction(int actionHandle);
    __declspec(dllexport) int   Framework_Input_GetAction(const char* name);  // Returns -1 if not found
    __declspec(dllexport) bool  Framework_Input_IsActionValid(int actionHandle);
    __declspec(dllexport) void  Framework_Input_ClearAllActions();

    // Keyboard bindings
    __declspec(dllexport) void  Framework_Input_BindKey(int actionHandle, int keyCode);
    __declspec(dllexport) void  Framework_Input_UnbindKey(int actionHandle, int keyCode);
    __declspec(dllexport) void  Framework_Input_ClearKeyBindings(int actionHandle);

    // Mouse button bindings
    __declspec(dllexport) void  Framework_Input_BindMouseButton(int actionHandle, int button);  // 0=left, 1=right, 2=middle
    __declspec(dllexport) void  Framework_Input_UnbindMouseButton(int actionHandle, int button);

    // Gamepad button bindings
    __declspec(dllexport) void  Framework_Input_BindGamepadButton(int actionHandle, int button);
    __declspec(dllexport) void  Framework_Input_UnbindGamepadButton(int actionHandle, int button);

    // Axis bindings (for analog input actions)
    __declspec(dllexport) void  Framework_Input_BindMouseAxis(int actionHandle, int axis, float scale);  // MouseAxis enum
    __declspec(dllexport) void  Framework_Input_BindGamepadAxis(int actionHandle, int axis, float scale);  // GamepadAxis enum
    __declspec(dllexport) void  Framework_Input_ClearAxisBindings(int actionHandle);

    // Action state queries
    __declspec(dllexport) bool  Framework_Input_IsActionPressed(int actionHandle);   // Just pressed this frame
    __declspec(dllexport) bool  Framework_Input_IsActionDown(int actionHandle);      // Currently held
    __declspec(dllexport) bool  Framework_Input_IsActionReleased(int actionHandle);  // Just released this frame
    __declspec(dllexport) float Framework_Input_GetActionValue(int actionHandle);    // Analog value (-1 to 1)
    __declspec(dllexport) float Framework_Input_GetActionRawValue(int actionHandle); // Raw unprocessed value

    // Action configuration
    __declspec(dllexport) void  Framework_Input_SetActionDeadzone(int actionHandle, float deadzone);  // For analog
    __declspec(dllexport) float Framework_Input_GetActionDeadzone(int actionHandle);
    __declspec(dllexport) void  Framework_Input_SetActionSensitivity(int actionHandle, float sensitivity);
    __declspec(dllexport) float Framework_Input_GetActionSensitivity(int actionHandle);

    // Gamepad management
    __declspec(dllexport) bool  Framework_Input_IsGamepadAvailable(int gamepadId);  // 0-3
    __declspec(dllexport) const char* Framework_Input_GetGamepadName(int gamepadId);
    __declspec(dllexport) int   Framework_Input_GetGamepadCount();
    __declspec(dllexport) void  Framework_Input_SetActiveGamepad(int gamepadId);
    __declspec(dllexport) int   Framework_Input_GetActiveGamepad();

    // Direct gamepad queries (bypass action system)
    __declspec(dllexport) bool  Framework_Input_IsGamepadButtonPressed(int gamepadId, int button);
    __declspec(dllexport) bool  Framework_Input_IsGamepadButtonDown(int gamepadId, int button);
    __declspec(dllexport) bool  Framework_Input_IsGamepadButtonReleased(int gamepadId, int button);
    __declspec(dllexport) float Framework_Input_GetGamepadAxisValue(int gamepadId, int axis);

    // Rebinding support
    __declspec(dllexport) void  Framework_Input_StartListening(int actionHandle);  // Start listening for next input
    __declspec(dllexport) bool  Framework_Input_IsListening();
    __declspec(dllexport) void  Framework_Input_StopListening();
    __declspec(dllexport) bool  Framework_Input_WasBindingCaptured();  // True if new binding was captured
    __declspec(dllexport) int   Framework_Input_GetCapturedSourceType();  // InputSourceType
    __declspec(dllexport) int   Framework_Input_GetCapturedCode();  // Key/button code that was captured

    // Rumble/vibration (gamepad)
    __declspec(dllexport) void  Framework_Input_SetGamepadVibration(int gamepadId, float leftMotor, float rightMotor, float duration);
    __declspec(dllexport) void  Framework_Input_StopGamepadVibration(int gamepadId);

    // Input system update
    __declspec(dllexport) void  Framework_Input_Update();  // Call each frame to update action states

    // Serialization (save/load bindings)
    __declspec(dllexport) bool  Framework_Input_SaveBindings(const char* filename);
    __declspec(dllexport) bool  Framework_Input_LoadBindings(const char* filename);

    // ========================================================================
    // SAVE/LOAD SYSTEM - Game State Persistence
    // ========================================================================
    // Save slot management
    __declspec(dllexport) void  Framework_Save_SetDirectory(const char* directory);  // Set save directory
    __declspec(dllexport) const char* Framework_Save_GetDirectory();
    __declspec(dllexport) int   Framework_Save_GetSlotCount();  // Number of existing saves
    __declspec(dllexport) bool  Framework_Save_SlotExists(int slot);
    __declspec(dllexport) bool  Framework_Save_DeleteSlot(int slot);
    __declspec(dllexport) bool  Framework_Save_CopySlot(int fromSlot, int toSlot);
    __declspec(dllexport) const char* Framework_Save_GetSlotInfo(int slot);  // Returns metadata string

    // Save/Load current game state
    __declspec(dllexport) bool  Framework_Save_BeginSave(int slot);  // Start building save data
    __declspec(dllexport) bool  Framework_Save_EndSave();            // Write to disk
    __declspec(dllexport) bool  Framework_Save_BeginLoad(int slot);  // Start reading save data
    __declspec(dllexport) bool  Framework_Save_EndLoad();            // Finish loading

    // Data serialization - Write
    __declspec(dllexport) void  Framework_Save_WriteInt(const char* key, int value);
    __declspec(dllexport) void  Framework_Save_WriteFloat(const char* key, float value);
    __declspec(dllexport) void  Framework_Save_WriteBool(const char* key, bool value);
    __declspec(dllexport) void  Framework_Save_WriteString(const char* key, const char* value);
    __declspec(dllexport) void  Framework_Save_WriteVector2(const char* key, float x, float y);
    __declspec(dllexport) void  Framework_Save_WriteIntArray(const char* key, const int* values, int count);
    __declspec(dllexport) void  Framework_Save_WriteFloatArray(const char* key, const float* values, int count);

    // Data serialization - Read
    __declspec(dllexport) int   Framework_Save_ReadInt(const char* key, int defaultValue);
    __declspec(dllexport) float Framework_Save_ReadFloat(const char* key, float defaultValue);
    __declspec(dllexport) bool  Framework_Save_ReadBool(const char* key, bool defaultValue);
    __declspec(dllexport) const char* Framework_Save_ReadString(const char* key, const char* defaultValue);
    __declspec(dllexport) void  Framework_Save_ReadVector2(const char* key, float* x, float* y, float defX, float defY);
    __declspec(dllexport) int   Framework_Save_ReadIntArray(const char* key, int* buffer, int bufferSize);
    __declspec(dllexport) int   Framework_Save_ReadFloatArray(const char* key, float* buffer, int bufferSize);

    // Check if key exists
    __declspec(dllexport) bool  Framework_Save_HasKey(const char* key);

    // Save metadata (stored with each save)
    __declspec(dllexport) void  Framework_Save_SetMetadata(const char* key, const char* value);
    __declspec(dllexport) const char* Framework_Save_GetMetadata(int slot, const char* key);

    // Auto-save
    __declspec(dllexport) void  Framework_Save_SetAutoSaveEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Save_IsAutoSaveEnabled();
    __declspec(dllexport) void  Framework_Save_SetAutoSaveInterval(float seconds);
    __declspec(dllexport) float Framework_Save_GetAutoSaveInterval();
    __declspec(dllexport) void  Framework_Save_SetAutoSaveSlot(int slot);  // -1 for rotating slots
    __declspec(dllexport) int   Framework_Save_GetAutoSaveSlot();
    __declspec(dllexport) void  Framework_Save_TriggerAutoSave();  // Force auto-save now
    __declspec(dllexport) void  Framework_Save_Update(float dt);   // Call each frame for auto-save timing

    // Quick save/load (slot 0)
    __declspec(dllexport) bool  Framework_Save_QuickSave();
    __declspec(dllexport) bool  Framework_Save_QuickLoad();

    // Settings (separate from game saves, persistent across sessions)
    __declspec(dllexport) void  Framework_Settings_SetInt(const char* key, int value);
    __declspec(dllexport) int   Framework_Settings_GetInt(const char* key, int defaultValue);
    __declspec(dllexport) void  Framework_Settings_SetFloat(const char* key, float value);
    __declspec(dllexport) float Framework_Settings_GetFloat(const char* key, float defaultValue);
    __declspec(dllexport) void  Framework_Settings_SetBool(const char* key, bool value);
    __declspec(dllexport) bool  Framework_Settings_GetBool(const char* key, bool defaultValue);
    __declspec(dllexport) void  Framework_Settings_SetString(const char* key, const char* value);
    __declspec(dllexport) const char* Framework_Settings_GetString(const char* key, const char* defaultValue);
    __declspec(dllexport) bool  Framework_Settings_Save();  // Save settings to disk
    __declspec(dllexport) bool  Framework_Settings_Load();  // Load settings from disk
    __declspec(dllexport) void  Framework_Settings_Clear(); // Clear all settings

    // ========================================================================
    // TWEENING SYSTEM - Property Animation & Interpolation
    // ========================================================================
    // Float tweens
    __declspec(dllexport) int   Framework_Tween_Float(float from, float to, float duration, int easing);  // Returns tween handle
    __declspec(dllexport) int   Framework_Tween_FloatTo(float* target, float to, float duration, int easing);  // Tweens pointer directly
    __declspec(dllexport) int   Framework_Tween_FloatFromTo(float* target, float from, float to, float duration, int easing);

    // Vector2 tweens
    __declspec(dllexport) int   Framework_Tween_Vector2(float fromX, float fromY, float toX, float toY, float duration, int easing);
    __declspec(dllexport) int   Framework_Tween_Vector2To(float* targetX, float* targetY, float toX, float toY, float duration, int easing);

    // Color tweens (RGBA bytes)
    __declspec(dllexport) int   Framework_Tween_Color(unsigned char fromR, unsigned char fromG, unsigned char fromB, unsigned char fromA,
                                                       unsigned char toR, unsigned char toG, unsigned char toB, unsigned char toA,
                                                       float duration, int easing);

    // Tween control
    __declspec(dllexport) void  Framework_Tween_Play(int tweenId);
    __declspec(dllexport) void  Framework_Tween_Pause(int tweenId);
    __declspec(dllexport) void  Framework_Tween_Resume(int tweenId);
    __declspec(dllexport) void  Framework_Tween_Stop(int tweenId);
    __declspec(dllexport) void  Framework_Tween_Restart(int tweenId);
    __declspec(dllexport) void  Framework_Tween_Kill(int tweenId);  // Stop and remove
    __declspec(dllexport) void  Framework_Tween_Complete(int tweenId);  // Jump to end

    // Tween state queries
    __declspec(dllexport) bool  Framework_Tween_IsValid(int tweenId);
    __declspec(dllexport) int   Framework_Tween_GetState(int tweenId);  // TweenState enum
    __declspec(dllexport) bool  Framework_Tween_IsPlaying(int tweenId);
    __declspec(dllexport) bool  Framework_Tween_IsPaused(int tweenId);
    __declspec(dllexport) bool  Framework_Tween_IsCompleted(int tweenId);
    __declspec(dllexport) float Framework_Tween_GetProgress(int tweenId);  // 0-1 normalized time
    __declspec(dllexport) float Framework_Tween_GetElapsed(int tweenId);   // Elapsed seconds
    __declspec(dllexport) float Framework_Tween_GetDuration(int tweenId);

    // Tween value getters
    __declspec(dllexport) float Framework_Tween_GetFloat(int tweenId);
    __declspec(dllexport) void  Framework_Tween_GetVector2(int tweenId, float* x, float* y);
    __declspec(dllexport) void  Framework_Tween_GetColor(int tweenId, unsigned char* r, unsigned char* g, unsigned char* b, unsigned char* a);

    // Tween configuration
    __declspec(dllexport) void  Framework_Tween_SetDelay(int tweenId, float delay);  // Delay before start
    __declspec(dllexport) float Framework_Tween_GetDelay(int tweenId);
    __declspec(dllexport) void  Framework_Tween_SetLoopMode(int tweenId, int loopMode);  // TweenLoopMode
    __declspec(dllexport) int   Framework_Tween_GetLoopMode(int tweenId);
    __declspec(dllexport) void  Framework_Tween_SetLoopCount(int tweenId, int count);  // -1 = infinite
    __declspec(dllexport) int   Framework_Tween_GetLoopCount(int tweenId);
    __declspec(dllexport) int   Framework_Tween_GetCurrentLoop(int tweenId);
    __declspec(dllexport) void  Framework_Tween_SetTimeScale(int tweenId, float scale);  // 1.0 = normal speed
    __declspec(dllexport) float Framework_Tween_GetTimeScale(int tweenId);
    __declspec(dllexport) void  Framework_Tween_SetAutoKill(int tweenId, bool autoKill);  // Remove when complete

    // Tween callbacks
    __declspec(dllexport) void  Framework_Tween_SetOnStart(int tweenId, TweenCallback callback);
    __declspec(dllexport) void  Framework_Tween_SetOnUpdate(int tweenId, TweenUpdateCallback callback);
    __declspec(dllexport) void  Framework_Tween_SetOnComplete(int tweenId, TweenCallback callback);
    __declspec(dllexport) void  Framework_Tween_SetOnLoop(int tweenId, TweenCallback callback);
    __declspec(dllexport) void  Framework_Tween_SetOnKill(int tweenId, TweenCallback callback);

    // Sequence building (chain tweens)
    __declspec(dllexport) int   Framework_Tween_CreateSequence();  // Returns sequence handle
    __declspec(dllexport) void  Framework_Tween_SequenceAppend(int seqId, int tweenId);  // Add tween to run after previous
    __declspec(dllexport) void  Framework_Tween_SequenceJoin(int seqId, int tweenId);    // Add tween to run with previous
    __declspec(dllexport) void  Framework_Tween_SequenceInsert(int seqId, float atTime, int tweenId);  // Insert at specific time
    __declspec(dllexport) void  Framework_Tween_SequenceAppendDelay(int seqId, float delay);  // Add delay
    __declspec(dllexport) void  Framework_Tween_SequenceAppendCallback(int seqId, TweenCallback callback);  // Add callback point
    __declspec(dllexport) void  Framework_Tween_PlaySequence(int seqId);
    __declspec(dllexport) void  Framework_Tween_PauseSequence(int seqId);
    __declspec(dllexport) void  Framework_Tween_StopSequence(int seqId);
    __declspec(dllexport) void  Framework_Tween_KillSequence(int seqId);
    __declspec(dllexport) bool  Framework_Tween_IsSequenceValid(int seqId);
    __declspec(dllexport) bool  Framework_Tween_IsSequencePlaying(int seqId);
    __declspec(dllexport) float Framework_Tween_GetSequenceDuration(int seqId);

    // Entity property tweens (convenience)
    __declspec(dllexport) int   Framework_Tween_EntityPosition(int entity, float toX, float toY, float duration, int easing);
    __declspec(dllexport) int   Framework_Tween_EntityRotation(int entity, float toRotation, float duration, int easing);
    __declspec(dllexport) int   Framework_Tween_EntityScale(int entity, float toScaleX, float toScaleY, float duration, int easing);
    __declspec(dllexport) int   Framework_Tween_EntityAlpha(int entity, unsigned char toAlpha, float duration, int easing);

    // Global tween management
    __declspec(dllexport) void  Framework_Tween_Update(float dt);  // Call each frame
    __declspec(dllexport) void  Framework_Tween_PauseAll();
    __declspec(dllexport) void  Framework_Tween_ResumeAll();
    __declspec(dllexport) void  Framework_Tween_KillAll();
    __declspec(dllexport) int   Framework_Tween_GetActiveCount();
    __declspec(dllexport) void  Framework_Tween_SetGlobalTimeScale(float scale);
    __declspec(dllexport) float Framework_Tween_GetGlobalTimeScale();

    // Easing function (standalone utility)
    __declspec(dllexport) float Framework_Tween_Ease(float t, int easing);  // Apply easing to 0-1 value

    // ========================================================================
    // EVENT SYSTEM - Publish/Subscribe messaging
    // ========================================================================

    // Event data types for payloads
    enum EventDataType {
        EVENT_DATA_NONE = 0,
        EVENT_DATA_INT = 1,
        EVENT_DATA_FLOAT = 2,
        EVENT_DATA_STRING = 3,
        EVENT_DATA_VECTOR2 = 4,
        EVENT_DATA_ENTITY = 5,
        EVENT_DATA_POINTER = 6
    };

    // Event callback types
    typedef void (*EventCallback)(int eventId, void* userData);
    typedef void (*EventCallbackInt)(int eventId, int value, void* userData);
    typedef void (*EventCallbackFloat)(int eventId, float value, void* userData);
    typedef void (*EventCallbackString)(int eventId, const char* value, void* userData);
    typedef void (*EventCallbackVector2)(int eventId, float x, float y, void* userData);
    typedef void (*EventCallbackEntity)(int eventId, int entity, void* userData);

    // Event registration and naming
    __declspec(dllexport) int   Framework_Event_Register(const char* eventName);  // Returns event ID
    __declspec(dllexport) int   Framework_Event_GetId(const char* eventName);     // Get ID by name (-1 if not found)
    __declspec(dllexport) const char* Framework_Event_GetName(int eventId);       // Get name by ID
    __declspec(dllexport) bool  Framework_Event_Exists(const char* eventName);

    // Subscribe to events (returns subscription handle)
    __declspec(dllexport) int   Framework_Event_Subscribe(int eventId, EventCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeInt(int eventId, EventCallbackInt callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeFloat(int eventId, EventCallbackFloat callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeString(int eventId, EventCallbackString callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeVector2(int eventId, EventCallbackVector2 callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeEntity(int eventId, EventCallbackEntity callback, void* userData);

    // Subscribe by name (convenience)
    __declspec(dllexport) int   Framework_Event_SubscribeByName(const char* eventName, EventCallback callback, void* userData);

    // One-shot subscriptions (auto-unsubscribe after first trigger)
    __declspec(dllexport) int   Framework_Event_SubscribeOnce(int eventId, EventCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Event_SubscribeOnceInt(int eventId, EventCallbackInt callback, void* userData);

    // Unsubscribe
    __declspec(dllexport) void  Framework_Event_Unsubscribe(int subscriptionId);
    __declspec(dllexport) void  Framework_Event_UnsubscribeAll(int eventId);      // Remove all listeners for event
    __declspec(dllexport) void  Framework_Event_UnsubscribeCallback(int eventId, EventCallback callback);  // Remove specific callback

    // Publish events (immediate dispatch)
    __declspec(dllexport) void  Framework_Event_Publish(int eventId);
    __declspec(dllexport) void  Framework_Event_PublishInt(int eventId, int value);
    __declspec(dllexport) void  Framework_Event_PublishFloat(int eventId, float value);
    __declspec(dllexport) void  Framework_Event_PublishString(int eventId, const char* value);
    __declspec(dllexport) void  Framework_Event_PublishVector2(int eventId, float x, float y);
    __declspec(dllexport) void  Framework_Event_PublishEntity(int eventId, int entity);

    // Publish by name (convenience)
    __declspec(dllexport) void  Framework_Event_PublishByName(const char* eventName);
    __declspec(dllexport) void  Framework_Event_PublishByNameInt(const char* eventName, int value);

    // Queued/deferred events (processed on Framework_Event_ProcessQueue)
    __declspec(dllexport) void  Framework_Event_Queue(int eventId);
    __declspec(dllexport) void  Framework_Event_QueueInt(int eventId, int value);
    __declspec(dllexport) void  Framework_Event_QueueFloat(int eventId, float value);
    __declspec(dllexport) void  Framework_Event_QueueString(int eventId, const char* value);
    __declspec(dllexport) void  Framework_Event_QueueDelayed(int eventId, float delay);  // Fire after delay
    __declspec(dllexport) void  Framework_Event_QueueDelayedInt(int eventId, int value, float delay);

    // Entity-specific events
    __declspec(dllexport) int   Framework_Event_SubscribeToEntity(int entity, int eventId, EventCallbackEntity callback, void* userData);
    __declspec(dllexport) void  Framework_Event_PublishToEntity(int entity, int eventId);
    __declspec(dllexport) void  Framework_Event_PublishToEntityInt(int entity, int eventId, int value);
    __declspec(dllexport) void  Framework_Event_UnsubscribeFromEntity(int entity, int eventId);
    __declspec(dllexport) void  Framework_Event_UnsubscribeAllFromEntity(int entity);  // Clear all entity subscriptions

    // Priority control (higher = called first, default = 0)
    __declspec(dllexport) void  Framework_Event_SetPriority(int subscriptionId, int priority);
    __declspec(dllexport) int   Framework_Event_GetPriority(int subscriptionId);

    // Event state and management
    __declspec(dllexport) void  Framework_Event_SetEnabled(int subscriptionId, bool enabled);
    __declspec(dllexport) bool  Framework_Event_IsEnabled(int subscriptionId);
    __declspec(dllexport) bool  Framework_Event_IsSubscriptionValid(int subscriptionId);
    __declspec(dllexport) int   Framework_Event_GetSubscriberCount(int eventId);

    // Queue processing and management
    __declspec(dllexport) void  Framework_Event_ProcessQueue(float dt);  // Process queued/delayed events
    __declspec(dllexport) void  Framework_Event_ClearQueue();
    __declspec(dllexport) int   Framework_Event_GetQueuedCount();

    // Global event system management
    __declspec(dllexport) void  Framework_Event_PauseAll();   // Stop processing events
    __declspec(dllexport) void  Framework_Event_ResumeAll();
    __declspec(dllexport) bool  Framework_Event_IsPaused();
    __declspec(dllexport) void  Framework_Event_Clear();      // Clear all events and subscriptions
    __declspec(dllexport) int   Framework_Event_GetEventCount();
    __declspec(dllexport) int   Framework_Event_GetTotalSubscriptions();

    // ========================================================================
    // TIMER SYSTEM - Delayed execution and scheduling
    // ========================================================================

    // Timer callback types
    typedef void (*TimerCallback)(int timerId, void* userData);
    typedef void (*TimerCallbackInt)(int timerId, int value, void* userData);
    typedef void (*TimerCallbackFloat)(int timerId, float value, void* userData);

    // Timer states
    enum TimerState {
        TIMER_STATE_PENDING = 0,    // Not yet started (has delay)
        TIMER_STATE_RUNNING = 1,    // Currently active
        TIMER_STATE_PAUSED = 2,     // Temporarily stopped
        TIMER_STATE_COMPLETED = 3,  // Finished (for one-shot)
        TIMER_STATE_CANCELLED = 4   // Manually stopped
    };

    // Basic timers (one-shot)
    __declspec(dllexport) int   Framework_Timer_After(float delay, TimerCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Timer_AfterInt(float delay, TimerCallbackInt callback, int value, void* userData);
    __declspec(dllexport) int   Framework_Timer_AfterFloat(float delay, TimerCallbackFloat callback, float value, void* userData);

    // Repeating timers
    __declspec(dllexport) int   Framework_Timer_Every(float interval, TimerCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Timer_EveryInt(float interval, TimerCallbackInt callback, int value, void* userData);
    __declspec(dllexport) int   Framework_Timer_EveryLimit(float interval, int repeatCount, TimerCallback callback, void* userData);

    // Timer with initial delay then repeat
    __declspec(dllexport) int   Framework_Timer_AfterThenEvery(float delay, float interval, TimerCallback callback, void* userData);

    // Timer control
    __declspec(dllexport) void  Framework_Timer_Cancel(int timerId);
    __declspec(dllexport) void  Framework_Timer_Pause(int timerId);
    __declspec(dllexport) void  Framework_Timer_Resume(int timerId);
    __declspec(dllexport) void  Framework_Timer_Reset(int timerId);  // Restart from beginning

    // Timer state queries
    __declspec(dllexport) bool  Framework_Timer_IsValid(int timerId);
    __declspec(dllexport) bool  Framework_Timer_IsRunning(int timerId);
    __declspec(dllexport) bool  Framework_Timer_IsPaused(int timerId);
    __declspec(dllexport) int   Framework_Timer_GetState(int timerId);  // Returns TimerState
    __declspec(dllexport) float Framework_Timer_GetElapsed(int timerId);
    __declspec(dllexport) float Framework_Timer_GetRemaining(int timerId);
    __declspec(dllexport) int   Framework_Timer_GetRepeatCount(int timerId);
    __declspec(dllexport) int   Framework_Timer_GetCurrentRepeat(int timerId);

    // Timer configuration
    __declspec(dllexport) void  Framework_Timer_SetTimeScale(int timerId, float scale);
    __declspec(dllexport) float Framework_Timer_GetTimeScale(int timerId);
    __declspec(dllexport) void  Framework_Timer_SetInterval(int timerId, float interval);
    __declspec(dllexport) float Framework_Timer_GetInterval(int timerId);

    // Entity-bound timers (auto-cancel when entity destroyed)
    __declspec(dllexport) int   Framework_Timer_AfterEntity(int entity, float delay, TimerCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Timer_EveryEntity(int entity, float interval, TimerCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Timer_CancelAllForEntity(int entity);

    // Sequence building (chain timed actions)
    __declspec(dllexport) int   Framework_Timer_CreateSequence();
    __declspec(dllexport) void  Framework_Timer_SequenceAppend(int seqId, float delay, TimerCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Timer_SequenceAppendInt(int seqId, float delay, TimerCallbackInt callback, int value, void* userData);
    __declspec(dllexport) void  Framework_Timer_SequenceStart(int seqId);
    __declspec(dllexport) void  Framework_Timer_SequencePause(int seqId);
    __declspec(dllexport) void  Framework_Timer_SequenceResume(int seqId);
    __declspec(dllexport) void  Framework_Timer_SequenceCancel(int seqId);
    __declspec(dllexport) void  Framework_Timer_SequenceReset(int seqId);
    __declspec(dllexport) bool  Framework_Timer_SequenceIsValid(int seqId);
    __declspec(dllexport) bool  Framework_Timer_SequenceIsRunning(int seqId);
    __declspec(dllexport) float Framework_Timer_SequenceGetDuration(int seqId);
    __declspec(dllexport) float Framework_Timer_SequenceGetElapsed(int seqId);
    __declspec(dllexport) void  Framework_Timer_SequenceSetLoop(int seqId, bool loop);

    // Global timer management
    __declspec(dllexport) void  Framework_Timer_Update(float dt);  // Call each frame
    __declspec(dllexport) void  Framework_Timer_PauseAll();
    __declspec(dllexport) void  Framework_Timer_ResumeAll();
    __declspec(dllexport) void  Framework_Timer_CancelAll();
    __declspec(dllexport) int   Framework_Timer_GetActiveCount();
    __declspec(dllexport) void  Framework_Timer_SetGlobalTimeScale(float scale);
    __declspec(dllexport) float Framework_Timer_GetGlobalTimeScale();

    // Frame-based timers (for frame-precise timing)
    __declspec(dllexport) int   Framework_Timer_AfterFrames(int frames, TimerCallback callback, void* userData);
    __declspec(dllexport) int   Framework_Timer_EveryFrames(int frames, TimerCallback callback, void* userData);

    // Utility functions
    __declspec(dllexport) void  Framework_Timer_ClearCompleted();  // Remove finished one-shot timers

    // ========================================================================
    // OBJECT POOLING - Efficient object reuse
    // ========================================================================

    // Forward declarations for functions used by entity pools
    __declspec(dllexport) void  Framework_Ecs_DestroyEntity(int entity);
    __declspec(dllexport) void  Framework_Ecs_SetEnabled(int entity, bool enabled);
    __declspec(dllexport) int   Framework_Prefab_Instantiate(int prefabH, int parentEntity, float x, float y);

    // Pool callback types
    typedef void (*PoolResetCallback)(int poolId, int objectIndex, void* userData);
    typedef void (*PoolInitCallback)(int poolId, int objectIndex, void* userData);

    // Pool creation and management
    __declspec(dllexport) int   Framework_Pool_Create(const char* poolName, int initialCapacity, int maxCapacity);
    __declspec(dllexport) int   Framework_Pool_GetByName(const char* poolName);
    __declspec(dllexport) void  Framework_Pool_Destroy(int poolId);
    __declspec(dllexport) bool  Framework_Pool_IsValid(int poolId);

    // Pool configuration
    __declspec(dllexport) void  Framework_Pool_SetAutoGrow(int poolId, bool autoGrow);
    __declspec(dllexport) bool  Framework_Pool_GetAutoGrow(int poolId);
    __declspec(dllexport) void  Framework_Pool_SetGrowAmount(int poolId, int amount);  // How many to add when growing
    __declspec(dllexport) int   Framework_Pool_GetGrowAmount(int poolId);
    __declspec(dllexport) void  Framework_Pool_SetResetCallback(int poolId, PoolResetCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Pool_SetInitCallback(int poolId, PoolInitCallback callback, void* userData);

    // Acquire and release objects
    __declspec(dllexport) int   Framework_Pool_Acquire(int poolId);  // Returns object index, -1 if empty
    __declspec(dllexport) void  Framework_Pool_Release(int poolId, int objectIndex);
    __declspec(dllexport) void  Framework_Pool_ReleaseAll(int poolId);  // Return all to pool

    // Pool state queries
    __declspec(dllexport) int   Framework_Pool_GetCapacity(int poolId);
    __declspec(dllexport) int   Framework_Pool_GetActiveCount(int poolId);
    __declspec(dllexport) int   Framework_Pool_GetAvailableCount(int poolId);
    __declspec(dllexport) bool  Framework_Pool_IsEmpty(int poolId);  // No available objects
    __declspec(dllexport) bool  Framework_Pool_IsFull(int poolId);   // All objects in use
    __declspec(dllexport) bool  Framework_Pool_IsObjectActive(int poolId, int objectIndex);

    // Pool statistics
    __declspec(dllexport) int   Framework_Pool_GetTotalAcquires(int poolId);
    __declspec(dllexport) int   Framework_Pool_GetTotalReleases(int poolId);
    __declspec(dllexport) int   Framework_Pool_GetPeakUsage(int poolId);
    __declspec(dllexport) void  Framework_Pool_ResetStats(int poolId);

    // Pre-warming (allocate objects ahead of time)
    __declspec(dllexport) void  Framework_Pool_Warmup(int poolId, int count);
    __declspec(dllexport) void  Framework_Pool_Shrink(int poolId);  // Remove unused capacity

    // Entity pools (special pools that manage ECS entities)
    __declspec(dllexport) int   Framework_Pool_CreateEntityPool(const char* poolName, int prefabId, int initialCapacity, int maxCapacity);
    __declspec(dllexport) int   Framework_Pool_AcquireEntity(int poolId);  // Returns entity ID
    __declspec(dllexport) void  Framework_Pool_ReleaseEntity(int poolId, int entity);

    // Iterate active objects
    __declspec(dllexport) int   Framework_Pool_GetFirstActive(int poolId);  // Returns first active index, -1 if none
    __declspec(dllexport) int   Framework_Pool_GetNextActive(int poolId, int currentIndex);  // Returns next active, -1 if none

    // Bulk operations
    __declspec(dllexport) int   Framework_Pool_AcquireMultiple(int poolId, int count, int* outIndices);  // Returns actual count acquired
    __declspec(dllexport) void  Framework_Pool_ReleaseMultiple(int poolId, int* indices, int count);

    // Global pool management
    __declspec(dllexport) int   Framework_Pool_GetPoolCount();
    __declspec(dllexport) void  Framework_Pool_DestroyAll();
    __declspec(dllexport) void  Framework_Pool_ReleaseAllPools();  // Release all objects in all pools

    // ========================================================================
    // STATE MACHINE - Finite State Machines for game logic and AI
    // ========================================================================

    // State callback types
    typedef void (*StateEnterCallback)(int fsmId, int stateId, int previousState, void* userData);
    typedef void (*StateUpdateCallback)(int fsmId, int stateId, float deltaTime, void* userData);
    typedef void (*StateExitCallback)(int fsmId, int stateId, int nextState, void* userData);
    typedef bool (*TransitionCondition)(int fsmId, int fromState, int toState, void* userData);

    // FSM creation and management
    __declspec(dllexport) int   Framework_FSM_Create(const char* name);
    __declspec(dllexport) int   Framework_FSM_CreateForEntity(const char* name, int entity);
    __declspec(dllexport) void  Framework_FSM_Destroy(int fsmId);
    __declspec(dllexport) int   Framework_FSM_GetByName(const char* name);
    __declspec(dllexport) int   Framework_FSM_GetForEntity(int entity);
    __declspec(dllexport) bool  Framework_FSM_IsValid(int fsmId);

    // State registration
    __declspec(dllexport) int   Framework_FSM_AddState(int fsmId, const char* stateName);
    __declspec(dllexport) int   Framework_FSM_GetState(int fsmId, const char* stateName);
    __declspec(dllexport) const char* Framework_FSM_GetStateName(int fsmId, int stateId);
    __declspec(dllexport) void  Framework_FSM_RemoveState(int fsmId, int stateId);
    __declspec(dllexport) int   Framework_FSM_GetStateCount(int fsmId);

    // State callbacks
    __declspec(dllexport) void  Framework_FSM_SetStateEnter(int fsmId, int stateId, StateEnterCallback callback, void* userData);
    __declspec(dllexport) void  Framework_FSM_SetStateUpdate(int fsmId, int stateId, StateUpdateCallback callback, void* userData);
    __declspec(dllexport) void  Framework_FSM_SetStateExit(int fsmId, int stateId, StateExitCallback callback, void* userData);

    // Transitions
    __declspec(dllexport) int   Framework_FSM_AddTransition(int fsmId, int fromState, int toState);
    __declspec(dllexport) void  Framework_FSM_SetTransitionCondition(int fsmId, int transitionId, TransitionCondition condition, void* userData);
    __declspec(dllexport) void  Framework_FSM_RemoveTransition(int fsmId, int transitionId);
    __declspec(dllexport) bool  Framework_FSM_CanTransition(int fsmId, int fromState, int toState);

    // Any-state transitions (can trigger from any state)
    __declspec(dllexport) int   Framework_FSM_AddAnyTransition(int fsmId, int toState);
    __declspec(dllexport) void  Framework_FSM_SetAnyTransitionCondition(int fsmId, int transitionId, TransitionCondition condition, void* userData);

    // State machine control
    __declspec(dllexport) void  Framework_FSM_SetInitialState(int fsmId, int stateId);
    __declspec(dllexport) void  Framework_FSM_Start(int fsmId);
    __declspec(dllexport) void  Framework_FSM_Stop(int fsmId);
    __declspec(dllexport) void  Framework_FSM_Pause(int fsmId);
    __declspec(dllexport) void  Framework_FSM_Resume(int fsmId);
    __declspec(dllexport) bool  Framework_FSM_IsRunning(int fsmId);
    __declspec(dllexport) bool  Framework_FSM_IsPaused(int fsmId);

    // State queries
    __declspec(dllexport) int   Framework_FSM_GetCurrentState(int fsmId);
    __declspec(dllexport) int   Framework_FSM_GetPreviousState(int fsmId);
    __declspec(dllexport) float Framework_FSM_GetTimeInState(int fsmId);
    __declspec(dllexport) int   Framework_FSM_GetStateChangeCount(int fsmId);

    // Manual transitions
    __declspec(dllexport) bool  Framework_FSM_TransitionTo(int fsmId, int stateId);  // Force transition
    __declspec(dllexport) bool  Framework_FSM_TransitionToByName(int fsmId, const char* stateName);
    __declspec(dllexport) bool  Framework_FSM_TryTransition(int fsmId, int toState);  // Only if condition passes
    __declspec(dllexport) void  Framework_FSM_RevertToPrevious(int fsmId);  // Go back to previous state

    // State history
    __declspec(dllexport) void  Framework_FSM_SetHistorySize(int fsmId, int size);  // How many states to remember
    __declspec(dllexport) int   Framework_FSM_GetHistoryState(int fsmId, int index);  // 0 = most recent
    __declspec(dllexport) int   Framework_FSM_GetHistoryCount(int fsmId);

    // Triggers (named events that can cause transitions)
    __declspec(dllexport) int   Framework_FSM_AddTrigger(int fsmId, const char* triggerName, int fromState, int toState);
    __declspec(dllexport) void  Framework_FSM_FireTrigger(int fsmId, const char* triggerName);
    __declspec(dllexport) void  Framework_FSM_FireTriggerWithData(int fsmId, const char* triggerName, void* data);
    __declspec(dllexport) void  Framework_FSM_RemoveTrigger(int fsmId, int triggerId);

    // Update (call each frame)
    __declspec(dllexport) void  Framework_FSM_Update(int fsmId, float deltaTime);
    __declspec(dllexport) void  Framework_FSM_UpdateAll(float deltaTime);  // Update all FSMs

    // Global FSM management
    __declspec(dllexport) int   Framework_FSM_GetCount();
    __declspec(dllexport) void  Framework_FSM_DestroyAll();
    __declspec(dllexport) void  Framework_FSM_PauseAll();
    __declspec(dllexport) void  Framework_FSM_ResumeAll();

    // Debug
    __declspec(dllexport) void  Framework_FSM_SetDebugEnabled(int fsmId, bool enabled);
    __declspec(dllexport) bool  Framework_FSM_GetDebugEnabled(int fsmId);

    // ========================================================================
    // AI & PATHFINDING - Navigation grids, A* pathfinding, steering behaviors
    // ========================================================================

    // Steering behavior types
    enum SteeringBehavior {
        STEER_NONE = 0,
        STEER_SEEK = 1,
        STEER_FLEE = 2,
        STEER_ARRIVE = 3,
        STEER_PURSUE = 4,
        STEER_EVADE = 5,
        STEER_WANDER = 6,
        STEER_PATH_FOLLOW = 7,
        STEER_OBSTACLE_AVOID = 8,
        STEER_SEPARATION = 9,
        STEER_ALIGNMENT = 10,
        STEER_COHESION = 11
    };

    // Navigation grid creation and management
    __declspec(dllexport) int   Framework_NavGrid_Create(int width, int height, float cellSize);
    __declspec(dllexport) void  Framework_NavGrid_Destroy(int gridId);
    __declspec(dllexport) bool  Framework_NavGrid_IsValid(int gridId);
    __declspec(dllexport) void  Framework_NavGrid_SetOrigin(int gridId, float x, float y);
    __declspec(dllexport) void  Framework_NavGrid_GetOrigin(int gridId, float* outX, float* outY);

    // Grid cell properties
    __declspec(dllexport) void  Framework_NavGrid_SetWalkable(int gridId, int cellX, int cellY, bool walkable);
    __declspec(dllexport) bool  Framework_NavGrid_IsWalkable(int gridId, int cellX, int cellY);
    __declspec(dllexport) void  Framework_NavGrid_SetCost(int gridId, int cellX, int cellY, float cost);
    __declspec(dllexport) float Framework_NavGrid_GetCost(int gridId, int cellX, int cellY);
    __declspec(dllexport) void  Framework_NavGrid_SetAllWalkable(int gridId, bool walkable);
    __declspec(dllexport) void  Framework_NavGrid_SetRect(int gridId, int x, int y, int w, int h, bool walkable);
    __declspec(dllexport) void  Framework_NavGrid_SetCircle(int gridId, int centerX, int centerY, int radius, bool walkable);

    // World to grid coordinate conversion
    __declspec(dllexport) void  Framework_NavGrid_WorldToCell(int gridId, float worldX, float worldY, int* outCellX, int* outCellY);
    __declspec(dllexport) void  Framework_NavGrid_CellToWorld(int gridId, int cellX, int cellY, float* outWorldX, float* outWorldY);
    __declspec(dllexport) bool  Framework_NavGrid_IsWorldPosWalkable(int gridId, float worldX, float worldY);

    // A* Pathfinding
    __declspec(dllexport) int   Framework_Path_Find(int gridId, float startX, float startY, float endX, float endY);  // Returns path handle
    __declspec(dllexport) int   Framework_Path_FindCell(int gridId, int startCellX, int startCellY, int endCellX, int endCellY);
    __declspec(dllexport) void  Framework_Path_Destroy(int pathId);
    __declspec(dllexport) bool  Framework_Path_IsValid(int pathId);
    __declspec(dllexport) int   Framework_Path_GetLength(int pathId);  // Number of waypoints
    __declspec(dllexport) void  Framework_Path_GetWaypoint(int pathId, int index, float* outX, float* outY);
    __declspec(dllexport) float Framework_Path_GetTotalDistance(int pathId);

    // Path smoothing
    __declspec(dllexport) void  Framework_Path_Smooth(int pathId);  // Remove unnecessary waypoints
    __declspec(dllexport) void  Framework_Path_SimplifyRDP(int pathId, float epsilon);  // Ramer-Douglas-Peucker

    // Pathfinding options
    __declspec(dllexport) void  Framework_Path_SetDiagonalEnabled(int gridId, bool enabled);
    __declspec(dllexport) void  Framework_Path_SetDiagonalCost(int gridId, float cost);  // Default 1.414
    __declspec(dllexport) void  Framework_Path_SetHeuristic(int gridId, int heuristic);  // 0=Manhattan, 1=Euclidean, 2=Chebyshev

    // Steering agent creation
    __declspec(dllexport) int   Framework_Steer_CreateAgent(int entity);  // Bind steering to entity
    __declspec(dllexport) void  Framework_Steer_DestroyAgent(int agentId);
    __declspec(dllexport) int   Framework_Steer_GetAgentForEntity(int entity);
    __declspec(dllexport) bool  Framework_Steer_IsAgentValid(int agentId);

    // Agent properties
    __declspec(dllexport) void  Framework_Steer_SetMaxSpeed(int agentId, float maxSpeed);
    __declspec(dllexport) float Framework_Steer_GetMaxSpeed(int agentId);
    __declspec(dllexport) void  Framework_Steer_SetMaxForce(int agentId, float maxForce);
    __declspec(dllexport) float Framework_Steer_GetMaxForce(int agentId);
    __declspec(dllexport) void  Framework_Steer_SetMass(int agentId, float mass);
    __declspec(dllexport) float Framework_Steer_GetMass(int agentId);
    __declspec(dllexport) void  Framework_Steer_SetSlowingRadius(int agentId, float radius);  // For arrive behavior
    __declspec(dllexport) void  Framework_Steer_SetWanderRadius(int agentId, float radius);
    __declspec(dllexport) void  Framework_Steer_SetWanderDistance(int agentId, float distance);
    __declspec(dllexport) void  Framework_Steer_SetWanderJitter(int agentId, float jitter);

    // Velocity
    __declspec(dllexport) void  Framework_Steer_GetVelocity(int agentId, float* outX, float* outY);
    __declspec(dllexport) void  Framework_Steer_SetVelocity(int agentId, float x, float y);

    // Steering behaviors - Enable/Disable
    __declspec(dllexport) void  Framework_Steer_EnableBehavior(int agentId, int behavior, bool enabled);
    __declspec(dllexport) bool  Framework_Steer_IsBehaviorEnabled(int agentId, int behavior);
    __declspec(dllexport) void  Framework_Steer_SetBehaviorWeight(int agentId, int behavior, float weight);
    __declspec(dllexport) float Framework_Steer_GetBehaviorWeight(int agentId, int behavior);

    // Steering targets
    __declspec(dllexport) void  Framework_Steer_SetTargetPosition(int agentId, float x, float y);
    __declspec(dllexport) void  Framework_Steer_SetTargetEntity(int agentId, int targetEntity);
    __declspec(dllexport) void  Framework_Steer_SetPath(int agentId, int pathId);  // For path following
    __declspec(dllexport) void  Framework_Steer_SetPathOffset(int agentId, float offset);  // How far ahead to look

    // Flocking neighbors
    __declspec(dllexport) void  Framework_Steer_SetNeighborRadius(int agentId, float radius);
    __declspec(dllexport) void  Framework_Steer_SetSeparationRadius(int agentId, float radius);

    // Obstacle avoidance
    __declspec(dllexport) void  Framework_Steer_SetAvoidanceRadius(int agentId, float radius);
    __declspec(dllexport) void  Framework_Steer_SetAvoidanceForce(int agentId, float force);

    // Update and calculate steering
    __declspec(dllexport) void  Framework_Steer_Update(int agentId, float deltaTime);
    __declspec(dllexport) void  Framework_Steer_UpdateAll(float deltaTime);  // Update all agents
    __declspec(dllexport) void  Framework_Steer_GetSteeringForce(int agentId, float* outX, float* outY);

    // Path following state
    __declspec(dllexport) int   Framework_Steer_GetCurrentWaypoint(int agentId);
    __declspec(dllexport) bool  Framework_Steer_HasReachedTarget(int agentId);
    __declspec(dllexport) bool  Framework_Steer_HasReachedPathEnd(int agentId);
    __declspec(dllexport) void  Framework_Steer_ResetPath(int agentId);

    // Debug visualization
    __declspec(dllexport) void  Framework_NavGrid_DrawDebug(int gridId);
    __declspec(dllexport) void  Framework_Path_DrawDebug(int pathId, unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Steer_DrawDebug(int agentId);
    __declspec(dllexport) void  Framework_Steer_SetDebugEnabled(int agentId, bool enabled);

    // Global management
    __declspec(dllexport) void  Framework_NavGrid_DestroyAll();
    __declspec(dllexport) void  Framework_Path_DestroyAll();
    __declspec(dllexport) void  Framework_Steer_DestroyAllAgents();

    // ========================================================================
    // DIALOGUE SYSTEM - Branching conversations and dialogue trees
    // ========================================================================

    // Dialogue callback types
    typedef void (*DialogueCallback)(int dialogueId, int nodeId, void* userData);
    typedef void (*DialogueChoiceCallback)(int dialogueId, int nodeId, int choiceIndex, void* userData);
    typedef bool (*DialogueConditionCallback)(int dialogueId, const char* condition, void* userData);

    // Dialogue creation and management
    __declspec(dllexport) int   Framework_Dialogue_Create(const char* name);
    __declspec(dllexport) void  Framework_Dialogue_Destroy(int dialogueId);
    __declspec(dllexport) int   Framework_Dialogue_GetByName(const char* name);
    __declspec(dllexport) bool  Framework_Dialogue_IsValid(int dialogueId);
    __declspec(dllexport) void  Framework_Dialogue_Clear(int dialogueId);  // Remove all nodes

    // Node creation - returns node ID
    __declspec(dllexport) int   Framework_Dialogue_AddNode(int dialogueId, const char* nodeTag);  // Optional tag for identification
    __declspec(dllexport) void  Framework_Dialogue_RemoveNode(int dialogueId, int nodeId);
    __declspec(dllexport) int   Framework_Dialogue_GetNodeByTag(int dialogueId, const char* tag);
    __declspec(dllexport) int   Framework_Dialogue_GetNodeCount(int dialogueId);

    // Node content
    __declspec(dllexport) void  Framework_Dialogue_SetNodeSpeaker(int dialogueId, int nodeId, const char* speaker);
    __declspec(dllexport) const char* Framework_Dialogue_GetNodeSpeaker(int dialogueId, int nodeId);
    __declspec(dllexport) void  Framework_Dialogue_SetNodeText(int dialogueId, int nodeId, const char* text);
    __declspec(dllexport) const char* Framework_Dialogue_GetNodeText(int dialogueId, int nodeId);
    __declspec(dllexport) void  Framework_Dialogue_SetNodePortrait(int dialogueId, int nodeId, int textureHandle);
    __declspec(dllexport) int   Framework_Dialogue_GetNodePortrait(int dialogueId, int nodeId);

    // Node connections - simple linear flow
    __declspec(dllexport) void  Framework_Dialogue_SetNextNode(int dialogueId, int nodeId, int nextNodeId);  // -1 = end dialogue
    __declspec(dllexport) int   Framework_Dialogue_GetNextNode(int dialogueId, int nodeId);
    __declspec(dllexport) void  Framework_Dialogue_SetStartNode(int dialogueId, int nodeId);
    __declspec(dllexport) int   Framework_Dialogue_GetStartNode(int dialogueId);

    // Choices/responses - for branching dialogue
    __declspec(dllexport) int   Framework_Dialogue_AddChoice(int dialogueId, int nodeId, const char* choiceText, int targetNodeId);
    __declspec(dllexport) void  Framework_Dialogue_RemoveChoice(int dialogueId, int nodeId, int choiceIndex);
    __declspec(dllexport) int   Framework_Dialogue_GetChoiceCount(int dialogueId, int nodeId);
    __declspec(dllexport) const char* Framework_Dialogue_GetChoiceText(int dialogueId, int nodeId, int choiceIndex);
    __declspec(dllexport) int   Framework_Dialogue_GetChoiceTarget(int dialogueId, int nodeId, int choiceIndex);
    __declspec(dllexport) void  Framework_Dialogue_SetChoiceCondition(int dialogueId, int nodeId, int choiceIndex, const char* condition);
    __declspec(dllexport) const char* Framework_Dialogue_GetChoiceCondition(int dialogueId, int nodeId, int choiceIndex);

    // Conditional nodes - node only shows if condition is met
    __declspec(dllexport) void  Framework_Dialogue_SetNodeCondition(int dialogueId, int nodeId, const char* condition);
    __declspec(dllexport) const char* Framework_Dialogue_GetNodeCondition(int dialogueId, int nodeId);

    // Node events/triggers
    __declspec(dllexport) void  Framework_Dialogue_SetNodeEvent(int dialogueId, int nodeId, const char* eventName);
    __declspec(dllexport) const char* Framework_Dialogue_GetNodeEvent(int dialogueId, int nodeId);

    // Dialogue variables (for conditions and text substitution)
    __declspec(dllexport) void  Framework_Dialogue_SetVarInt(const char* varName, int value);
    __declspec(dllexport) int   Framework_Dialogue_GetVarInt(const char* varName);
    __declspec(dllexport) void  Framework_Dialogue_SetVarFloat(const char* varName, float value);
    __declspec(dllexport) float Framework_Dialogue_GetVarFloat(const char* varName);
    __declspec(dllexport) void  Framework_Dialogue_SetVarBool(const char* varName, bool value);
    __declspec(dllexport) bool  Framework_Dialogue_GetVarBool(const char* varName);
    __declspec(dllexport) void  Framework_Dialogue_SetVarString(const char* varName, const char* value);
    __declspec(dllexport) const char* Framework_Dialogue_GetVarString(const char* varName);
    __declspec(dllexport) void  Framework_Dialogue_ClearVar(const char* varName);
    __declspec(dllexport) void  Framework_Dialogue_ClearAllVars();

    // Playback - active dialogue state
    __declspec(dllexport) void  Framework_Dialogue_Start(int dialogueId);
    __declspec(dllexport) void  Framework_Dialogue_StartAtNode(int dialogueId, int nodeId);
    __declspec(dllexport) void  Framework_Dialogue_Stop();
    __declspec(dllexport) bool  Framework_Dialogue_IsActive();
    __declspec(dllexport) int   Framework_Dialogue_GetActiveDialogue();
    __declspec(dllexport) int   Framework_Dialogue_GetCurrentNode();

    // Advance dialogue
    __declspec(dllexport) bool  Framework_Dialogue_Continue();  // Move to next node (returns false if ended)
    __declspec(dllexport) bool  Framework_Dialogue_SelectChoice(int choiceIndex);  // Select a choice

    // Current node queries during playback
    __declspec(dllexport) const char* Framework_Dialogue_GetCurrentSpeaker();
    __declspec(dllexport) const char* Framework_Dialogue_GetCurrentText();
    __declspec(dllexport) int   Framework_Dialogue_GetCurrentPortrait();
    __declspec(dllexport) int   Framework_Dialogue_GetCurrentChoiceCount();  // 0 if not a choice node
    __declspec(dllexport) const char* Framework_Dialogue_GetCurrentChoiceText(int choiceIndex);
    __declspec(dllexport) bool  Framework_Dialogue_IsCurrentChoiceAvailable(int choiceIndex);  // Check condition

    // Typewriter effect
    __declspec(dllexport) void  Framework_Dialogue_SetTypewriterEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Dialogue_IsTypewriterEnabled();
    __declspec(dllexport) void  Framework_Dialogue_SetTypewriterSpeed(float charsPerSecond);
    __declspec(dllexport) float Framework_Dialogue_GetTypewriterSpeed();
    __declspec(dllexport) void  Framework_Dialogue_SkipTypewriter();  // Complete text instantly
    __declspec(dllexport) bool  Framework_Dialogue_IsTypewriterComplete();
    __declspec(dllexport) const char* Framework_Dialogue_GetVisibleText();  // Text shown so far
    __declspec(dllexport) int   Framework_Dialogue_GetVisibleCharCount();

    // Callbacks
    __declspec(dllexport) void  Framework_Dialogue_SetOnStartCallback(DialogueCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Dialogue_SetOnEndCallback(DialogueCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Dialogue_SetOnNodeEnterCallback(DialogueCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Dialogue_SetOnNodeExitCallback(DialogueCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Dialogue_SetOnChoiceCallback(DialogueChoiceCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Dialogue_SetConditionHandler(DialogueConditionCallback callback, void* userData);

    // Dialogue update (for typewriter effect)
    __declspec(dllexport) void  Framework_Dialogue_Update(float dt);

    // Speaker management (for portraits and names)
    __declspec(dllexport) void  Framework_Dialogue_RegisterSpeaker(const char* speakerId, const char* displayName, int defaultPortrait);
    __declspec(dllexport) void  Framework_Dialogue_UnregisterSpeaker(const char* speakerId);
    __declspec(dllexport) const char* Framework_Dialogue_GetSpeakerDisplayName(const char* speakerId);
    __declspec(dllexport) int   Framework_Dialogue_GetSpeakerPortrait(const char* speakerId);
    __declspec(dllexport) void  Framework_Dialogue_SetSpeakerPortrait(const char* speakerId, int textureHandle);

    // History/log
    __declspec(dllexport) void  Framework_Dialogue_SetHistoryEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Dialogue_IsHistoryEnabled();
    __declspec(dllexport) int   Framework_Dialogue_GetHistoryCount();
    __declspec(dllexport) const char* Framework_Dialogue_GetHistorySpeaker(int index);
    __declspec(dllexport) const char* Framework_Dialogue_GetHistoryText(int index);
    __declspec(dllexport) void  Framework_Dialogue_ClearHistory();

    // Save/Load dialogue state
    __declspec(dllexport) bool  Framework_Dialogue_SaveToFile(int dialogueId, const char* filename);
    __declspec(dllexport) int   Framework_Dialogue_LoadFromFile(const char* filename);  // Returns dialogue ID

    // Global dialogue management
    __declspec(dllexport) void  Framework_Dialogue_DestroyAll();
    __declspec(dllexport) int   Framework_Dialogue_GetCount();

    // ========================================================================
    // INVENTORY SYSTEM - Item management and equipment
    // ========================================================================

    // Item rarity levels
    enum ItemRarity {
        ITEM_RARITY_COMMON = 0,
        ITEM_RARITY_UNCOMMON = 1,
        ITEM_RARITY_RARE = 2,
        ITEM_RARITY_EPIC = 3,
        ITEM_RARITY_LEGENDARY = 4
    };

    // Equipment slot types
    enum EquipSlot {
        EQUIP_SLOT_NONE = 0,
        EQUIP_SLOT_HEAD = 1,
        EQUIP_SLOT_CHEST = 2,
        EQUIP_SLOT_LEGS = 3,
        EQUIP_SLOT_FEET = 4,
        EQUIP_SLOT_HANDS = 5,
        EQUIP_SLOT_MAIN_HAND = 6,
        EQUIP_SLOT_OFF_HAND = 7,
        EQUIP_SLOT_ACCESSORY1 = 8,
        EQUIP_SLOT_ACCESSORY2 = 9
    };

    // Inventory callbacks
    typedef void (*InventoryCallback)(int inventoryId, int slotIndex, int itemId, void* userData);
    typedef void (*ItemUseCallback)(int inventoryId, int slotIndex, int itemId, int quantity, void* userData);
    typedef bool (*ItemDropCallback)(int inventoryId, int slotIndex, int itemId, int quantity, void* userData);

    // ---- Item Definition (template/prototype) ----
    __declspec(dllexport) int   Framework_Item_Define(const char* itemName, const char* description);
    __declspec(dllexport) void  Framework_Item_Undefine(int itemDefId);
    __declspec(dllexport) int   Framework_Item_GetDefByName(const char* itemName);
    __declspec(dllexport) bool  Framework_Item_IsDefValid(int itemDefId);

    // Item definition properties
    __declspec(dllexport) const char* Framework_Item_GetName(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetDisplayName(int itemDefId, const char* displayName);
    __declspec(dllexport) const char* Framework_Item_GetDisplayName(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetDescription(int itemDefId, const char* description);
    __declspec(dllexport) const char* Framework_Item_GetDescription(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetIcon(int itemDefId, int textureHandle);
    __declspec(dllexport) int   Framework_Item_GetIcon(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetIconRect(int itemDefId, float x, float y, float w, float h);

    // Stacking
    __declspec(dllexport) void  Framework_Item_SetStackable(int itemDefId, bool stackable, int maxStack);
    __declspec(dllexport) bool  Framework_Item_IsStackable(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetMaxStack(int itemDefId, int maxStack);
    __declspec(dllexport) int   Framework_Item_GetMaxStack(int itemDefId);

    // Category and rarity
    __declspec(dllexport) void  Framework_Item_SetCategory(int itemDefId, const char* category);
    __declspec(dllexport) const char* Framework_Item_GetCategory(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetRarity(int itemDefId, int rarity);
    __declspec(dllexport) int   Framework_Item_GetRarity(int itemDefId);

    // Equipment properties
    __declspec(dllexport) void  Framework_Item_SetEquipSlot(int itemDefId, int equipSlot);
    __declspec(dllexport) int   Framework_Item_GetEquipSlot(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetUsable(int itemDefId, bool usable);
    __declspec(dllexport) bool  Framework_Item_IsUsable(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetConsumable(int itemDefId, bool consumable);
    __declspec(dllexport) bool  Framework_Item_IsConsumable(int itemDefId);

    // Item stats (generic key-value for RPG stats)
    __declspec(dllexport) void  Framework_Item_SetStatInt(int itemDefId, const char* statName, int value);
    __declspec(dllexport) int   Framework_Item_GetStatInt(int itemDefId, const char* statName);
    __declspec(dllexport) void  Framework_Item_SetStatFloat(int itemDefId, const char* statName, float value);
    __declspec(dllexport) float Framework_Item_GetStatFloat(int itemDefId, const char* statName);

    // Value/price
    __declspec(dllexport) void  Framework_Item_SetValue(int itemDefId, int value);
    __declspec(dllexport) int   Framework_Item_GetValue(int itemDefId);
    __declspec(dllexport) void  Framework_Item_SetWeight(int itemDefId, float weight);
    __declspec(dllexport) float Framework_Item_GetWeight(int itemDefId);

    // ---- Inventory Container ----
    __declspec(dllexport) int   Framework_Inventory_Create(const char* name, int slotCount);
    __declspec(dllexport) void  Framework_Inventory_Destroy(int inventoryId);
    __declspec(dllexport) int   Framework_Inventory_GetByName(const char* name);
    __declspec(dllexport) bool  Framework_Inventory_IsValid(int inventoryId);

    // Inventory properties
    __declspec(dllexport) void  Framework_Inventory_SetSlotCount(int inventoryId, int slotCount);
    __declspec(dllexport) int   Framework_Inventory_GetSlotCount(int inventoryId);
    __declspec(dllexport) int   Framework_Inventory_GetCapacity(int inventoryId);  // Alias for GetSlotCount
    __declspec(dllexport) void  Framework_Inventory_SetMaxWeight(int inventoryId, float maxWeight);
    __declspec(dllexport) float Framework_Inventory_GetMaxWeight(int inventoryId);
    __declspec(dllexport) float Framework_Inventory_GetCurrentWeight(int inventoryId);
    __declspec(dllexport) bool  Framework_Inventory_IsWeightLimited(int inventoryId);
    __declspec(dllexport) void  Framework_Inventory_SetAutoStack(int inventoryId, bool autoStack);
    __declspec(dllexport) bool  Framework_Inventory_GetAutoStack(int inventoryId);

    // Adding items
    __declspec(dllexport) bool  Framework_Inventory_AddItem(int inventoryId, int itemDefId, int quantity);  // Auto-stack/find slot
    __declspec(dllexport) bool  Framework_Inventory_AddItemToSlot(int inventoryId, int slotIndex, int itemDefId, int quantity);
    __declspec(dllexport) int   Framework_Inventory_AddItemGetRemaining(int inventoryId, int itemDefId, int quantity);  // Returns leftover

    // Removing items
    __declspec(dllexport) bool  Framework_Inventory_RemoveItem(int inventoryId, int itemDefId, int quantity);  // From any slot
    __declspec(dllexport) bool  Framework_Inventory_RemoveItemFromSlot(int inventoryId, int slotIndex, int quantity);
    __declspec(dllexport) int   Framework_Inventory_RemoveFromSlot(int inventoryId, int slotIndex, int quantity);  // Alias, returns remaining
    __declspec(dllexport) void  Framework_Inventory_ClearSlot(int inventoryId, int slotIndex);
    __declspec(dllexport) void  Framework_Inventory_Clear(int inventoryId);

    // Slot queries
    __declspec(dllexport) int   Framework_Inventory_GetItemAt(int inventoryId, int slotIndex);  // Returns itemDefId or -1
    __declspec(dllexport) int   Framework_Inventory_GetSlotItem(int inventoryId, int slotIndex);  // Alias for GetItemAt
    __declspec(dllexport) int   Framework_Inventory_GetQuantityAt(int inventoryId, int slotIndex);
    __declspec(dllexport) int   Framework_Inventory_GetSlotQuantity(int inventoryId, int slotIndex);  // Alias for GetQuantityAt
    __declspec(dllexport) bool  Framework_Inventory_IsSlotEmpty(int inventoryId, int slotIndex);
    __declspec(dllexport) int   Framework_Inventory_GetFirstEmptySlot(int inventoryId);
    __declspec(dllexport) int   Framework_Inventory_GetEmptySlotCount(int inventoryId);
    __declspec(dllexport) int   Framework_Inventory_GetUsedSlotCount(int inventoryId);

    // Item queries
    __declspec(dllexport) bool  Framework_Inventory_HasItem(int inventoryId, int itemDefId);
    __declspec(dllexport) int   Framework_Inventory_CountItem(int inventoryId, int itemDefId);  // Total quantity
    __declspec(dllexport) int   Framework_Inventory_FindItem(int inventoryId, int itemDefId);  // Returns first slot index or -1
    __declspec(dllexport) int   Framework_Inventory_FindItemByCategory(int inventoryId, const char* category);

    // Moving/swapping items
    __declspec(dllexport) bool  Framework_Inventory_MoveItem(int inventoryId, int fromSlot, int toSlot);
    __declspec(dllexport) bool  Framework_Inventory_SwapSlots(int inventoryId, int slotA, int slotB);
    __declspec(dllexport) bool  Framework_Inventory_TransferItem(int fromInvId, int fromSlot, int toInvId, int toSlot, int quantity);
    __declspec(dllexport) bool  Framework_Inventory_SplitStack(int inventoryId, int slotIndex, int quantity, int targetSlot);

    // Sorting
    __declspec(dllexport) void  Framework_Inventory_Sort(int inventoryId, int sortMode);  // 0=name, 1=rarity, 2=value, 3=weight
    __declspec(dllexport) void  Framework_Inventory_SortByRarity(int inventoryId);
    __declspec(dllexport) void  Framework_Inventory_Compact(int inventoryId);  // Move items to fill gaps

    // Using items
    __declspec(dllexport) bool  Framework_Inventory_UseItem(int inventoryId, int slotIndex);
    __declspec(dllexport) void  Framework_Inventory_SetUseCallback(int inventoryId, ItemUseCallback callback, void* userData);

    // ---- Equipment System ----
    __declspec(dllexport) int   Framework_Equipment_Create(const char* name);
    __declspec(dllexport) void  Framework_Equipment_Destroy(int equipId);
    __declspec(dllexport) int   Framework_Equipment_GetByName(const char* name);
    __declspec(dllexport) bool  Framework_Equipment_IsValid(int equipId);

    // Equip/unequip
    __declspec(dllexport) bool  Framework_Equipment_Equip(int equipId, int itemDefId, int slot);
    __declspec(dllexport) bool  Framework_Equipment_EquipFromInventory(int equipId, int inventoryId, int invSlot, int equipSlot);
    __declspec(dllexport) int   Framework_Equipment_Unequip(int equipId, int slot);  // Returns itemDefId
    __declspec(dllexport) bool  Framework_Equipment_UnequipToInventory(int equipId, int slot, int inventoryId);
    __declspec(dllexport) void  Framework_Equipment_UnequipAll(int equipId);

    // Equipment queries
    __declspec(dllexport) int   Framework_Equipment_GetItemAt(int equipId, int slot);  // Returns itemDefId or -1
    __declspec(dllexport) bool  Framework_Equipment_IsSlotEmpty(int equipId, int slot);
    __declspec(dllexport) bool  Framework_Equipment_CanEquip(int equipId, int itemDefId, int slot);

    // Equipment stats (sum of all equipped item stats)
    __declspec(dllexport) int   Framework_Equipment_GetTotalStatInt(int equipId, const char* statName);
    __declspec(dllexport) float Framework_Equipment_GetTotalStatFloat(int equipId, const char* statName);

    // ---- Callbacks ----
    __declspec(dllexport) void  Framework_Inventory_SetOnAddCallback(int inventoryId, InventoryCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Inventory_SetOnRemoveCallback(int inventoryId, InventoryCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Inventory_SetOnChangeCallback(int inventoryId, InventoryCallback callback, void* userData);
    __declspec(dllexport) void  Framework_Inventory_SetDropCallback(int inventoryId, ItemDropCallback callback, void* userData);

    // ---- Loot Tables ----
    __declspec(dllexport) int   Framework_LootTable_Create(const char* name);
    __declspec(dllexport) void  Framework_LootTable_Destroy(int tableId);
    __declspec(dllexport) void  Framework_LootTable_AddEntry(int tableId, int itemDefId, float weight, int minQty, int maxQty);
    __declspec(dllexport) void  Framework_LootTable_RemoveEntry(int tableId, int itemDefId);
    __declspec(dllexport) int   Framework_LootTable_Roll(int tableId, int* outQuantity);  // Returns itemDefId
    __declspec(dllexport) void  Framework_LootTable_RollMultiple(int tableId, int rolls, int* outItems, int* outQuantities, int bufferSize);

    // ---- Save/Load ----
    __declspec(dllexport) bool  Framework_Inventory_SaveToSlot(int inventoryId, int saveSlot, const char* key);
    __declspec(dllexport) bool  Framework_Inventory_LoadFromSlot(int inventoryId, int saveSlot, const char* key);
    __declspec(dllexport) bool  Framework_Equipment_SaveToSlot(int equipId, int saveSlot, const char* key);
    __declspec(dllexport) bool  Framework_Equipment_LoadFromSlot(int equipId, int saveSlot, const char* key);

    // ---- Global Management ----
    __declspec(dllexport) void  Framework_Item_UndefineAll();
    __declspec(dllexport) void  Framework_Inventory_DestroyAll();
    __declspec(dllexport) void  Framework_Equipment_DestroyAll();
    __declspec(dllexport) void  Framework_LootTable_DestroyAll();
    __declspec(dllexport) int   Framework_Item_GetDefCount();
    __declspec(dllexport) int   Framework_Inventory_GetCount();
    __declspec(dllexport) int   Framework_Equipment_GetCount();

    // ========================================================================
    // QUEST SYSTEM
    // ========================================================================
    // Quest and objective management for tracking player progress

    // Quest States
    #define QUEST_STATE_NOT_STARTED  0
    #define QUEST_STATE_IN_PROGRESS  1
    #define QUEST_STATE_COMPLETED    2
    #define QUEST_STATE_FAILED       3

    // Objective Types
    #define OBJECTIVE_TYPE_CUSTOM    0   // Generic, manual completion
    #define OBJECTIVE_TYPE_KILL      1   // Kill X of target type
    #define OBJECTIVE_TYPE_COLLECT   2   // Collect X of item type
    #define OBJECTIVE_TYPE_TALK      3   // Talk to specific NPC
    #define OBJECTIVE_TYPE_REACH     4   // Reach a location
    #define OBJECTIVE_TYPE_INTERACT  5   // Interact with object
    #define OBJECTIVE_TYPE_ESCORT    6   // Escort entity to location
    #define OBJECTIVE_TYPE_DEFEND    7   // Defend for X seconds
    #define OBJECTIVE_TYPE_EXPLORE   8   // Discover X locations

    // Quest callback types
    typedef void (*QuestStateCallback)(int questId, int newState);
    typedef void (*ObjectiveUpdateCallback)(int questId, int objectiveIndex, int currentProgress, int requiredProgress);

    // ---- Quest Definition ----
    __declspec(dllexport) int   Framework_Quest_Define(const char* questId);
    __declspec(dllexport) void  Framework_Quest_SetName(int questHandle, const char* name);
    __declspec(dllexport) void  Framework_Quest_SetDescription(int questHandle, const char* description);
    __declspec(dllexport) void  Framework_Quest_SetCategory(int questHandle, const char* category);
    __declspec(dllexport) void  Framework_Quest_SetLevel(int questHandle, int level);
    __declspec(dllexport) void  Framework_Quest_SetRepeatable(int questHandle, bool repeatable);
    __declspec(dllexport) void  Framework_Quest_SetAutoComplete(int questHandle, bool autoComplete);
    __declspec(dllexport) void  Framework_Quest_SetHidden(int questHandle, bool hidden);
    __declspec(dllexport) void  Framework_Quest_SetTimeLimit(int questHandle, float seconds);

    // ---- Quest Prerequisites ----
    __declspec(dllexport) void  Framework_Quest_AddPrerequisite(int questHandle, const char* requiredQuestId);
    __declspec(dllexport) void  Framework_Quest_SetMinLevel(int questHandle, int minLevel);
    __declspec(dllexport) bool  Framework_Quest_CheckPrerequisites(int questHandle);

    // ---- Objectives ----
    __declspec(dllexport) int   Framework_Quest_AddObjective(int questHandle, int objectiveType, const char* description, int requiredCount);
    __declspec(dllexport) void  Framework_Quest_SetObjectiveTarget(int questHandle, int objectiveIndex, const char* targetId);
    __declspec(dllexport) void  Framework_Quest_SetObjectiveLocation(int questHandle, int objectiveIndex, float x, float y, float radius);
    __declspec(dllexport) void  Framework_Quest_SetObjectiveOptional(int questHandle, int objectiveIndex, bool optional);
    __declspec(dllexport) void  Framework_Quest_SetObjectiveHidden(int questHandle, int objectiveIndex, bool hidden);
    __declspec(dllexport) int   Framework_Quest_GetObjectiveCount(int questHandle);
    __declspec(dllexport) const char* Framework_Quest_GetObjectiveDescription(int questHandle, int objectiveIndex);
    __declspec(dllexport) int   Framework_Quest_GetObjectiveType(int questHandle, int objectiveIndex);
    __declspec(dllexport) int   Framework_Quest_GetObjectiveProgress(int questHandle, int objectiveIndex);
    __declspec(dllexport) int   Framework_Quest_GetObjectiveRequired(int questHandle, int objectiveIndex);
    __declspec(dllexport) bool  Framework_Quest_IsObjectiveComplete(int questHandle, int objectiveIndex);

    // ---- Rewards ----
    __declspec(dllexport) void  Framework_Quest_AddRewardItem(int questHandle, int itemDefId, int quantity);
    __declspec(dllexport) void  Framework_Quest_SetRewardExperience(int questHandle, int experience);
    __declspec(dllexport) void  Framework_Quest_SetRewardCurrency(int questHandle, int currencyType, int amount);
    __declspec(dllexport) void  Framework_Quest_AddRewardUnlock(int questHandle, const char* unlockId);

    // ---- Quest State Management ----
    __declspec(dllexport) bool  Framework_Quest_Start(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_Complete(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_Fail(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_Abandon(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_Reset(int questHandle);
    __declspec(dllexport) int   Framework_Quest_GetState(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_IsActive(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_IsCompleted(int questHandle);
    __declspec(dllexport) bool  Framework_Quest_CanStart(int questHandle);

    // ---- Progress Tracking ----
    __declspec(dllexport) void  Framework_Quest_SetObjectiveProgress(int questHandle, int objectiveIndex, int progress);
    __declspec(dllexport) void  Framework_Quest_AddObjectiveProgress(int questHandle, int objectiveIndex, int amount);
    __declspec(dllexport) float Framework_Quest_GetCompletionPercent(int questHandle);

    // ---- Auto-Progress Reporting ----
    __declspec(dllexport) void  Framework_Quest_ReportKill(const char* targetType, int count);
    __declspec(dllexport) void  Framework_Quest_ReportCollect(int itemDefId, int count);
    __declspec(dllexport) void  Framework_Quest_ReportTalk(const char* npcId);
    __declspec(dllexport) void  Framework_Quest_ReportLocation(float x, float y);
    __declspec(dllexport) void  Framework_Quest_ReportInteract(const char* objectId);
    __declspec(dllexport) void  Framework_Quest_ReportCustom(const char* eventType, const char* eventData);

    // ---- Quest Queries ----
    __declspec(dllexport) int   Framework_Quest_GetByStringId(const char* questId);
    __declspec(dllexport) const char* Framework_Quest_GetName(int questHandle);
    __declspec(dllexport) const char* Framework_Quest_GetDescription(int questHandle);
    __declspec(dllexport) const char* Framework_Quest_GetCategory(int questHandle);
    __declspec(dllexport) const char* Framework_Quest_GetStringId(int questHandle);
    __declspec(dllexport) int   Framework_Quest_GetLevel(int questHandle);
    __declspec(dllexport) float Framework_Quest_GetTimeRemaining(int questHandle);
    __declspec(dllexport) float Framework_Quest_GetTimeElapsed(int questHandle);

    // ---- Active Quest List ----
    __declspec(dllexport) int   Framework_Quest_GetActiveCount();
    __declspec(dllexport) int   Framework_Quest_GetActiveAt(int index);
    __declspec(dllexport) int   Framework_Quest_GetCompletedCount();
    __declspec(dllexport) int   Framework_Quest_GetCompletedAt(int index);
    __declspec(dllexport) int   Framework_Quest_GetAvailableCount();
    __declspec(dllexport) int   Framework_Quest_GetAvailableAt(int index);

    // ---- Quest Tracking (HUD) ----
    __declspec(dllexport) void  Framework_Quest_SetTracked(int questHandle, bool tracked);
    __declspec(dllexport) bool  Framework_Quest_IsTracked(int questHandle);
    __declspec(dllexport) int   Framework_Quest_GetTrackedCount();
    __declspec(dllexport) int   Framework_Quest_GetTrackedAt(int index);
    __declspec(dllexport) void  Framework_Quest_SetMaxTracked(int maxTracked);

    // ---- Callbacks ----
    __declspec(dllexport) void  Framework_Quest_SetOnStateChange(QuestStateCallback callback);
    __declspec(dllexport) void  Framework_Quest_SetOnObjectiveUpdate(ObjectiveUpdateCallback callback);

    // ---- Quest Chains ----
    __declspec(dllexport) int   Framework_QuestChain_Create(const char* chainId);
    __declspec(dllexport) void  Framework_QuestChain_AddQuest(int chainHandle, int questHandle);
    __declspec(dllexport) int   Framework_QuestChain_GetCurrentQuest(int chainHandle);
    __declspec(dllexport) int   Framework_QuestChain_GetProgress(int chainHandle);
    __declspec(dllexport) int   Framework_QuestChain_GetLength(int chainHandle);
    __declspec(dllexport) bool  Framework_QuestChain_IsComplete(int chainHandle);

    // ---- Save/Load ----
    __declspec(dllexport) bool  Framework_Quest_SaveProgress(int saveSlot, const char* key);
    __declspec(dllexport) bool  Framework_Quest_LoadProgress(int saveSlot, const char* key);

    // ---- Global Management ----
    __declspec(dllexport) void  Framework_Quest_Update(float deltaTime);
    __declspec(dllexport) void  Framework_Quest_UndefineAll();
    __declspec(dllexport) void  Framework_Quest_ResetAllProgress();
    __declspec(dllexport) int   Framework_Quest_GetDefinedCount();

    // ========================================================================
    // 2D LIGHTING SYSTEM
    // ========================================================================
    // Dynamic 2D lighting with shadows for atmospheric effects

    // Light Types
    #define LIGHT_TYPE_POINT       0   // Radial light (torch, lamp)
    #define LIGHT_TYPE_SPOT        1   // Directional cone (flashlight)
    #define LIGHT_TYPE_DIRECTIONAL 2   // Global directional (sun/moon)

    // Shadow Quality
    #define SHADOW_QUALITY_NONE    0   // No shadows
    #define SHADOW_QUALITY_HARD    1   // Hard-edged shadows
    #define SHADOW_QUALITY_SOFT    2   // Soft shadows with blur

    // Blend Modes for lighting
    #define LIGHT_BLEND_ADDITIVE   0   // Add light to scene
    #define LIGHT_BLEND_MULTIPLY   1   // Multiply with scene

    // ---- Lighting System Control ----
    __declspec(dllexport) void  Framework_Lighting_Initialize(int width, int height);
    __declspec(dllexport) void  Framework_Lighting_Shutdown();
    __declspec(dllexport) void  Framework_Lighting_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Lighting_IsEnabled();
    __declspec(dllexport) void  Framework_Lighting_SetResolution(int width, int height);

    // ---- Ambient Light ----
    __declspec(dllexport) void  Framework_Lighting_SetAmbientColor(unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Lighting_SetAmbientIntensity(float intensity);
    __declspec(dllexport) float Framework_Lighting_GetAmbientIntensity();

    // ---- Point Lights ----
    __declspec(dllexport) int   Framework_Light_CreatePoint(float x, float y, float radius);
    __declspec(dllexport) void  Framework_Light_Destroy(int lightId);
    __declspec(dllexport) void  Framework_Light_SetPosition(int lightId, float x, float y);
    __declspec(dllexport) void  Framework_Light_GetPosition(int lightId, float* x, float* y);
    __declspec(dllexport) void  Framework_Light_SetColor(int lightId, unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Light_SetIntensity(int lightId, float intensity);
    __declspec(dllexport) float Framework_Light_GetIntensity(int lightId);
    __declspec(dllexport) void  Framework_Light_SetRadius(int lightId, float radius);
    __declspec(dllexport) float Framework_Light_GetRadius(int lightId);
    __declspec(dllexport) void  Framework_Light_SetEnabled(int lightId, bool enabled);
    __declspec(dllexport) bool  Framework_Light_IsEnabled(int lightId);

    // ---- Spot Lights ----
    __declspec(dllexport) int   Framework_Light_CreateSpot(float x, float y, float radius, float angle, float coneAngle);
    __declspec(dllexport) void  Framework_Light_SetDirection(int lightId, float angle);
    __declspec(dllexport) float Framework_Light_GetDirection(int lightId);
    __declspec(dllexport) void  Framework_Light_SetConeAngle(int lightId, float angle);
    __declspec(dllexport) float Framework_Light_GetConeAngle(int lightId);
    __declspec(dllexport) void  Framework_Light_SetSoftEdge(int lightId, float softness);

    // ---- Directional Light (Global) ----
    __declspec(dllexport) void  Framework_Lighting_SetDirectionalAngle(float angle);
    __declspec(dllexport) void  Framework_Lighting_SetDirectionalColor(unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Lighting_SetDirectionalIntensity(float intensity);
    __declspec(dllexport) void  Framework_Lighting_SetDirectionalEnabled(bool enabled);

    // ---- Light Properties ----
    __declspec(dllexport) void  Framework_Light_SetFalloff(int lightId, float falloff);
    __declspec(dllexport) float Framework_Light_GetFalloff(int lightId);
    __declspec(dllexport) void  Framework_Light_SetFlicker(int lightId, float amount, float speed);
    __declspec(dllexport) void  Framework_Light_SetPulse(int lightId, float minIntensity, float maxIntensity, float speed);
    __declspec(dllexport) void  Framework_Light_SetLayer(int lightId, int layer);
    __declspec(dllexport) int   Framework_Light_GetLayer(int lightId);

    // ---- Light Attachment ----
    __declspec(dllexport) void  Framework_Light_AttachToEntity(int lightId, int entityId, float offsetX, float offsetY);
    __declspec(dllexport) void  Framework_Light_Detach(int lightId);

    // ---- Shadow Occluders ----
    __declspec(dllexport) int   Framework_Shadow_CreateBox(float x, float y, float width, float height);
    __declspec(dllexport) int   Framework_Shadow_CreateCircle(float x, float y, float radius);
    __declspec(dllexport) int   Framework_Shadow_CreatePolygon(const float* points, int pointCount);
    __declspec(dllexport) void  Framework_Shadow_Destroy(int occluderId);
    __declspec(dllexport) void  Framework_Shadow_SetPosition(int occluderId, float x, float y);
    __declspec(dllexport) void  Framework_Shadow_SetRotation(int occluderId, float angle);
    __declspec(dllexport) void  Framework_Shadow_SetEnabled(int occluderId, bool enabled);
    __declspec(dllexport) void  Framework_Shadow_AttachToEntity(int occluderId, int entityId, float offsetX, float offsetY);
    __declspec(dllexport) void  Framework_Shadow_Detach(int occluderId);

    // ---- Shadow Settings ----
    __declspec(dllexport) void  Framework_Lighting_SetShadowQuality(int quality);
    __declspec(dllexport) int   Framework_Lighting_GetShadowQuality();
    __declspec(dllexport) void  Framework_Lighting_SetShadowBlur(float blur);
    __declspec(dllexport) void  Framework_Lighting_SetShadowColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a);

    // ---- Day/Night Cycle ----
    __declspec(dllexport) void  Framework_Lighting_SetTimeOfDay(float time);  // 0-24 hours
    __declspec(dllexport) float Framework_Lighting_GetTimeOfDay();
    __declspec(dllexport) void  Framework_Lighting_SetDayNightSpeed(float speed);  // 1.0 = real time
    __declspec(dllexport) void  Framework_Lighting_SetDayNightEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Lighting_SetSunriseTime(float hour);
    __declspec(dllexport) void  Framework_Lighting_SetSunsetTime(float hour);
    __declspec(dllexport) void  Framework_Lighting_SetDayAmbient(unsigned char r, unsigned char g, unsigned char b, float intensity);
    __declspec(dllexport) void  Framework_Lighting_SetNightAmbient(unsigned char r, unsigned char g, unsigned char b, float intensity);

    // ---- Rendering ----
    __declspec(dllexport) void  Framework_Lighting_BeginLightPass();   // Call before drawing lit objects
    __declspec(dllexport) void  Framework_Lighting_EndLightPass();     // Call after drawing lit objects
    __declspec(dllexport) void  Framework_Lighting_RenderToScreen();   // Apply lighting to final output
    __declspec(dllexport) void  Framework_Lighting_Update(float deltaTime);

    // ---- Light Queries ----
    __declspec(dllexport) int   Framework_Light_GetCount();
    __declspec(dllexport) int   Framework_Light_GetAt(int index);
    __declspec(dllexport) int   Framework_Light_GetType(int lightId);
    __declspec(dllexport) float Framework_Light_GetBrightnessAt(float x, float y);  // Sample light intensity at point

    // ---- Global Management ----
    __declspec(dllexport) void  Framework_Light_DestroyAll();
    __declspec(dllexport) void  Framework_Shadow_DestroyAll();

    // ========================================================================
    // SCREEN EFFECTS SYSTEM
    // ========================================================================
    // Post-processing visual effects for screen-wide transformations

    // Effect Types (for queries)
    #define EFFECT_VIGNETTE         0
    #define EFFECT_BLUR             1
    #define EFFECT_CHROMATIC        2
    #define EFFECT_PIXELATE         3
    #define EFFECT_SCANLINES        4
    #define EFFECT_CRT              5
    #define EFFECT_GRAYSCALE        6
    #define EFFECT_SEPIA            7
    #define EFFECT_INVERT           8
    #define EFFECT_TINT             9
    #define EFFECT_BRIGHTNESS       10
    #define EFFECT_CONTRAST         11
    #define EFFECT_SATURATION       12
    #define EFFECT_FILMGRAIN        13

    // ---- System Control ----
    __declspec(dllexport) void  Framework_Effects_Initialize(int width, int height);
    __declspec(dllexport) void  Framework_Effects_Shutdown();
    __declspec(dllexport) void  Framework_Effects_SetEnabled(bool enabled);
    __declspec(dllexport) bool  Framework_Effects_IsEnabled();
    __declspec(dllexport) void  Framework_Effects_SetResolution(int width, int height);

    // ---- Vignette Effect ----
    __declspec(dllexport) void  Framework_Effects_SetVignetteEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetVignetteIntensity(float intensity);
    __declspec(dllexport) void  Framework_Effects_SetVignetteRadius(float radius);
    __declspec(dllexport) void  Framework_Effects_SetVignetteSoftness(float softness);
    __declspec(dllexport) void  Framework_Effects_SetVignetteColor(unsigned char r, unsigned char g, unsigned char b);

    // ---- Blur Effect ----
    __declspec(dllexport) void  Framework_Effects_SetBlurEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetBlurAmount(float amount);
    __declspec(dllexport) void  Framework_Effects_SetBlurIterations(int iterations);

    // ---- Chromatic Aberration ----
    __declspec(dllexport) void  Framework_Effects_SetChromaticEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetChromaticOffset(float offset);
    __declspec(dllexport) void  Framework_Effects_SetChromaticAngle(float angle);

    // ---- Pixelate Effect ----
    __declspec(dllexport) void  Framework_Effects_SetPixelateEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetPixelateSize(int pixelSize);

    // ---- Scanlines Effect ----
    __declspec(dllexport) void  Framework_Effects_SetScanlinesEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetScanlinesIntensity(float intensity);
    __declspec(dllexport) void  Framework_Effects_SetScanlinesCount(int count);
    __declspec(dllexport) void  Framework_Effects_SetScanlinesSpeed(float speed);

    // ---- CRT Effect ----
    __declspec(dllexport) void  Framework_Effects_SetCRTEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetCRTCurvature(float curvature);
    __declspec(dllexport) void  Framework_Effects_SetCRTVignetteIntensity(float intensity);

    // ---- Color Effects ----
    __declspec(dllexport) void  Framework_Effects_SetGrayscaleEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetGrayscaleAmount(float amount);
    __declspec(dllexport) void  Framework_Effects_SetSepiaEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetSepiaAmount(float amount);
    __declspec(dllexport) void  Framework_Effects_SetInvertEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetInvertAmount(float amount);

    // ---- Color Grading ----
    __declspec(dllexport) void  Framework_Effects_SetTintEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetTintColor(unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) void  Framework_Effects_SetTintAmount(float amount);
    __declspec(dllexport) void  Framework_Effects_SetBrightness(float brightness);
    __declspec(dllexport) void  Framework_Effects_SetContrast(float contrast);
    __declspec(dllexport) void  Framework_Effects_SetSaturation(float saturation);
    __declspec(dllexport) void  Framework_Effects_SetGamma(float gamma);

    // ---- Film Grain / Noise ----
    __declspec(dllexport) void  Framework_Effects_SetFilmGrainEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Effects_SetFilmGrainIntensity(float intensity);
    __declspec(dllexport) void  Framework_Effects_SetFilmGrainSpeed(float speed);

    // ---- Screen Flash ----
    __declspec(dllexport) void  Framework_Effects_Flash(unsigned char r, unsigned char g, unsigned char b, float duration);
    __declspec(dllexport) void  Framework_Effects_FlashWhite(float duration);
    __declspec(dllexport) void  Framework_Effects_FlashDamage(float duration);
    __declspec(dllexport) bool  Framework_Effects_IsFlashing();

    // ---- Screen Fade ----
    __declspec(dllexport) void  Framework_Effects_FadeIn(float duration);
    __declspec(dllexport) void  Framework_Effects_FadeOut(float duration);
    __declspec(dllexport) void  Framework_Effects_FadeToColor(unsigned char r, unsigned char g, unsigned char b, float duration);
    __declspec(dllexport) void  Framework_Effects_SetFadeColor(unsigned char r, unsigned char g, unsigned char b);
    __declspec(dllexport) float Framework_Effects_GetFadeAmount();
    __declspec(dllexport) bool  Framework_Effects_IsFading();

    // ---- Screen Shake ----
    __declspec(dllexport) void  Framework_Effects_Shake(float intensity, float duration);
    __declspec(dllexport) void  Framework_Effects_ShakeDecay(float intensity, float duration, float decay);
    __declspec(dllexport) void  Framework_Effects_StopShake();
    __declspec(dllexport) bool  Framework_Effects_IsShaking();
    __declspec(dllexport) void  Framework_Effects_GetShakeOffset(float* x, float* y);

    // ---- Rendering ----
    __declspec(dllexport) void  Framework_Effects_BeginCapture();   // Begin capturing scene to buffer
    __declspec(dllexport) void  Framework_Effects_EndCapture();     // End capture
    __declspec(dllexport) void  Framework_Effects_Apply();          // Apply all effects and render to screen
    __declspec(dllexport) void  Framework_Effects_DrawOverlays(int screenWidth, int screenHeight);  // Draw overlays without render texture
    __declspec(dllexport) void  Framework_Effects_Update(float deltaTime);

    // ---- Presets ----
    __declspec(dllexport) void  Framework_Effects_ApplyPresetRetro();
    __declspec(dllexport) void  Framework_Effects_ApplyPresetDream();
    __declspec(dllexport) void  Framework_Effects_ApplyPresetHorror();
    __declspec(dllexport) void  Framework_Effects_ApplyPresetNoir();
    __declspec(dllexport) void  Framework_Effects_ResetAll();

    // ========================================================================
    // LOCALIZATION SYSTEM
    // ========================================================================
    // Multi-language support with string tables and language switching

    // ---- System Control ----
    __declspec(dllexport) void  Framework_Locale_Initialize();
    __declspec(dllexport) void  Framework_Locale_Shutdown();
    __declspec(dllexport) bool  Framework_Locale_LoadLanguage(const char* languageCode, const char* filePath);
    __declspec(dllexport) bool  Framework_Locale_SetLanguage(const char* languageCode);
    __declspec(dllexport) const char* Framework_Locale_GetCurrentLanguage();
    __declspec(dllexport) int   Framework_Locale_GetLanguageCount();
    __declspec(dllexport) const char* Framework_Locale_GetLanguageAt(int index);

    // ---- String Retrieval ----
    __declspec(dllexport) const char* Framework_Locale_GetString(const char* key);
    __declspec(dllexport) const char* Framework_Locale_GetStringDefault(const char* key, const char* defaultValue);
    __declspec(dllexport) const char* Framework_Locale_Format(const char* key, const char* arg1);
    __declspec(dllexport) const char* Framework_Locale_Format2(const char* key, const char* arg1, const char* arg2);
    __declspec(dllexport) const char* Framework_Locale_Format3(const char* key, const char* arg1, const char* arg2, const char* arg3);
    __declspec(dllexport) bool  Framework_Locale_HasString(const char* key);

    // ---- String Table Management ----
    __declspec(dllexport) void  Framework_Locale_SetString(const char* key, const char* value);
    __declspec(dllexport) void  Framework_Locale_RemoveString(const char* key);
    __declspec(dllexport) int   Framework_Locale_GetStringCount();
    __declspec(dllexport) void  Framework_Locale_ClearStrings();

    // ---- File Operations ----
    __declspec(dllexport) bool  Framework_Locale_SaveLanguage(const char* languageCode, const char* filePath);
    __declspec(dllexport) bool  Framework_Locale_ReloadCurrent();

    // ---- Callbacks ----
    typedef void (*LocaleChangedCallback)(const char* newLanguage);
    __declspec(dllexport) void  Framework_Locale_SetOnLanguageChanged(LocaleChangedCallback callback);

    // ========================================================================
    // ACHIEVEMENT SYSTEM
    // ========================================================================
    // Unlock achievements, track progress, and display notifications

    // Achievement State
    #define ACHIEVEMENT_LOCKED      0
    #define ACHIEVEMENT_UNLOCKED    1
    #define ACHIEVEMENT_HIDDEN      2

    // ---- Achievement Definition ----
    __declspec(dllexport) int   Framework_Achievement_Create(const char* id, const char* name, const char* description);
    __declspec(dllexport) void  Framework_Achievement_SetIcon(int achievementId, int textureHandle);
    __declspec(dllexport) void  Framework_Achievement_SetHidden(int achievementId, bool hidden);
    __declspec(dllexport) void  Framework_Achievement_SetPoints(int achievementId, int points);

    // ---- Progress Achievements ----
    __declspec(dllexport) void  Framework_Achievement_SetProgressTarget(int achievementId, int target);
    __declspec(dllexport) void  Framework_Achievement_SetProgress(int achievementId, int progress);
    __declspec(dllexport) void  Framework_Achievement_AddProgress(int achievementId, int amount);
    __declspec(dllexport) int   Framework_Achievement_GetProgress(int achievementId);
    __declspec(dllexport) int   Framework_Achievement_GetProgressTarget(int achievementId);
    __declspec(dllexport) float Framework_Achievement_GetProgressPercent(int achievementId);

    // ---- Unlock/Lock ----
    __declspec(dllexport) void  Framework_Achievement_Unlock(int achievementId);
    __declspec(dllexport) void  Framework_Achievement_Lock(int achievementId);
    __declspec(dllexport) bool  Framework_Achievement_IsUnlocked(int achievementId);
    __declspec(dllexport) int   Framework_Achievement_GetState(int achievementId);

    // ---- Queries ----
    __declspec(dllexport) int   Framework_Achievement_GetByName(const char* id);
    __declspec(dllexport) const char* Framework_Achievement_GetName(int achievementId);
    __declspec(dllexport) const char* Framework_Achievement_GetDescription(int achievementId);
    __declspec(dllexport) int   Framework_Achievement_GetPoints(int achievementId);
    __declspec(dllexport) int   Framework_Achievement_GetCount();
    __declspec(dllexport) int   Framework_Achievement_GetUnlockedCount();
    __declspec(dllexport) int   Framework_Achievement_GetTotalPoints();
    __declspec(dllexport) int   Framework_Achievement_GetEarnedPoints();

    // ---- Notifications ----
    __declspec(dllexport) void  Framework_Achievement_SetNotificationsEnabled(bool enabled);
    __declspec(dllexport) void  Framework_Achievement_SetNotificationDuration(float seconds);
    __declspec(dllexport) void  Framework_Achievement_SetNotificationPosition(int x, int y);
    __declspec(dllexport) void  Framework_Achievement_Update(float deltaTime);
    __declspec(dllexport) void  Framework_Achievement_DrawNotifications();

    // ---- Persistence ----
    __declspec(dllexport) bool  Framework_Achievement_Save(const char* filePath);
    __declspec(dllexport) bool  Framework_Achievement_Load(const char* filePath);
    __declspec(dllexport) void  Framework_Achievement_ResetAll();

    // ---- Callbacks ----
    typedef void (*AchievementUnlockedCallback)(int achievementId, const char* name);
    __declspec(dllexport) void  Framework_Achievement_SetOnUnlocked(AchievementUnlockedCallback callback);

    // ========================================================================
    // CUTSCENE SYSTEM
    // ========================================================================
    // Scripted sequences with actor commands and camera control

    // Command Types
    #define CUTSCENE_CMD_WAIT           0
    #define CUTSCENE_CMD_DIALOGUE       1
    #define CUTSCENE_CMD_MOVE_ACTOR     2
    #define CUTSCENE_CMD_FADE_IN        3
    #define CUTSCENE_CMD_FADE_OUT       4
    #define CUTSCENE_CMD_PLAY_SOUND     5
    #define CUTSCENE_CMD_PLAY_MUSIC     6
    #define CUTSCENE_CMD_STOP_MUSIC     7
    #define CUTSCENE_CMD_CAMERA_PAN     8
    #define CUTSCENE_CMD_CAMERA_ZOOM    9
    #define CUTSCENE_CMD_SHAKE          10
    #define CUTSCENE_CMD_SET_VISIBLE    11
    #define CUTSCENE_CMD_ANIMATE        12
    #define CUTSCENE_CMD_CALLBACK       13

    // Cutscene State
    #define CUTSCENE_STATE_IDLE         0
    #define CUTSCENE_STATE_PLAYING      1
    #define CUTSCENE_STATE_PAUSED       2
    #define CUTSCENE_STATE_FINISHED     3

    // ---- Cutscene Management ----
    __declspec(dllexport) int   Framework_Cutscene_Create(const char* name);
    __declspec(dllexport) void  Framework_Cutscene_Destroy(int cutsceneId);
    __declspec(dllexport) int   Framework_Cutscene_GetByName(const char* name);

    // ---- Adding Commands ----
    __declspec(dllexport) void  Framework_Cutscene_AddWait(int cutsceneId, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddDialogue(int cutsceneId, const char* speaker, const char* text, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddMoveActor(int cutsceneId, int entityId, float targetX, float targetY, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddFadeIn(int cutsceneId, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddFadeOut(int cutsceneId, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddPlaySound(int cutsceneId, int soundHandle);
    __declspec(dllexport) void  Framework_Cutscene_AddPlayMusic(int cutsceneId, const char* musicPath);
    __declspec(dllexport) void  Framework_Cutscene_AddStopMusic(int cutsceneId, float fadeTime);
    __declspec(dllexport) void  Framework_Cutscene_AddCameraPan(int cutsceneId, float targetX, float targetY, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddCameraZoom(int cutsceneId, float targetZoom, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddShake(int cutsceneId, float intensity, float duration);
    __declspec(dllexport) void  Framework_Cutscene_AddSetVisible(int cutsceneId, int entityId, bool visible);
    __declspec(dllexport) void  Framework_Cutscene_AddAnimate(int cutsceneId, int entityId, const char* animationName);
    typedef void (*CutsceneCallback)();
    __declspec(dllexport) void  Framework_Cutscene_AddCallback(int cutsceneId, CutsceneCallback callback);

    // ---- Playback Control ----
    __declspec(dllexport) void  Framework_Cutscene_Play(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_Pause(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_Resume(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_Stop(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_Skip(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_SetSkippable(int cutsceneId, bool skippable);

    // ---- State Queries ----
    __declspec(dllexport) int   Framework_Cutscene_GetState(int cutsceneId);
    __declspec(dllexport) bool  Framework_Cutscene_IsPlaying(int cutsceneId);
    __declspec(dllexport) bool  Framework_Cutscene_IsPaused(int cutsceneId);
    __declspec(dllexport) bool  Framework_Cutscene_IsFinished(int cutsceneId);
    __declspec(dllexport) float Framework_Cutscene_GetProgress(int cutsceneId);
    __declspec(dllexport) int   Framework_Cutscene_GetCurrentCommand(int cutsceneId);
    __declspec(dllexport) int   Framework_Cutscene_GetCommandCount(int cutsceneId);

    // ---- Update & Render ----
    __declspec(dllexport) void  Framework_Cutscene_Update(float deltaTime);
    __declspec(dllexport) void  Framework_Cutscene_DrawDialogue();

    // ---- Dialogue Display Settings ----
    __declspec(dllexport) void  Framework_Cutscene_SetDialogueFont(int fontHandle);
    __declspec(dllexport) void  Framework_Cutscene_SetDialogueBox(int x, int y, int width, int height);
    __declspec(dllexport) void  Framework_Cutscene_SetDialogueColors(unsigned char bgR, unsigned char bgG, unsigned char bgB, unsigned char bgA,
                                                                      unsigned char textR, unsigned char textG, unsigned char textB);
    __declspec(dllexport) void  Framework_Cutscene_SetTypewriterSpeed(float charsPerSecond);

    // ---- Callbacks ----
    typedef void (*CutsceneFinishedCallback)(int cutsceneId);
    __declspec(dllexport) void  Framework_Cutscene_SetOnFinished(CutsceneFinishedCallback callback);

    // ========================================================================
    // LEADERBOARD SYSTEM
    // ========================================================================
    // Score tracking with sorting and persistence

    // Sort Order
    #define LEADERBOARD_SORT_DESC   0   // Higher is better
    #define LEADERBOARD_SORT_ASC    1   // Lower is better

    // ---- Leaderboard Management ----
    __declspec(dllexport) int   Framework_Leaderboard_Create(const char* name, int sortOrder, int maxEntries);
    __declspec(dllexport) void  Framework_Leaderboard_Destroy(int leaderboardId);
    __declspec(dllexport) int   Framework_Leaderboard_GetByName(const char* name);
    __declspec(dllexport) void  Framework_Leaderboard_Clear(int leaderboardId);

    // ---- Score Submission ----
    __declspec(dllexport) int   Framework_Leaderboard_SubmitScore(int leaderboardId, const char* playerName, int score);
    __declspec(dllexport) int   Framework_Leaderboard_SubmitScoreEx(int leaderboardId, const char* playerName, int score, const char* metadata);
    __declspec(dllexport) bool  Framework_Leaderboard_IsHighScore(int leaderboardId, int score);
    __declspec(dllexport) int   Framework_Leaderboard_GetRankForScore(int leaderboardId, int score);

    // ---- Entry Queries ----
    __declspec(dllexport) int   Framework_Leaderboard_GetEntryCount(int leaderboardId);
    __declspec(dllexport) const char* Framework_Leaderboard_GetEntryName(int leaderboardId, int rank);
    __declspec(dllexport) int   Framework_Leaderboard_GetEntryScore(int leaderboardId, int rank);
    __declspec(dllexport) const char* Framework_Leaderboard_GetEntryMetadata(int leaderboardId, int rank);
    __declspec(dllexport) const char* Framework_Leaderboard_GetEntryDate(int leaderboardId, int rank);

    // ---- Player Queries ----
    __declspec(dllexport) int   Framework_Leaderboard_GetPlayerRank(int leaderboardId, const char* playerName);
    __declspec(dllexport) int   Framework_Leaderboard_GetPlayerBestScore(int leaderboardId, const char* playerName);
    __declspec(dllexport) int   Framework_Leaderboard_GetPlayerEntryCount(int leaderboardId, const char* playerName);

    // ---- Top Scores ----
    __declspec(dllexport) int   Framework_Leaderboard_GetTopScore(int leaderboardId);
    __declspec(dllexport) const char* Framework_Leaderboard_GetTopPlayer(int leaderboardId);

    // ---- Persistence ----
    __declspec(dllexport) bool  Framework_Leaderboard_Save(int leaderboardId, const char* filePath);
    __declspec(dllexport) bool  Framework_Leaderboard_Load(int leaderboardId, const char* filePath);
    __declspec(dllexport) bool  Framework_Leaderboard_SaveAll(const char* filePath);
    __declspec(dllexport) bool  Framework_Leaderboard_LoadAll(const char* filePath);

    // ---- Global Management ----
    __declspec(dllexport) int   Framework_Leaderboard_GetCount();
    __declspec(dllexport) void  Framework_Leaderboard_DestroyAll();

    // ========================================================================
    // CLEANUP
    // ========================================================================
    __declspec(dllexport) void  Framework_ResourcesShutdown();

} // extern "C"
