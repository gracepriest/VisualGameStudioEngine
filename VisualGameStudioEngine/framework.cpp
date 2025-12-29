// framework.cpp
// Visual Game Studio Engine - Framework v1.0 / Engine v0.5
#include "pch.h"
#include "framework.h"

#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <algorithm>
#include <cctype>
#include <string>
#include <cstring>
#include <cmath>
#include <fstream>
#include <functional>

// ============================================================================
// GLOBAL ENGINE STATE
// ============================================================================
namespace {
    EngineState g_engineState = ENGINE_STOPPED;
    unsigned long long g_frameCount = 0;
    float g_timeScale = 1.0f;
    float g_masterVolume = 1.0f;
    bool g_audioPaused = false;

    // Fixed timestep
    double g_fixedStep = 1.0 / 60.0;
    double g_accum = 0.0;

    // Asset root path
    char g_assetRoot[FW_PATH_MAX] = "";

    // Draw callback
    DrawCallback userDrawCallback = nullptr;

    // Managed Camera2D
    Camera2D g_camera = { {0, 0}, {0, 0}, 0.0f, 1.0f };
    int g_cameraFollowEntity = -1;

    // Debug overlay state
    bool g_debugEnabled = false;
    bool g_debugDrawBounds = true;
    bool g_debugDrawHierarchy = false;
    bool g_debugDrawStats = true;
}

// ============================================================================
// SOUND CACHE
// ============================================================================
namespace {
    struct SoundEntry {
        Sound snd{};
        bool valid = false;
        bool paused = false;
    };
    std::unordered_map<int, SoundEntry> g_sounds;
    int g_nextSound = 1;
}

// ============================================================================
// TEXTURE CACHE
// ============================================================================
namespace {
    std::string NormalizePath(std::string p) {
        std::replace(p.begin(), p.end(), '\\', '/');
        std::transform(p.begin(), p.end(), p.begin(),
            [](unsigned char c) { return (unsigned char)std::tolower(c); });
        return p;
    }

    std::string ResolveAssetPath(const char* path) {
        if (!path) return "";
        std::string p(path);
        if (g_assetRoot[0] != '\0' && p.length() > 0 && p[0] != '/' && p[1] != ':') {
            p = std::string(g_assetRoot) + "/" + p;
        }
        return NormalizePath(p);
    }

    struct TexEntry {
        Texture2D   tex{};
        int         refCount = 0;
        std::string path;
        bool        valid = false;
    };

    std::unordered_map<int, TexEntry> g_texByHandle;
    std::unordered_map<std::string, int> g_handleByTexPath;
    int g_nextTexHandle = 1;

    int AcquireTextureH_Internal(const char* cpath) {
        std::string path = ResolveAssetPath(cpath);
        auto it = g_handleByTexPath.find(path);
        if (it != g_handleByTexPath.end()) {
            g_texByHandle[it->second].refCount++;
            return it->second;
        }

        Texture2D t = LoadTexture(path.c_str());
        int h = g_nextTexHandle++;

        TexEntry e;
        e.tex = t;
        e.refCount = 1;
        e.path = path;
        e.valid = (t.id != 0);
        g_texByHandle[h] = e;
        g_handleByTexPath[path] = h;
        return h;
    }

    void ReleaseTextureH_Internal(int h) {
        auto it = g_texByHandle.find(h);
        if (it == g_texByHandle.end()) return;
        if (--it->second.refCount <= 0) {
            if (it->second.valid) UnloadTexture(it->second.tex);
            g_handleByTexPath.erase(it->second.path);
            g_texByHandle.erase(it);
        }
    }

    const Texture2D* GetTextureH_Internal(int h) {
        auto it = g_texByHandle.find(h);
        if (it == g_texByHandle.end() || !it->second.valid) return nullptr;
        return &it->second.tex;
    }
}

// ============================================================================
// FONT CACHE
// ============================================================================
namespace {
    struct FontEntry {
        Font        font{};
        int         refCount = 0;
        std::string key;
        bool        valid = false;
    };

    std::unordered_map<int, FontEntry> g_fontByHandle;
    std::unordered_map<std::string, int> g_handleByFontKey;
    int g_nextFontHandle = 1;

    std::string MakeFontKey(const std::string& path, int size) {
        return NormalizePath(path) + "|" + std::to_string(size);
    }

    int AcquireFontH_Internal(const char* cpath, int size) {
        std::string key = MakeFontKey(cpath ? cpath : "", size);
        auto it = g_handleByFontKey.find(key);
        if (it != g_handleByFontKey.end()) {
            g_fontByHandle[it->second].refCount++;
            return it->second;
        }

        std::string path = ResolveAssetPath(cpath);
        Font f = LoadFontEx(path.c_str(), size, nullptr, 0);
        int h = g_nextFontHandle++;

        FontEntry e;
        e.font = f;
        e.refCount = 1;
        e.key = key;
        e.valid = (f.texture.id != 0);
        g_fontByHandle[h] = e;
        g_handleByFontKey[key] = h;
        return h;
    }

    void ReleaseFontH_Internal(int h) {
        auto it = g_fontByHandle.find(h);
        if (it == g_fontByHandle.end()) return;
        if (--it->second.refCount <= 0) {
            if (it->second.valid) UnloadFont(it->second.font);
            g_handleByFontKey.erase(it->second.key);
            g_fontByHandle.erase(it);
        }
    }

    const Font* GetFontH_Internal(int h) {
        auto it = g_fontByHandle.find(h);
        if (it == g_fontByHandle.end() || !it->second.valid) return nullptr;
        return &it->second.font;
    }
}

// ============================================================================
// MUSIC CACHE
// ============================================================================
namespace {
    struct MusicEntry {
        Music       mus{};
        int         refCount = 0;
        std::string path;
        bool        valid = false;
        bool        playing = false;
    };

    std::unordered_map<int, MusicEntry> g_musByHandle;
    std::unordered_map<std::string, int> g_handleByMusPath;
    int g_nextMusicHandle = 1;

    int AcquireMusicH_Internal(const char* cpath) {
        std::string path = ResolveAssetPath(cpath);
        auto it = g_handleByMusPath.find(path);
        if (it != g_handleByMusPath.end()) {
            g_musByHandle[it->second].refCount++;
            return it->second;
        }

        Music m = LoadMusicStream(path.c_str());
        int h = g_nextMusicHandle++;

        MusicEntry e;
        e.mus = m;
        e.refCount = 1;
        e.path = path;
        e.valid = (m.ctxData != nullptr);
        e.playing = false;
        g_musByHandle[h] = e;
        g_handleByMusPath[path] = h;
        return h;
    }

    void ReleaseMusicH_Internal(int h) {
        auto it = g_musByHandle.find(h);
        if (it == g_musByHandle.end()) return;
        if (--it->second.refCount <= 0) {
            if (it->second.valid) {
                StopMusicStream(it->second.mus);
                UnloadMusicStream(it->second.mus);
            }
            g_handleByMusPath.erase(it->second.path);
            g_musByHandle.erase(it);
        }
    }

    Music* GetMusicH_Internal(int h) {
        auto it = g_musByHandle.find(h);
        if (it == g_musByHandle.end() || !it->second.valid) return nullptr;
        return &it->second.mus;
    }
}

// ============================================================================
// ECS CORE
// ============================================================================
namespace {
    using Entity = int;

    // Component structures
    struct Transform2D {
        Vector2 position{ 0.0f, 0.0f };
        float   rotation = 0.0f;
        Vector2 scale{ 1.0f, 1.0f };
    };

    struct Sprite2D {
        int       textureHandle = 0;
        Rectangle source{ 0, 0, 0, 0 };
        Color     tint{ 255, 255, 255, 255 };
        int       layer = 0;
        bool      visible = true;
    };

    struct NameComponent {
        char name[FW_NAME_MAX] = {0};
    };

    struct TagComponent {
        char tag[FW_TAG_MAX] = {0};
    };

    struct HierarchyComponent {
        int parent = -1;
        int firstChild = -1;
        int nextSibling = -1;
        int prevSibling = -1;
    };

    struct Velocity2D {
        float vx = 0.0f;
        float vy = 0.0f;
    };

    struct BoxCollider2D {
        float offsetX = 0.0f;
        float offsetY = 0.0f;
        float width = 0.0f;
        float height = 0.0f;
        bool isTrigger = false;
    };

    struct EnabledComponent {
        bool enabled = true;
    };

    // ========================================================================
    // TILESET (shared resource)
    // ========================================================================
    struct Tileset {
        int textureHandle = 0;
        int tileWidth = 16;
        int tileHeight = 16;
        int columns = 1;
        bool valid = false;
    };

    std::unordered_map<int, Tileset> g_tilesets;
    int g_nextTilesetHandle = 1;

    // ========================================================================
    // TILEMAP COMPONENT
    // ========================================================================
    struct TilemapComponent {
        int tilesetHandle = 0;
        int mapWidth = 0;
        int mapHeight = 0;
        std::vector<int> tiles;  // 2D grid stored as 1D, -1 = empty
        std::unordered_set<int> solidTiles;  // Which tile indices are solid
    };

    // ========================================================================
    // ANIMATION CLIP (shared resource)
    // ========================================================================
    struct AnimFrame {
        Rectangle source{0, 0, 0, 0};
        float duration = 0.1f;  // seconds
    };

    struct AnimClip {
        std::string name;
        std::vector<AnimFrame> frames;
        int loopMode = ANIM_LOOP_REPEAT;
        bool valid = false;
    };

    std::unordered_map<int, AnimClip> g_animClips;
    int g_nextAnimClipHandle = 1;

    // ========================================================================
    // ANIMATOR COMPONENT
    // ========================================================================
    struct AnimatorComponent {
        int clipHandle = 0;
        int currentFrame = 0;
        float timer = 0.0f;
        float speed = 1.0f;
        bool playing = false;
        bool pingpongReverse = false;  // For pingpong mode
    };

    // ========================================================================
    // PARTICLE EMITTER COMPONENT
    // ========================================================================
    struct Particle {
        float x, y;
        float vx, vy;
        float life;       // Remaining life
        float maxLife;    // Initial life (for lerping)
        float size;
        bool active = false;
    };

    struct ParticleEmitterComponent {
        int textureHandle = 0;
        Rectangle sourceRect{0, 0, 0, 0};

        // Emission settings
        float emissionRate = 10.0f;  // particles per second
        float emissionAccum = 0.0f;  // accumulator for spawning
        int maxParticles = 100;

        // Particle properties
        float lifetimeMin = 1.0f;
        float lifetimeMax = 2.0f;
        float velocityMinX = -50.0f, velocityMinY = -100.0f;
        float velocityMaxX = 50.0f, velocityMaxY = -50.0f;
        Color colorStart{255, 255, 255, 255};
        Color colorEnd{255, 255, 255, 0};
        float sizeStart = 8.0f;
        float sizeEnd = 2.0f;
        float gravityX = 0.0f;
        float gravityY = 100.0f;
        float spreadAngle = 45.0f;  // Cone angle in degrees
        float directionX = 0.0f;
        float directionY = -1.0f;   // Default: upward

        // State
        bool active = false;
        std::vector<Particle> particles;
    };

    // Entity storage
    int g_nextEntityId = 1;
    std::unordered_set<Entity> g_entities;
    std::unordered_map<Entity, Transform2D> g_transform2D;
    std::unordered_map<Entity, Sprite2D> g_sprite2D;
    std::unordered_map<Entity, NameComponent> g_name;
    std::unordered_map<Entity, TagComponent> g_tag;
    std::unordered_map<Entity, HierarchyComponent> g_hierarchy;
    std::unordered_map<Entity, Velocity2D> g_velocity2D;
    std::unordered_map<Entity, BoxCollider2D> g_boxCollider2D;
    std::unordered_map<Entity, EnabledComponent> g_enabled;
    std::unordered_map<Entity, TilemapComponent> g_tilemap;
    std::unordered_map<Entity, AnimatorComponent> g_animator;
    std::unordered_map<Entity, ParticleEmitterComponent> g_particleEmitter;

    // Helpers
    bool EcsIsAlive(Entity e) {
        return g_entities.find(e) != g_entities.end();
    }

    void RemoveFromParent(Entity e) {
        auto hIt = g_hierarchy.find(e);
        if (hIt == g_hierarchy.end()) return;

        HierarchyComponent& h = hIt->second;
        if (h.parent == -1) return;

        auto pIt = g_hierarchy.find(h.parent);
        if (pIt != g_hierarchy.end()) {
            if (pIt->second.firstChild == e) {
                pIt->second.firstChild = h.nextSibling;
            }
        }

        if (h.prevSibling != -1) {
            auto prevIt = g_hierarchy.find(h.prevSibling);
            if (prevIt != g_hierarchy.end()) {
                prevIt->second.nextSibling = h.nextSibling;
            }
        }
        if (h.nextSibling != -1) {
            auto nextIt = g_hierarchy.find(h.nextSibling);
            if (nextIt != g_hierarchy.end()) {
                nextIt->second.prevSibling = h.prevSibling;
            }
        }

        h.parent = -1;
        h.prevSibling = -1;
        h.nextSibling = -1;
    }

    void DestroyEntityRecursive(Entity e) {
        auto hIt = g_hierarchy.find(e);
        if (hIt != g_hierarchy.end()) {
            int child = hIt->second.firstChild;
            while (child != -1) {
                int next = -1;
                auto chIt = g_hierarchy.find(child);
                if (chIt != g_hierarchy.end()) {
                    next = chIt->second.nextSibling;
                }
                DestroyEntityRecursive(child);
                child = next;
            }
        }

        RemoveFromParent(e);
        g_entities.erase(e);
        g_transform2D.erase(e);
        g_sprite2D.erase(e);
        g_name.erase(e);
        g_tag.erase(e);
        g_hierarchy.erase(e);
        g_velocity2D.erase(e);
        g_boxCollider2D.erase(e);
        g_enabled.erase(e);
    }

    void EcsClearAllInternal() {
        g_entities.clear();
        g_transform2D.clear();
        g_sprite2D.clear();
        g_name.clear();
        g_tag.clear();
        g_hierarchy.clear();
        g_velocity2D.clear();
        g_boxCollider2D.clear();
        g_enabled.clear();
    }

    Vector2 GetWorldPositionInternal(Entity e) {
        auto tIt = g_transform2D.find(e);
        if (tIt == g_transform2D.end()) return Vector2{ 0, 0 };

        Vector2 pos = tIt->second.position;

        auto hIt = g_hierarchy.find(e);
        if (hIt != g_hierarchy.end() && hIt->second.parent != -1) {
            Vector2 parentPos = GetWorldPositionInternal(hIt->second.parent);
            pos.x += parentPos.x;
            pos.y += parentPos.y;
        }

        return pos;
    }

    float GetWorldRotationInternal(Entity e) {
        auto tIt = g_transform2D.find(e);
        if (tIt == g_transform2D.end()) return 0.0f;

        float rot = tIt->second.rotation;

        auto hIt = g_hierarchy.find(e);
        if (hIt != g_hierarchy.end() && hIt->second.parent != -1) {
            rot += GetWorldRotationInternal(hIt->second.parent);
        }

        return rot;
    }

    Vector2 GetWorldScaleInternal(Entity e) {
        auto tIt = g_transform2D.find(e);
        if (tIt == g_transform2D.end()) return Vector2{ 1, 1 };

        Vector2 scale = tIt->second.scale;

        auto hIt = g_hierarchy.find(e);
        if (hIt != g_hierarchy.end() && hIt->second.parent != -1) {
            Vector2 parentScale = GetWorldScaleInternal(hIt->second.parent);
            scale.x *= parentScale.x;
            scale.y *= parentScale.y;
        }

        return scale;
    }

    bool IsActiveInHierarchyInternal(Entity e) {
        auto enIt = g_enabled.find(e);
        if (enIt != g_enabled.end() && !enIt->second.enabled) return false;

        auto hIt = g_hierarchy.find(e);
        if (hIt != g_hierarchy.end() && hIt->second.parent != -1) {
            return IsActiveInHierarchyInternal(hIt->second.parent);
        }

        return true;
    }

    Rectangle GetBoxColliderWorldBoundsInternal(Entity e) {
        Rectangle result = { 0, 0, 0, 0 };

        auto bcIt = g_boxCollider2D.find(e);
        if (bcIt == g_boxCollider2D.end()) return result;

        Vector2 worldPos = GetWorldPositionInternal(e);
        Vector2 worldScale = GetWorldScaleInternal(e);

        result.x = worldPos.x + bcIt->second.offsetX * worldScale.x;
        result.y = worldPos.y + bcIt->second.offsetY * worldScale.y;
        result.width = bcIt->second.width * worldScale.x;
        result.height = bcIt->second.height * worldScale.y;

        return result;
    }

    // Draw sprites (layer sorted, respects enabled state)
    struct DrawItem {
        int         layer;
        Sprite2D*   sprite;
        Entity      entity;
    };

    void EcsDrawSpritesInternal() {
        if (g_sprite2D.empty()) return;

        std::vector<DrawItem> items;
        items.reserve(g_sprite2D.size());

        for (auto& kv : g_sprite2D) {
            Entity e = kv.first;
            Sprite2D& sp = kv.second;
            if (!sp.visible) continue;
            if (!EcsIsAlive(e)) continue;
            if (!IsActiveInHierarchyInternal(e)) continue;

            auto tIt = g_transform2D.find(e);
            if (tIt == g_transform2D.end()) continue;

            items.push_back(DrawItem{ sp.layer, &sp, e });
        }

        std::sort(items.begin(), items.end(),
            [](const DrawItem& a, const DrawItem& b) {
                return a.layer < b.layer;
            });

        for (auto& it : items) {
            Sprite2D* sp = it.sprite;

            const Texture2D* tex = GetTextureH_Internal(sp->textureHandle);
            if (!tex) continue;

            Vector2 worldPos = GetWorldPositionInternal(it.entity);
            float worldRot = GetWorldRotationInternal(it.entity);
            Vector2 worldScale = GetWorldScaleInternal(it.entity);

            Rectangle dst;
            dst.x = worldPos.x;
            dst.y = worldPos.y;
            dst.width = sp->source.width * worldScale.x;
            dst.height = sp->source.height * worldScale.y;

            Vector2 origin{ dst.width * 0.5f, dst.height * 0.5f };

            DrawTexturePro(*tex, sp->source, dst, origin, worldRot, sp->tint);
        }
    }
}

// ============================================================================
// SCENE SYSTEM
// ============================================================================
namespace {
    struct ScriptScene {
        SceneCallbacks cb{};
    };

    std::unordered_map<int, ScriptScene> g_scenes;
    std::vector<int> g_sceneStack;
    int g_nextSceneHandle = 1;

    ScriptScene* GetScene(int h) {
        auto it = g_scenes.find(h);
        return (it == g_scenes.end()) ? nullptr : &it->second;
    }

    ScriptScene* TopScene() {
        if (g_sceneStack.empty()) return nullptr;
        return GetScene(g_sceneStack.back());
    }
}

// ============================================================================
// PREFAB STORAGE
// ============================================================================
namespace {
    struct PrefabData {
        std::vector<uint8_t> data;
        bool valid = false;
    };

    std::unordered_map<int, PrefabData> g_prefabs;
    int g_nextPrefabHandle = 1;
}

// Forward declarations
extern "C" void Framework_UpdateAllMusic();
extern "C" void Framework_ResourcesShutdown();

// ============================================================================
// ENGINE STATE & LIFECYCLE
// ============================================================================
extern "C" {

    bool Framework_Initialize(int width, int height, const char* title) {
        InitWindow(width, height, title);
        SetTargetFPS(60);
        g_engineState = ENGINE_RUNNING;
        g_frameCount = 0;
        g_timeScale = 1.0f;
        g_accum = 0.0;

        // Initialize camera
        g_camera.offset = Vector2{ (float)width / 2.0f, (float)height / 2.0f };
        g_camera.target = Vector2{ 0, 0 };
        g_camera.rotation = 0.0f;
        g_camera.zoom = 1.0f;

        return true;
    }

    void Framework_Update() {
        if (g_engineState == ENGINE_STOPPED) return;

        g_frameCount++;

        BeginDrawing();

        if (userDrawCallback != nullptr) {
            userDrawCallback();
        }

        EndDrawing();

        if (!g_audioPaused) {
            Framework_UpdateAllMusic();
        }

        // Only accumulate time if not paused
        if (g_engineState == ENGINE_RUNNING) {
            g_accum += (double)GetFrameTime() * g_timeScale;
        }
    }

    bool Framework_ShouldClose() {
        return WindowShouldClose() || g_engineState == ENGINE_QUITTING;
    }

    void Framework_Shutdown() {
        g_engineState = ENGINE_STOPPED;
        Framework_ResourcesShutdown();
        EcsClearAllInternal();
        CloseWindow();
    }

    int Framework_GetState() {
        return (int)g_engineState;
    }

    void Framework_Pause() {
        if (g_engineState == ENGINE_RUNNING) {
            g_engineState = ENGINE_PAUSED;
        }
    }

    void Framework_Resume() {
        if (g_engineState == ENGINE_PAUSED) {
            g_engineState = ENGINE_RUNNING;
        }
    }

    void Framework_Quit() {
        g_engineState = ENGINE_QUITTING;
    }

    bool Framework_IsPaused() {
        return g_engineState == ENGINE_PAUSED;
    }

    // ========================================================================
    // DRAW CONTROL
    // ========================================================================
    void Framework_SetDrawCallback(DrawCallback callback) {
        userDrawCallback = callback;
    }

    void Framework_BeginDrawing() { BeginDrawing(); }
    void Framework_EndDrawing() { EndDrawing(); }

    void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        ClearBackground(color);
    }

    void Framework_DrawText(const char* text, int x, int y, int fontSize,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawText(text, x, y, fontSize, color);
    }

    void Framework_DrawRectangle(int x, int y, int w, int h,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawRectangle(x, y, w, h, color);
    }

    // ========================================================================
    // TIMING
    // ========================================================================
    void   Framework_SetTargetFPS(int fps) { SetTargetFPS(fps); }
    float  Framework_GetFrameTime() { return GetFrameTime(); }
    float  Framework_GetDeltaTime() { return GetFrameTime() * g_timeScale; }
    double Framework_GetTime() { return GetTime(); }
    int    Framework_GetFPS() { return GetFPS(); }
    unsigned long long Framework_GetFrameCount() { return g_frameCount; }

    void  Framework_SetTimeScale(float scale) { g_timeScale = scale < 0.0f ? 0.0f : scale; }
    float Framework_GetTimeScale() { return g_timeScale; }

    void   Framework_SetFixedStep(double seconds) { g_fixedStep = seconds; }
    void   Framework_ResetFixedClock() { g_accum = 0.0; }

    bool Framework_StepFixed() {
        if (g_engineState != ENGINE_RUNNING) return false;
        if (g_accum >= g_fixedStep) {
            g_accum -= g_fixedStep;
            return true;
        }
        return false;
    }

    double Framework_GetFixedStep() { return g_fixedStep; }
    double Framework_GetAccumulator() { return g_accum; }

    // ========================================================================
    // INPUT - KEYBOARD
    // ========================================================================
    bool Framework_IsKeyPressed(int key) { return IsKeyPressed(key); }
    bool Framework_IsKeyPressedRepeat(int key) { return IsKeyPressedRepeat(key); }
    bool Framework_IsKeyDown(int key) { return IsKeyDown(key); }
    bool Framework_IsKeyReleased(int key) { return IsKeyReleased(key); }
    bool Framework_IsKeyUp(int key) { return IsKeyUp(key); }
    int  Framework_GetKeyPressed() { return GetKeyPressed(); }
    int  Framework_GetCharPressed() { return GetCharPressed(); }
    void Framework_SetExitKey(int key) { SetExitKey(key); }

    // ========================================================================
    // INPUT - MOUSE
    // ========================================================================
    int     Framework_GetMouseX() { return GetMouseX(); }
    int     Framework_GetMouseY() { return GetMouseY(); }
    bool    Framework_IsMouseButtonPressed(int b) { return IsMouseButtonPressed(b); }
    bool    Framework_IsMouseButtonDown(int b) { return IsMouseButtonDown(b); }
    bool    Framework_IsMouseButtonReleased(int b) { return IsMouseButtonReleased(b); }
    bool    Framework_IsMouseButtonUp(int b) { return IsMouseButtonUp(b); }
    Vector2 Framework_GetMousePosition() { return GetMousePosition(); }
    Vector2 Framework_GetMouseDelta() { return GetMouseDelta(); }
    void    Framework_SetMousePosition(int x, int y) { SetMousePosition(x, y); }
    void    Framework_SetMouseOffset(int ox, int oy) { SetMouseOffset(ox, oy); }
    void    Framework_SetMouseScale(float sx, float sy) { SetMouseScale(sx, sy); }
    float   Framework_GetMouseWheelMove() { return GetMouseWheelMove(); }
    Vector2 Framework_GetMouseWheelMoveV() { return GetMouseWheelMoveV(); }
    void    Framework_SetMouseCursor(int cursor) { SetMouseCursor(cursor); }

    void Framework_ShowCursor() { ShowCursor(); }
    void Framework_HideCursor() { HideCursor(); }
    bool Framework_IsCursorHidden() { return IsCursorHidden(); }
    void Framework_EnableCursor() { EnableCursor(); }
    void Framework_DisableCursor() { DisableCursor(); }
    bool Framework_IsCursorOnScreen() { return IsCursorOnScreen(); }

    // ========================================================================
    // SHAPES
    // ========================================================================
    void Framework_DrawPixel(int x, int y,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawPixel(x, y, Color{ r, g, b, a });
    }

    void Framework_DrawLine(int x0, int y0, int x1, int y1,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawLine(x0, y0, x1, y1, Color{ r, g, b, a });
    }

    void Framework_DrawCircle(int cx, int cy, float radius,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawCircle(cx, cy, radius, Color{ r, g, b, a });
    }

    void Framework_DrawCircleLines(int cx, int cy, float radius,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawCircleLines(cx, cy, radius, Color{ r, g, b, a });
    }

    void Framework_DrawRectangleLines(int x, int y, int w, int h,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawRectangleLines(x, y, w, h, Color{ r, g, b, a });
    }

    // ========================================================================
    // COLLISIONS
    // ========================================================================
    bool      Framework_CheckCollisionRecs(Rectangle a, Rectangle b) { return CheckCollisionRecs(a, b); }
    bool      Framework_CheckCollisionCircles(Vector2 c1, float r1, Vector2 c2, float r2) { return CheckCollisionCircles(c1, r1, c2, r2); }
    bool      Framework_CheckCollisionCircleRec(Vector2 c, float r, Rectangle rec) { return CheckCollisionCircleRec(c, r, rec); }
    bool      Framework_CheckCollisionCircleLine(Vector2 c, float r, Vector2 p1, Vector2 p2) { return CheckCollisionCircleLine(c, r, p1, p2); }
    bool      Framework_CheckCollisionPointRec(Vector2 p, Rectangle rec) { return CheckCollisionPointRec(p, rec); }
    bool      Framework_CheckCollisionPointCircle(Vector2 p, Vector2 c, float r) { return CheckCollisionPointCircle(p, c, r); }
    bool      Framework_CheckCollisionPointTriangle(Vector2 p, Vector2 p1, Vector2 p2, Vector2 p3) { return CheckCollisionPointTriangle(p, p1, p2, p3); }
    bool      Framework_CheckCollisionPointLine(Vector2 p, Vector2 p1, Vector2 p2, int thr) { return CheckCollisionPointLine(p, p1, p2, thr); }
    bool      Framework_CheckCollisionPointPoly(Vector2 p, const Vector2* pts, int n) { return CheckCollisionPointPoly(p, pts, n); }
    bool      Framework_CheckCollisionLines(Vector2 s1, Vector2 e1, Vector2 s2, Vector2 e2, Vector2* cp) {
        return CheckCollisionLines(s1, e1, s2, e2, cp);
    }
    Rectangle Framework_GetCollisionRec(Rectangle a, Rectangle b) { return GetCollisionRec(a, b); }

    // ========================================================================
    // TEXTURES / IMAGES
    // ========================================================================
    Texture2D Framework_LoadTexture(const char* fileName) {
        std::string path = ResolveAssetPath(fileName);
        return LoadTexture(path.c_str());
    }

    void Framework_UnloadTexture(Texture2D texture) { UnloadTexture(texture); }
    bool Framework_IsTextureValid(Texture2D texture) { return IsTextureValid(texture); }

    void Framework_UpdateTexture(Texture2D texture, const void* pixels) { UpdateTexture(texture, pixels); }
    void Framework_UpdateTextureRec(Texture2D texture, Rectangle rec, const void* pixels) { UpdateTextureRec(texture, rec, pixels); }
    void Framework_GenTextureMipmaps(Texture2D* texture) { GenTextureMipmaps(texture); }
    void Framework_SetTextureFilter(Texture2D texture, int filter) { SetTextureFilter(texture, filter); }
    void Framework_SetTextureWrap(Texture2D texture, int wrap) { SetTextureWrap(texture, wrap); }

    void Framework_DrawTexture(Texture2D texture, int posX, int posY,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTexture(texture, posX, posY, Color{ r, g, b, a });
    }

    void Framework_DrawTextureV(Texture2D texture, Vector2 position,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTextureV(texture, position, Color{ r, g, b, a });
    }

    void Framework_DrawTextureEx(Texture2D texture, Vector2 position, float rotation, float scale,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTextureEx(texture, position, rotation, scale, Color{ r, g, b, a });
    }

    void Framework_DrawTextureRec(Texture2D texture, Rectangle source, Vector2 position,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTextureRec(texture, source, position, Color{ r, g, b, a });
    }

    void Framework_DrawTexturePro(Texture2D texture, Rectangle source, Rectangle dest, Vector2 origin,
        float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTexturePro(texture, source, dest, origin, rotation, Color{ r, g, b, a });
    }

    void Framework_DrawTextureNPatch(Texture2D texture, NPatchInfo nPatchInfo, Rectangle dest, Vector2 origin,
        float rotation, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTextureNPatch(texture, nPatchInfo, dest, origin, rotation, Color{ r, g, b, a });
    }

    RenderTexture2D Framework_LoadRenderTexture(int width, int height) { return LoadRenderTexture(width, height); }
    void Framework_UnloadRenderTexture(RenderTexture2D target) { UnloadRenderTexture(target); }
    bool Framework_IsRenderTextureValid(RenderTexture2D target) { return IsRenderTextureValid(target); }
    void Framework_BeginTextureMode(RenderTexture2D rt) { BeginTextureMode(rt); }
    void Framework_EndTextureMode() { EndTextureMode(); }
    void Framework_BeginMode2D(Camera2D cam) { BeginMode2D(cam); }
    void Framework_EndMode2D() { EndMode2D(); }

    Image Framework_LoadImage(const char* fileName) {
        std::string path = ResolveAssetPath(fileName);
        return LoadImage(path.c_str());
    }
    void  Framework_UnloadImage(Image img) { UnloadImage(img); }
    void  Framework_ImageColorInvert(Image* img) { ImageColorInvert(img); }
    void  Framework_ImageResize(Image* img, int w, int h) { ImageResize(img, w, h); }
    void  Framework_ImageFlipVertical(Image* img) { ImageFlipVertical(img); }

    Font Framework_LoadFontEx(const char* fileName, int fontSize, int* glyphs, int glyphCount) {
        std::string path = ResolveAssetPath(fileName);
        return LoadFontEx(path.c_str(), fontSize, glyphs, glyphCount);
    }

    void Framework_UnloadFont(Font font) { UnloadFont(font); }

    void Framework_DrawTextEx(Font font, const char* text, Vector2 pos, float fontSize, float spacing,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        DrawTextEx(font, text, pos, fontSize, spacing, Color{ r, g, b, a });
    }

    Rectangle Framework_SpriteFrame(Rectangle sheetArea, int frameW, int frameH, int index, int columns) {
        Rectangle r{};
        r.x = sheetArea.x + (index % columns) * frameW;
        r.y = sheetArea.y + (index / columns) * frameH;
        r.width = (float)frameW;
        r.height = (float)frameH;
        return r;
    }

    void Framework_DrawFPS(int x, int y) { DrawFPS(x, y); }
    void Framework_DrawGrid(int slices, float spacing) { DrawGrid(slices, spacing); }

    // ========================================================================
    // CAMERA 2D (Managed)
    // ========================================================================
    void Framework_Camera_SetPosition(float x, float y) {
        g_camera.target = Vector2{ x, y };
    }

    void Framework_Camera_SetTarget(float x, float y) {
        g_camera.target = Vector2{ x, y };
    }

    void Framework_Camera_SetRotation(float rotation) {
        g_camera.rotation = rotation;
    }

    void Framework_Camera_SetZoom(float zoom) {
        g_camera.zoom = zoom < 0.01f ? 0.01f : zoom;
    }

    void Framework_Camera_SetOffset(float x, float y) {
        g_camera.offset = Vector2{ x, y };
    }

    Vector2 Framework_Camera_GetPosition() {
        return g_camera.target;
    }

    float Framework_Camera_GetZoom() {
        return g_camera.zoom;
    }

    float Framework_Camera_GetRotation() {
        return g_camera.rotation;
    }

    void Framework_Camera_FollowEntity(int entity) {
        g_cameraFollowEntity = entity;
    }

    void Framework_Camera_BeginMode() {
        // Update camera if following an entity
        if (g_cameraFollowEntity != -1 && EcsIsAlive(g_cameraFollowEntity)) {
            Vector2 pos = GetWorldPositionInternal(g_cameraFollowEntity);
            g_camera.target = pos;
        }
        BeginMode2D(g_camera);
    }

    void Framework_Camera_EndMode() {
        EndMode2D();
    }

    Vector2 Framework_Camera_ScreenToWorld(float screenX, float screenY) {
        return GetScreenToWorld2D(Vector2{ screenX, screenY }, g_camera);
    }

    Vector2 Framework_Camera_WorldToScreen(float worldX, float worldY) {
        return GetWorldToScreen2D(Vector2{ worldX, worldY }, g_camera);
    }

    // ========================================================================
    // AUDIO
    // ========================================================================
    bool Framework_InitAudio() {
        InitAudioDevice();
        return IsAudioDeviceReady();
    }

    void Framework_CloseAudio() {
        for (auto& kv : g_sounds) {
            if (kv.second.valid) UnloadSound(kv.second.snd);
        }
        g_sounds.clear();
        CloseAudioDevice();
    }

    void Framework_SetMasterVolume(float volume) {
        g_masterVolume = volume < 0.0f ? 0.0f : (volume > 1.0f ? 1.0f : volume);
        SetMasterVolume(g_masterVolume);
    }

    float Framework_GetMasterVolume() {
        return g_masterVolume;
    }

    void Framework_PauseAllAudio() {
        g_audioPaused = true;
        for (auto& kv : g_sounds) {
            if (kv.second.valid && IsSoundPlaying(kv.second.snd)) {
                PauseSound(kv.second.snd);
                kv.second.paused = true;
            }
        }
        for (auto& kv : g_musByHandle) {
            if (kv.second.playing) {
                PauseMusicStream(kv.second.mus);
            }
        }
    }

    void Framework_ResumeAllAudio() {
        g_audioPaused = false;
        for (auto& kv : g_sounds) {
            if (kv.second.valid && kv.second.paused) {
                ResumeSound(kv.second.snd);
                kv.second.paused = false;
            }
        }
        for (auto& kv : g_musByHandle) {
            if (kv.second.playing) {
                ResumeMusicStream(kv.second.mus);
            }
        }
    }

    int Framework_LoadSoundH(const char* file) {
        std::string path = ResolveAssetPath(file);
        Sound s = LoadSound(path.c_str());
        int id = g_nextSound++;
        SoundEntry entry;
        entry.snd = s;
        entry.valid = IsSoundValid(s);
        entry.paused = false;
        g_sounds[id] = entry;
        return id;
    }

    void Framework_UnloadSoundH(int h) {
        auto it = g_sounds.find(h);
        if (it != g_sounds.end()) {
            if (it->second.valid) UnloadSound(it->second.snd);
            g_sounds.erase(it);
        }
    }

    void Framework_PlaySoundH(int h) {
        auto it = g_sounds.find(h);
        if (it != g_sounds.end() && it->second.valid && !g_audioPaused) {
            PlaySound(it->second.snd);
        }
    }
    void Framework_StopSoundH(int h) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) StopSound(it->second.snd); }
    void Framework_PauseSoundH(int h) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) PauseSound(it->second.snd); }
    void Framework_ResumeSoundH(int h) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) ResumeSound(it->second.snd); }
    void Framework_SetSoundVolumeH(int h, float v) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) SetSoundVolume(it->second.snd, v); }
    void Framework_SetSoundPitchH(int h, float p) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) SetSoundPitch(it->second.snd, p); }
    void Framework_SetSoundPanH(int h, float pan) { auto it = g_sounds.find(h); if (it != g_sounds.end() && it->second.valid) SetSoundPan(it->second.snd, pan); }

    // Music
    int  Framework_AcquireMusicH(const char* path) { return AcquireMusicH_Internal(path); }
    void Framework_ReleaseMusicH(int handle) { ReleaseMusicH_Internal(handle); }
    bool Framework_IsMusicValidH(int handle) { return GetMusicH_Internal(handle) != nullptr; }

    void Framework_PlayMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m && !g_audioPaused) {
            PlayMusicStream(*m);
            g_musByHandle[handle].playing = true;
        }
    }

    void Framework_StopMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) {
            StopMusicStream(*m);
            g_musByHandle[handle].playing = false;
        }
    }

    void Framework_PauseMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) {
            PauseMusicStream(*m);
        }
    }

    void Framework_ResumeMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m && !g_audioPaused) {
            ResumeMusicStream(*m);
        }
    }

    void Framework_SetMusicVolumeH(int handle, float v) {
        Music* m = GetMusicH_Internal(handle);
        if (m) SetMusicVolume(*m, v);
    }

    void Framework_SetMusicPitchH(int handle, float p) {
        Music* m = GetMusicH_Internal(handle);
        if (m) SetMusicPitch(*m, p);
    }

    void Framework_UpdateMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) UpdateMusicStream(*m);
    }

    void Framework_UpdateAllMusic() {
        if (g_audioPaused) return;
        for (auto& kv : g_musByHandle) {
            if (kv.second.playing) {
                UpdateMusicStream(kv.second.mus);
            }
        }
    }

    // ========================================================================
    // SHADERS
    // ========================================================================
    Shader Framework_LoadShaderF(const char* vsPath, const char* fsPath) {
        std::string vs = vsPath ? ResolveAssetPath(vsPath) : "";
        std::string fs = fsPath ? ResolveAssetPath(fsPath) : "";
        return LoadShader(vs.empty() ? nullptr : vs.c_str(), fs.empty() ? nullptr : fs.c_str());
    }

    void   Framework_UnloadShader(Shader sh) { UnloadShader(sh); }
    void   Framework_BeginShaderMode(Shader sh) { BeginShaderMode(sh); }
    void   Framework_EndShaderMode() { EndShaderMode(); }
    int    Framework_GetShaderLocation(Shader sh, const char* name) { return GetShaderLocation(sh, name); }

    void Framework_SetShaderValue1f(Shader sh, int loc, float v) {
        SetShaderValue(sh, loc, &v, SHADER_UNIFORM_FLOAT);
    }
    void Framework_SetShaderValue2f(Shader sh, int loc, float x, float y) {
        float a[2]{ x, y };
        SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC2);
    }
    void Framework_SetShaderValue3f(Shader sh, int loc, float x, float y, float z) {
        float a[3]{ x, y, z };
        SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC3);
    }
    void Framework_SetShaderValue4f(Shader sh, int loc, float x, float y, float z, float w) {
        float a[4]{ x, y, z, w };
        SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC4);
    }
    void Framework_SetShaderValue1i(Shader sh, int loc, int v) {
        SetShaderValue(sh, loc, &v, SHADER_UNIFORM_INT);
    }

    // ========================================================================
    // ASSET CACHE
    // ========================================================================
    void Framework_SetAssetRoot(const char* path) {
        if (path) {
            strncpy_s(g_assetRoot, FW_PATH_MAX, path, _TRUNCATE);
        } else {
            g_assetRoot[0] = '\0';
        }
    }

    const char* Framework_GetAssetRoot() {
        return g_assetRoot;
    }

    int  Framework_AcquireTextureH(const char* path) { return AcquireTextureH_Internal(path); }
    void Framework_ReleaseTextureH(int handle) { ReleaseTextureH_Internal(handle); }
    bool Framework_IsTextureValidH(int handle) { return GetTextureH_Internal(handle) != nullptr; }

    void Framework_DrawTextureH(int handle, int x, int y,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) DrawTexture(*tex, x, y, Color{ r, g, b, a });
    }

    void Framework_DrawTextureVH(int handle, Vector2 pos,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) DrawTextureV(*tex, pos, Color{ r, g, b, a });
    }

    void Framework_DrawTextureExH(int handle, Vector2 pos, float rotation, float scale,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) DrawTextureEx(*tex, pos, rotation, scale, Color{ r, g, b, a });
    }

    void Framework_DrawTextureRecH(int handle, Rectangle src, Vector2 pos,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) DrawTextureRec(*tex, src, pos, Color{ r, g, b, a });
    }

    void Framework_DrawTextureProH(int handle, Rectangle src, Rectangle dst, Vector2 origin, float rotation,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) DrawTexturePro(*tex, src, dst, origin, rotation, Color{ r, g, b, a });
    }

    int Framework_GetTextureWidth(int handle) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        return tex ? tex->width : 0;
    }

    int Framework_GetTextureHeight(int handle) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        return tex ? tex->height : 0;
    }

    int  Framework_AcquireFontH(const char* path, int fontSize) { return AcquireFontH_Internal(path, fontSize); }
    void Framework_ReleaseFontH(int handle) { ReleaseFontH_Internal(handle); }
    bool Framework_IsFontValidH(int handle) { return GetFontH_Internal(handle) != nullptr; }

    void Framework_DrawTextExH(int handle, const char* text, Vector2 pos, float fontSize, float spacing,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Font* f = GetFontH_Internal(handle);
        if (f) DrawTextEx(*f, text, pos, fontSize, spacing, Color{ r, g, b, a });
    }

    // ========================================================================
    // SCENE SYSTEM
    // ========================================================================
    int Framework_CreateScriptScene(SceneCallbacks cb) {
        int h = g_nextSceneHandle++;
        g_scenes[h] = ScriptScene{ cb };
        return h;
    }

    void Framework_DestroyScene(int sceneHandle) {
        for (int i = (int)g_sceneStack.size() - 1; i >= 0; --i) {
            if (g_sceneStack[i] == sceneHandle) {
                if (i == (int)g_sceneStack.size() - 1) {
                    if (auto sc = GetScene(sceneHandle); sc && sc->cb.onExit) {
                        sc->cb.onExit();
                    }
                }
                g_sceneStack.erase(g_sceneStack.begin() + i);
            }
        }
        g_scenes.erase(sceneHandle);
    }

    void Framework_SceneChange(int sceneHandle) {
        if (!g_sceneStack.empty()) {
            if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
            g_sceneStack.pop_back();
        }
        g_sceneStack.push_back(sceneHandle);
        if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
    }

    void Framework_ScenePush(int sceneHandle) {
        g_sceneStack.push_back(sceneHandle);
        if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
    }

    void Framework_ScenePop() {
        if (g_sceneStack.empty()) return;
        if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
        g_sceneStack.pop_back();
        if (auto sc = TopScene(); sc && sc->cb.onResume) sc->cb.onResume();
    }

    bool Framework_SceneHas() {
        return !g_sceneStack.empty();
    }

    int Framework_SceneGetCurrent() {
        return g_sceneStack.empty() ? -1 : g_sceneStack.back();
    }

    void Framework_SceneTick() {
        if (g_engineState == ENGINE_RUNNING) {
            while (Framework_StepFixed()) {
                auto sc = TopScene();
                if (!sc) return;
                if (sc->cb.onUpdateFixed) {
                    sc->cb.onUpdateFixed(Framework_GetFixedStep());
                }
            }
        }

        // Frame update runs even when paused (for UI)
        if (auto sc = TopScene(); sc && sc->cb.onUpdateFrame) {
            float dt = (g_engineState == ENGINE_RUNNING) ? Framework_GetDeltaTime() : 0.0f;
            sc->cb.onUpdateFrame(dt);
        }

        if (auto sc = TopScene(); sc && sc->cb.onDraw) {
            sc->cb.onDraw();
        }
    }

    // ========================================================================
    // ECS - ENTITIES
    // ========================================================================
    int Framework_Ecs_CreateEntity() {
        Entity e = g_nextEntityId++;
        g_entities.insert(e);
        g_enabled[e] = EnabledComponent{ true };
        return e;
    }

    void Framework_Ecs_DestroyEntity(int entity) {
        if (!EcsIsAlive(entity)) return;
        DestroyEntityRecursive(entity);
    }

    bool Framework_Ecs_IsAlive(int entity) {
        return EcsIsAlive(entity);
    }

    void Framework_Ecs_ClearAll() {
        EcsClearAllInternal();
    }

    int Framework_Ecs_GetEntityCount() {
        return (int)g_entities.size();
    }

    int Framework_Ecs_GetAllEntities(int* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) return 0;
        int count = 0;
        for (Entity e : g_entities) {
            if (count >= bufferSize) break;
            buffer[count++] = e;
        }
        return count;
    }

    // ========================================================================
    // ECS - NAME COMPONENT
    // ========================================================================
    void Framework_Ecs_SetName(int entity, const char* name) {
        if (!EcsIsAlive(entity)) return;
        NameComponent nc;
        memset(nc.name, 0, FW_NAME_MAX);
        if (name) {
            strncpy_s(nc.name, FW_NAME_MAX, name, _TRUNCATE);
        }
        g_name[entity] = nc;
    }

    const char* Framework_Ecs_GetName(int entity) {
        auto it = g_name.find(entity);
        if (it == g_name.end()) return "";
        return it->second.name;
    }

    bool Framework_Ecs_HasName(int entity) {
        return g_name.find(entity) != g_name.end();
    }

    int Framework_Ecs_FindByName(const char* name) {
        if (!name) return -1;
        for (auto& kv : g_name) {
            if (strcmp(kv.second.name, name) == 0) {
                return kv.first;
            }
        }
        return -1;
    }

    // ========================================================================
    // ECS - TAG COMPONENT
    // ========================================================================
    void Framework_Ecs_SetTag(int entity, const char* tag) {
        if (!EcsIsAlive(entity)) return;
        TagComponent tc;
        memset(tc.tag, 0, FW_TAG_MAX);
        if (tag) {
            strncpy_s(tc.tag, FW_TAG_MAX, tag, _TRUNCATE);
        }
        g_tag[entity] = tc;
    }

    const char* Framework_Ecs_GetTag(int entity) {
        auto it = g_tag.find(entity);
        if (it == g_tag.end()) return "";
        return it->second.tag;
    }

    bool Framework_Ecs_HasTag(int entity) {
        return g_tag.find(entity) != g_tag.end();
    }

    int Framework_Ecs_FindAllByTag(const char* tag, int* buffer, int bufferSize) {
        if (!tag || !buffer || bufferSize <= 0) return 0;
        int count = 0;
        for (auto& kv : g_tag) {
            if (count >= bufferSize) break;
            if (strcmp(kv.second.tag, tag) == 0) {
                buffer[count++] = kv.first;
            }
        }
        return count;
    }

    // ========================================================================
    // ECS - ENABLED COMPONENT
    // ========================================================================
    void Framework_Ecs_SetEnabled(int entity, bool enabled) {
        if (!EcsIsAlive(entity)) return;
        g_enabled[entity].enabled = enabled;
    }

    bool Framework_Ecs_IsEnabled(int entity) {
        auto it = g_enabled.find(entity);
        if (it == g_enabled.end()) return true;
        return it->second.enabled;
    }

    bool Framework_Ecs_IsActiveInHierarchy(int entity) {
        if (!EcsIsAlive(entity)) return false;
        return IsActiveInHierarchyInternal(entity);
    }

    // ========================================================================
    // ECS - HIERARCHY COMPONENT
    // ========================================================================
    void Framework_Ecs_SetParent(int entity, int parent) {
        if (!EcsIsAlive(entity)) return;
        if (parent != -1 && !EcsIsAlive(parent)) return;
        if (entity == parent) return;

        RemoveFromParent(entity);

        if (g_hierarchy.find(entity) == g_hierarchy.end()) {
            g_hierarchy[entity] = HierarchyComponent{};
        }

        if (parent == -1) return;

        if (g_hierarchy.find(parent) == g_hierarchy.end()) {
            g_hierarchy[parent] = HierarchyComponent{};
        }

        HierarchyComponent& h = g_hierarchy[entity];
        HierarchyComponent& ph = g_hierarchy[parent];

        h.parent = parent;
        h.nextSibling = ph.firstChild;
        h.prevSibling = -1;

        if (ph.firstChild != -1) {
            auto fcIt = g_hierarchy.find(ph.firstChild);
            if (fcIt != g_hierarchy.end()) {
                fcIt->second.prevSibling = entity;
            }
        }

        ph.firstChild = entity;
    }

    int Framework_Ecs_GetParent(int entity) {
        auto it = g_hierarchy.find(entity);
        if (it == g_hierarchy.end()) return -1;
        return it->second.parent;
    }

    int Framework_Ecs_GetFirstChild(int entity) {
        auto it = g_hierarchy.find(entity);
        if (it == g_hierarchy.end()) return -1;
        return it->second.firstChild;
    }

    int Framework_Ecs_GetNextSibling(int entity) {
        auto it = g_hierarchy.find(entity);
        if (it == g_hierarchy.end()) return -1;
        return it->second.nextSibling;
    }

    int Framework_Ecs_GetChildCount(int entity) {
        auto it = g_hierarchy.find(entity);
        if (it == g_hierarchy.end()) return 0;

        int count = 0;
        int child = it->second.firstChild;
        while (child != -1) {
            count++;
            auto cIt = g_hierarchy.find(child);
            if (cIt == g_hierarchy.end()) break;
            child = cIt->second.nextSibling;
        }
        return count;
    }

    int Framework_Ecs_GetChildren(int entity, int* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) return 0;

        auto it = g_hierarchy.find(entity);
        if (it == g_hierarchy.end()) return 0;

        int count = 0;
        int child = it->second.firstChild;
        while (child != -1 && count < bufferSize) {
            buffer[count++] = child;
            auto cIt = g_hierarchy.find(child);
            if (cIt == g_hierarchy.end()) break;
            child = cIt->second.nextSibling;
        }
        return count;
    }

    void Framework_Ecs_DetachFromParent(int entity) {
        RemoveFromParent(entity);
    }

    // ========================================================================
    // ECS - TRANSFORM2D COMPONENT
    // ========================================================================
    void Framework_Ecs_AddTransform2D(int entity, float x, float y, float rotation, float sx, float sy) {
        if (!EcsIsAlive(entity)) return;
        Transform2D t;
        t.position = Vector2{ x, y };
        t.rotation = rotation;
        t.scale = Vector2{ sx, sy };
        g_transform2D[entity] = t;
    }

    bool Framework_Ecs_HasTransform2D(int entity) {
        return g_transform2D.find(entity) != g_transform2D.end();
    }

    void Framework_Ecs_SetTransformPosition(int entity, float x, float y) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return;
        it->second.position = Vector2{ x, y };
    }

    void Framework_Ecs_SetTransformRotation(int entity, float rotation) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return;
        it->second.rotation = rotation;
    }

    void Framework_Ecs_SetTransformScale(int entity, float sx, float sy) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return;
        it->second.scale = Vector2{ sx, sy };
    }

    Vector2 Framework_Ecs_GetTransformPosition(int entity) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return Vector2{ 0.0f, 0.0f };
        return it->second.position;
    }

    Vector2 Framework_Ecs_GetTransformScale(int entity) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return Vector2{ 1.0f, 1.0f };
        return it->second.scale;
    }

    float Framework_Ecs_GetTransformRotation(int entity) {
        auto it = g_transform2D.find(entity);
        if (it == g_transform2D.end()) return 0.0f;
        return it->second.rotation;
    }

    Vector2 Framework_Ecs_GetWorldPosition(int entity) {
        if (!EcsIsAlive(entity)) return Vector2{ 0, 0 };
        return GetWorldPositionInternal(entity);
    }

    float Framework_Ecs_GetWorldRotation(int entity) {
        if (!EcsIsAlive(entity)) return 0.0f;
        return GetWorldRotationInternal(entity);
    }

    Vector2 Framework_Ecs_GetWorldScale(int entity) {
        if (!EcsIsAlive(entity)) return Vector2{ 1, 1 };
        return GetWorldScaleInternal(entity);
    }

    // ========================================================================
    // ECS - VELOCITY2D COMPONENT
    // ========================================================================
    void Framework_Ecs_AddVelocity2D(int entity, float vx, float vy) {
        if (!EcsIsAlive(entity)) return;
        g_velocity2D[entity] = Velocity2D{ vx, vy };
    }

    bool Framework_Ecs_HasVelocity2D(int entity) {
        return g_velocity2D.find(entity) != g_velocity2D.end();
    }

    void Framework_Ecs_SetVelocity(int entity, float vx, float vy) {
        auto it = g_velocity2D.find(entity);
        if (it == g_velocity2D.end()) return;
        it->second.vx = vx;
        it->second.vy = vy;
    }

    Vector2 Framework_Ecs_GetVelocity(int entity) {
        auto it = g_velocity2D.find(entity);
        if (it == g_velocity2D.end()) return Vector2{ 0, 0 };
        return Vector2{ it->second.vx, it->second.vy };
    }

    void Framework_Ecs_RemoveVelocity2D(int entity) {
        g_velocity2D.erase(entity);
    }

    // ========================================================================
    // ECS - BOXCOLLIDER2D COMPONENT
    // ========================================================================
    void Framework_Ecs_AddBoxCollider2D(int entity, float offsetX, float offsetY, float width, float height, bool isTrigger) {
        if (!EcsIsAlive(entity)) return;
        g_boxCollider2D[entity] = BoxCollider2D{ offsetX, offsetY, width, height, isTrigger };
    }

    bool Framework_Ecs_HasBoxCollider2D(int entity) {
        return g_boxCollider2D.find(entity) != g_boxCollider2D.end();
    }

    void Framework_Ecs_SetBoxCollider(int entity, float offsetX, float offsetY, float width, float height) {
        auto it = g_boxCollider2D.find(entity);
        if (it == g_boxCollider2D.end()) return;
        it->second.offsetX = offsetX;
        it->second.offsetY = offsetY;
        it->second.width = width;
        it->second.height = height;
    }

    void Framework_Ecs_SetBoxColliderTrigger(int entity, bool isTrigger) {
        auto it = g_boxCollider2D.find(entity);
        if (it == g_boxCollider2D.end()) return;
        it->second.isTrigger = isTrigger;
    }

    Rectangle Framework_Ecs_GetBoxColliderWorldBounds(int entity) {
        return GetBoxColliderWorldBoundsInternal(entity);
    }

    void Framework_Ecs_RemoveBoxCollider2D(int entity) {
        g_boxCollider2D.erase(entity);
    }

    // ========================================================================
    // ECS - SPRITE2D COMPONENT
    // ========================================================================
    void Framework_Ecs_AddSprite2D(int entity, int textureHandle,
        float srcX, float srcY, float srcW, float srcH,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a,
        int layer) {
        if (!EcsIsAlive(entity)) return;
        Sprite2D sp;
        sp.textureHandle = textureHandle;
        sp.source = Rectangle{ srcX, srcY, srcW, srcH };
        sp.tint = Color{ r, g, b, a };
        sp.layer = layer;
        sp.visible = true;
        g_sprite2D[entity] = sp;
    }

    bool Framework_Ecs_HasSprite2D(int entity) {
        return g_sprite2D.find(entity) != g_sprite2D.end();
    }

    void Framework_Ecs_SetSpriteTint(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return;
        it->second.tint = Color{ r, g, b, a };
    }

    void Framework_Ecs_SetSpriteVisible(int entity, bool visible) {
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return;
        it->second.visible = visible;
    }

    void Framework_Ecs_SetSpriteLayer(int entity, int layer) {
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return;
        it->second.layer = layer;
    }

    void Framework_Ecs_SetSpriteSource(int entity, float srcX, float srcY, float srcW, float srcH) {
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return;
        it->second.source = Rectangle{ srcX, srcY, srcW, srcH };
    }

    void Framework_Ecs_SetSpriteTexture(int entity, int textureHandle) {
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return;
        it->second.textureHandle = textureHandle;
    }

    void Framework_Ecs_RemoveSprite2D(int entity) {
        g_sprite2D.erase(entity);
    }

    // ========================================================================
    // ECS - SYSTEMS
    // ========================================================================
    void Framework_Ecs_UpdateVelocities(float dt) {
        for (auto& kv : g_velocity2D) {
            Entity e = kv.first;
            if (!IsActiveInHierarchyInternal(e)) continue;

            auto tIt = g_transform2D.find(e);
            if (tIt == g_transform2D.end()) continue;

            tIt->second.position.x += kv.second.vx * dt;
            tIt->second.position.y += kv.second.vy * dt;
        }
    }

    void Framework_Ecs_DrawSprites() {
        EcsDrawSpritesInternal();
    }

    // ========================================================================
    // PHYSICS - OVERLAP QUERIES
    // ========================================================================
    int Framework_Physics_OverlapBox(float x, float y, float w, float h, int* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) return 0;

        Rectangle query = { x, y, w, h };
        int count = 0;

        for (auto& kv : g_boxCollider2D) {
            if (count >= bufferSize) break;
            Rectangle bounds = GetBoxColliderWorldBoundsInternal(kv.first);
            if (CheckCollisionRecs(query, bounds)) {
                buffer[count++] = kv.first;
            }
        }

        return count;
    }

    int Framework_Physics_OverlapCircle(float x, float y, float radius, int* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) return 0;

        Vector2 center = { x, y };
        int count = 0;

        for (auto& kv : g_boxCollider2D) {
            if (count >= bufferSize) break;
            Rectangle bounds = GetBoxColliderWorldBoundsInternal(kv.first);
            if (CheckCollisionCircleRec(center, radius, bounds)) {
                buffer[count++] = kv.first;
            }
        }

        return count;
    }

    bool Framework_Physics_CheckEntityOverlap(int entityA, int entityB) {
        if (!EcsIsAlive(entityA) || !EcsIsAlive(entityB)) return false;

        auto aIt = g_boxCollider2D.find(entityA);
        auto bIt = g_boxCollider2D.find(entityB);
        if (aIt == g_boxCollider2D.end() || bIt == g_boxCollider2D.end()) return false;

        Rectangle boundsA = GetBoxColliderWorldBoundsInternal(entityA);
        Rectangle boundsB = GetBoxColliderWorldBoundsInternal(entityB);

        return CheckCollisionRecs(boundsA, boundsB);
    }

    int Framework_Physics_GetOverlappingEntities(int entity, int* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) return 0;
        if (!EcsIsAlive(entity)) return 0;

        auto eIt = g_boxCollider2D.find(entity);
        if (eIt == g_boxCollider2D.end()) return 0;

        Rectangle bounds = GetBoxColliderWorldBoundsInternal(entity);
        int count = 0;

        for (auto& kv : g_boxCollider2D) {
            if (count >= bufferSize) break;
            if (kv.first == entity) continue;

            Rectangle otherBounds = GetBoxColliderWorldBoundsInternal(kv.first);
            if (CheckCollisionRecs(bounds, otherBounds)) {
                buffer[count++] = kv.first;
            }
        }

        return count;
    }

    // ========================================================================
    // INTROSPECTION
    // ========================================================================
    int Framework_Entity_GetComponentCount(int entity) {
        if (!EcsIsAlive(entity)) return 0;
        int count = 0;
        if (g_transform2D.find(entity) != g_transform2D.end()) count++;
        if (g_sprite2D.find(entity) != g_sprite2D.end()) count++;
        if (g_name.find(entity) != g_name.end()) count++;
        if (g_tag.find(entity) != g_tag.end()) count++;
        if (g_hierarchy.find(entity) != g_hierarchy.end()) count++;
        if (g_velocity2D.find(entity) != g_velocity2D.end()) count++;
        if (g_boxCollider2D.find(entity) != g_boxCollider2D.end()) count++;
        if (g_enabled.find(entity) != g_enabled.end()) count++;
        return count;
    }

    int Framework_Entity_GetComponentTypeAt(int entity, int index) {
        if (!EcsIsAlive(entity)) return COMP_NONE;
        int current = 0;
        if (g_transform2D.find(entity) != g_transform2D.end()) { if (current == index) return COMP_TRANSFORM2D; current++; }
        if (g_sprite2D.find(entity) != g_sprite2D.end()) { if (current == index) return COMP_SPRITE2D; current++; }
        if (g_name.find(entity) != g_name.end()) { if (current == index) return COMP_NAME; current++; }
        if (g_tag.find(entity) != g_tag.end()) { if (current == index) return COMP_TAG; current++; }
        if (g_hierarchy.find(entity) != g_hierarchy.end()) { if (current == index) return COMP_HIERARCHY; current++; }
        if (g_velocity2D.find(entity) != g_velocity2D.end()) { if (current == index) return COMP_VELOCITY2D; current++; }
        if (g_boxCollider2D.find(entity) != g_boxCollider2D.end()) { if (current == index) return COMP_BOXCOLLIDER2D; current++; }
        if (g_enabled.find(entity) != g_enabled.end()) { if (current == index) return COMP_ENABLED; current++; }
        return COMP_NONE;
    }

    bool Framework_Entity_HasComponent(int entity, int compType) {
        if (!EcsIsAlive(entity)) return false;
        switch (compType) {
            case COMP_TRANSFORM2D: return g_transform2D.find(entity) != g_transform2D.end();
            case COMP_SPRITE2D: return g_sprite2D.find(entity) != g_sprite2D.end();
            case COMP_NAME: return g_name.find(entity) != g_name.end();
            case COMP_TAG: return g_tag.find(entity) != g_tag.end();
            case COMP_HIERARCHY: return g_hierarchy.find(entity) != g_hierarchy.end();
            case COMP_VELOCITY2D: return g_velocity2D.find(entity) != g_velocity2D.end();
            case COMP_BOXCOLLIDER2D: return g_boxCollider2D.find(entity) != g_boxCollider2D.end();
            case COMP_ENABLED: return g_enabled.find(entity) != g_enabled.end();
            default: return false;
        }
    }

    // Field metadata
    static const char* s_transform2DFields[] = { "posX", "posY", "rotation", "scaleX", "scaleY" };
    static const char* s_sprite2DFields[] = { "textureHandle", "srcX", "srcY", "srcW", "srcH", "tintR", "tintG", "tintB", "tintA", "layer", "visible" };
    static const char* s_nameFields[] = { "name" };
    static const char* s_tagFields[] = { "tag" };
    static const char* s_hierarchyFields[] = { "parent", "firstChild", "nextSibling" };
    static const char* s_velocity2DFields[] = { "vx", "vy" };
    static const char* s_boxCollider2DFields[] = { "offsetX", "offsetY", "width", "height", "isTrigger" };
    static const char* s_enabledFields[] = { "enabled" };

    int Framework_Component_GetFieldCount(int compType) {
        switch (compType) {
            case COMP_TRANSFORM2D: return 5;
            case COMP_SPRITE2D: return 11;
            case COMP_NAME: return 1;
            case COMP_TAG: return 1;
            case COMP_HIERARCHY: return 3;
            case COMP_VELOCITY2D: return 2;
            case COMP_BOXCOLLIDER2D: return 5;
            case COMP_ENABLED: return 1;
            default: return 0;
        }
    }

    const char* Framework_Component_GetFieldName(int compType, int fieldIndex) {
        switch (compType) {
            case COMP_TRANSFORM2D: return (fieldIndex >= 0 && fieldIndex < 5) ? s_transform2DFields[fieldIndex] : "";
            case COMP_SPRITE2D: return (fieldIndex >= 0 && fieldIndex < 11) ? s_sprite2DFields[fieldIndex] : "";
            case COMP_NAME: return (fieldIndex == 0) ? s_nameFields[0] : "";
            case COMP_TAG: return (fieldIndex == 0) ? s_tagFields[0] : "";
            case COMP_HIERARCHY: return (fieldIndex >= 0 && fieldIndex < 3) ? s_hierarchyFields[fieldIndex] : "";
            case COMP_VELOCITY2D: return (fieldIndex >= 0 && fieldIndex < 2) ? s_velocity2DFields[fieldIndex] : "";
            case COMP_BOXCOLLIDER2D: return (fieldIndex >= 0 && fieldIndex < 5) ? s_boxCollider2DFields[fieldIndex] : "";
            case COMP_ENABLED: return (fieldIndex == 0) ? s_enabledFields[0] : "";
            default: return "";
        }
    }

    int Framework_Component_GetFieldType(int compType, int fieldIndex) {
        // 0=float, 1=int, 2=bool, 3=string
        switch (compType) {
            case COMP_TRANSFORM2D: return 0; // all floats
            case COMP_SPRITE2D:
                if (fieldIndex == 0 || fieldIndex == 9) return 1; // textureHandle, layer = int
                if (fieldIndex == 10) return 2; // visible = bool
                return 0; // rest are floats
            case COMP_NAME: return 3;
            case COMP_TAG: return 3;
            case COMP_HIERARCHY: return 1; // all ints
            case COMP_VELOCITY2D: return 0;
            case COMP_BOXCOLLIDER2D:
                if (fieldIndex == 4) return 2; // isTrigger = bool
                return 0;
            case COMP_ENABLED: return 2;
            default: return 0;
        }
    }

    float Framework_Component_GetFieldFloat(int entity, int compType, int fieldIndex) {
        switch (compType) {
            case COMP_TRANSFORM2D: {
                auto it = g_transform2D.find(entity);
                if (it == g_transform2D.end()) return 0.0f;
                switch (fieldIndex) {
                    case 0: return it->second.position.x;
                    case 1: return it->second.position.y;
                    case 2: return it->second.rotation;
                    case 3: return it->second.scale.x;
                    case 4: return it->second.scale.y;
                }
                break;
            }
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return 0.0f;
                switch (fieldIndex) {
                    case 1: return it->second.source.x;
                    case 2: return it->second.source.y;
                    case 3: return it->second.source.width;
                    case 4: return it->second.source.height;
                    case 5: return (float)it->second.tint.r;
                    case 6: return (float)it->second.tint.g;
                    case 7: return (float)it->second.tint.b;
                    case 8: return (float)it->second.tint.a;
                }
                break;
            }
            case COMP_VELOCITY2D: {
                auto it = g_velocity2D.find(entity);
                if (it == g_velocity2D.end()) return 0.0f;
                switch (fieldIndex) {
                    case 0: return it->second.vx;
                    case 1: return it->second.vy;
                }
                break;
            }
            case COMP_BOXCOLLIDER2D: {
                auto it = g_boxCollider2D.find(entity);
                if (it == g_boxCollider2D.end()) return 0.0f;
                switch (fieldIndex) {
                    case 0: return it->second.offsetX;
                    case 1: return it->second.offsetY;
                    case 2: return it->second.width;
                    case 3: return it->second.height;
                }
                break;
            }
        }
        return 0.0f;
    }

    int Framework_Component_GetFieldInt(int entity, int compType, int fieldIndex) {
        switch (compType) {
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return 0;
                switch (fieldIndex) {
                    case 0: return it->second.textureHandle;
                    case 9: return it->second.layer;
                }
                break;
            }
            case COMP_HIERARCHY: {
                auto it = g_hierarchy.find(entity);
                if (it == g_hierarchy.end()) return -1;
                switch (fieldIndex) {
                    case 0: return it->second.parent;
                    case 1: return it->second.firstChild;
                    case 2: return it->second.nextSibling;
                }
                break;
            }
        }
        return 0;
    }

    bool Framework_Component_GetFieldBool(int entity, int compType, int fieldIndex) {
        switch (compType) {
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return false;
                if (fieldIndex == 10) return it->second.visible;
                break;
            }
            case COMP_BOXCOLLIDER2D: {
                auto it = g_boxCollider2D.find(entity);
                if (it == g_boxCollider2D.end()) return false;
                if (fieldIndex == 4) return it->second.isTrigger;
                break;
            }
            case COMP_ENABLED: {
                auto it = g_enabled.find(entity);
                if (it == g_enabled.end()) return true;
                if (fieldIndex == 0) return it->second.enabled;
                break;
            }
        }
        return false;
    }

    const char* Framework_Component_GetFieldString(int entity, int compType, int fieldIndex) {
        switch (compType) {
            case COMP_NAME: {
                auto it = g_name.find(entity);
                if (it == g_name.end()) return "";
                return it->second.name;
            }
            case COMP_TAG: {
                auto it = g_tag.find(entity);
                if (it == g_tag.end()) return "";
                return it->second.tag;
            }
        }
        return "";
    }

    void Framework_Component_SetFieldFloat(int entity, int compType, int fieldIndex, float value) {
        switch (compType) {
            case COMP_TRANSFORM2D: {
                auto it = g_transform2D.find(entity);
                if (it == g_transform2D.end()) return;
                switch (fieldIndex) {
                    case 0: it->second.position.x = value; break;
                    case 1: it->second.position.y = value; break;
                    case 2: it->second.rotation = value; break;
                    case 3: it->second.scale.x = value; break;
                    case 4: it->second.scale.y = value; break;
                }
                break;
            }
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return;
                switch (fieldIndex) {
                    case 1: it->second.source.x = value; break;
                    case 2: it->second.source.y = value; break;
                    case 3: it->second.source.width = value; break;
                    case 4: it->second.source.height = value; break;
                    case 5: it->second.tint.r = (unsigned char)value; break;
                    case 6: it->second.tint.g = (unsigned char)value; break;
                    case 7: it->second.tint.b = (unsigned char)value; break;
                    case 8: it->second.tint.a = (unsigned char)value; break;
                }
                break;
            }
            case COMP_VELOCITY2D: {
                auto it = g_velocity2D.find(entity);
                if (it == g_velocity2D.end()) return;
                switch (fieldIndex) {
                    case 0: it->second.vx = value; break;
                    case 1: it->second.vy = value; break;
                }
                break;
            }
            case COMP_BOXCOLLIDER2D: {
                auto it = g_boxCollider2D.find(entity);
                if (it == g_boxCollider2D.end()) return;
                switch (fieldIndex) {
                    case 0: it->second.offsetX = value; break;
                    case 1: it->second.offsetY = value; break;
                    case 2: it->second.width = value; break;
                    case 3: it->second.height = value; break;
                }
                break;
            }
        }
    }

    void Framework_Component_SetFieldInt(int entity, int compType, int fieldIndex, int value) {
        switch (compType) {
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return;
                switch (fieldIndex) {
                    case 0: it->second.textureHandle = value; break;
                    case 9: it->second.layer = value; break;
                }
                break;
            }
        }
    }

    void Framework_Component_SetFieldBool(int entity, int compType, int fieldIndex, bool value) {
        switch (compType) {
            case COMP_SPRITE2D: {
                auto it = g_sprite2D.find(entity);
                if (it == g_sprite2D.end()) return;
                if (fieldIndex == 10) it->second.visible = value;
                break;
            }
            case COMP_BOXCOLLIDER2D: {
                auto it = g_boxCollider2D.find(entity);
                if (it == g_boxCollider2D.end()) return;
                if (fieldIndex == 4) it->second.isTrigger = value;
                break;
            }
            case COMP_ENABLED: {
                auto it = g_enabled.find(entity);
                if (it == g_enabled.end()) return;
                if (fieldIndex == 0) it->second.enabled = value;
                break;
            }
        }
    }

    void Framework_Component_SetFieldString(int entity, int compType, int fieldIndex, const char* value) {
        switch (compType) {
            case COMP_NAME: {
                auto it = g_name.find(entity);
                if (it == g_name.end()) return;
                if (value) {
                    strncpy_s(it->second.name, FW_NAME_MAX, value, _TRUNCATE);
                }
                break;
            }
            case COMP_TAG: {
                auto it = g_tag.find(entity);
                if (it == g_tag.end()) return;
                if (value) {
                    strncpy_s(it->second.tag, FW_TAG_MAX, value, _TRUNCATE);
                }
                break;
            }
        }
    }

    // ========================================================================
    // DEBUG OVERLAY
    // ========================================================================
    void Framework_Debug_SetEnabled(bool enabled) {
        g_debugEnabled = enabled;
    }

    bool Framework_Debug_IsEnabled() {
        return g_debugEnabled;
    }

    void Framework_Debug_DrawEntityBounds(bool enabled) {
        g_debugDrawBounds = enabled;
    }

    void Framework_Debug_DrawHierarchy(bool enabled) {
        g_debugDrawHierarchy = enabled;
    }

    void Framework_Debug_DrawStats(bool enabled) {
        g_debugDrawStats = enabled;
    }

    void Framework_Debug_Render() {
        if (!g_debugEnabled) return;

        // Draw entity bounds
        if (g_debugDrawBounds) {
            for (auto& kv : g_boxCollider2D) {
                if (!IsActiveInHierarchyInternal(kv.first)) continue;
                Rectangle bounds = GetBoxColliderWorldBoundsInternal(kv.first);
                Color col = kv.second.isTrigger ? Color{ 0, 255, 0, 128 } : Color{ 255, 255, 0, 128 };
                DrawRectangleLinesEx(bounds, 1.0f, col);
            }
        }

        // Draw hierarchy lines
        if (g_debugDrawHierarchy) {
            for (auto& kv : g_hierarchy) {
                if (kv.second.parent == -1) continue;
                Vector2 childPos = GetWorldPositionInternal(kv.first);
                Vector2 parentPos = GetWorldPositionInternal(kv.second.parent);
                DrawLineV(childPos, parentPos, Color{ 128, 128, 255, 200 });
            }
        }

        // Draw stats
        if (g_debugDrawStats) {
            int y = 10;
            char buf[128];

            snprintf(buf, sizeof(buf), "FPS: %d", GetFPS());
            DrawText(buf, 10, y, 16, WHITE); y += 18;

            snprintf(buf, sizeof(buf), "Entities: %d", (int)g_entities.size());
            DrawText(buf, 10, y, 16, WHITE); y += 18;

            snprintf(buf, sizeof(buf), "Sprites: %d", (int)g_sprite2D.size());
            DrawText(buf, 10, y, 16, WHITE); y += 18;

            snprintf(buf, sizeof(buf), "Frame: %llu", g_frameCount);
            DrawText(buf, 10, y, 16, WHITE); y += 18;

            const char* stateStr = "UNKNOWN";
            switch (g_engineState) {
                case ENGINE_STOPPED: stateStr = "STOPPED"; break;
                case ENGINE_RUNNING: stateStr = "RUNNING"; break;
                case ENGINE_PAUSED: stateStr = "PAUSED"; break;
                case ENGINE_QUITTING: stateStr = "QUITTING"; break;
            }
            snprintf(buf, sizeof(buf), "State: %s", stateStr);
            DrawText(buf, 10, y, 16, WHITE);
        }
    }

    // ========================================================================
    // PREFABS & SERIALIZATION (Basic implementation)
    // ========================================================================

    // Scene/Prefab binary format magic
    #define VGSE_MAGIC 0x45534756  // 'VGSE'
    #define VGSE_VERSION 1

    bool Framework_Scene_Save(const char* path) {
        if (!path) return false;

        std::ofstream file(path, std::ios::binary);
        if (!file) return false;

        // Header
        uint32_t magic = VGSE_MAGIC;
        uint16_t version = VGSE_VERSION;
        uint32_t entityCount = (uint32_t)g_entities.size();

        file.write((char*)&magic, sizeof(magic));
        file.write((char*)&version, sizeof(version));
        file.write((char*)&entityCount, sizeof(entityCount));

        // For each entity, write components
        for (Entity e : g_entities) {
            file.write((char*)&e, sizeof(e));

            // Flags for which components exist
            uint16_t compFlags = 0;
            if (g_transform2D.find(e) != g_transform2D.end()) compFlags |= (1 << COMP_TRANSFORM2D);
            if (g_sprite2D.find(e) != g_sprite2D.end()) compFlags |= (1 << COMP_SPRITE2D);
            if (g_name.find(e) != g_name.end()) compFlags |= (1 << COMP_NAME);
            if (g_tag.find(e) != g_tag.end()) compFlags |= (1 << COMP_TAG);
            if (g_hierarchy.find(e) != g_hierarchy.end()) compFlags |= (1 << COMP_HIERARCHY);
            if (g_velocity2D.find(e) != g_velocity2D.end()) compFlags |= (1 << COMP_VELOCITY2D);
            if (g_boxCollider2D.find(e) != g_boxCollider2D.end()) compFlags |= (1 << COMP_BOXCOLLIDER2D);
            if (g_enabled.find(e) != g_enabled.end()) compFlags |= (1 << COMP_ENABLED);

            file.write((char*)&compFlags, sizeof(compFlags));

            if (compFlags & (1 << COMP_TRANSFORM2D)) {
                file.write((char*)&g_transform2D[e], sizeof(Transform2D));
            }
            if (compFlags & (1 << COMP_SPRITE2D)) {
                file.write((char*)&g_sprite2D[e], sizeof(Sprite2D));
            }
            if (compFlags & (1 << COMP_NAME)) {
                file.write((char*)&g_name[e], sizeof(NameComponent));
            }
            if (compFlags & (1 << COMP_TAG)) {
                file.write((char*)&g_tag[e], sizeof(TagComponent));
            }
            if (compFlags & (1 << COMP_HIERARCHY)) {
                file.write((char*)&g_hierarchy[e], sizeof(HierarchyComponent));
            }
            if (compFlags & (1 << COMP_VELOCITY2D)) {
                file.write((char*)&g_velocity2D[e], sizeof(Velocity2D));
            }
            if (compFlags & (1 << COMP_BOXCOLLIDER2D)) {
                file.write((char*)&g_boxCollider2D[e], sizeof(BoxCollider2D));
            }
            if (compFlags & (1 << COMP_ENABLED)) {
                file.write((char*)&g_enabled[e], sizeof(EnabledComponent));
            }
        }

        return true;
    }

    bool Framework_Scene_Load(const char* path) {
        if (!path) return false;

        std::ifstream file(path, std::ios::binary);
        if (!file) return false;

        uint32_t magic;
        uint16_t version;
        uint32_t entityCount;

        file.read((char*)&magic, sizeof(magic));
        if (magic != VGSE_MAGIC) return false;

        file.read((char*)&version, sizeof(version));
        if (version != VGSE_VERSION) return false;

        file.read((char*)&entityCount, sizeof(entityCount));

        // Clear current scene
        EcsClearAllInternal();

        for (uint32_t i = 0; i < entityCount; i++) {
            Entity e;
            file.read((char*)&e, sizeof(e));

            g_entities.insert(e);
            if (e >= g_nextEntityId) g_nextEntityId = e + 1;

            uint16_t compFlags;
            file.read((char*)&compFlags, sizeof(compFlags));

            if (compFlags & (1 << COMP_TRANSFORM2D)) {
                Transform2D t;
                file.read((char*)&t, sizeof(t));
                g_transform2D[e] = t;
            }
            if (compFlags & (1 << COMP_SPRITE2D)) {
                Sprite2D s;
                file.read((char*)&s, sizeof(s));
                g_sprite2D[e] = s;
            }
            if (compFlags & (1 << COMP_NAME)) {
                NameComponent n;
                file.read((char*)&n, sizeof(n));
                g_name[e] = n;
            }
            if (compFlags & (1 << COMP_TAG)) {
                TagComponent t;
                file.read((char*)&t, sizeof(t));
                g_tag[e] = t;
            }
            if (compFlags & (1 << COMP_HIERARCHY)) {
                HierarchyComponent h;
                file.read((char*)&h, sizeof(h));
                g_hierarchy[e] = h;
            }
            if (compFlags & (1 << COMP_VELOCITY2D)) {
                Velocity2D v;
                file.read((char*)&v, sizeof(v));
                g_velocity2D[e] = v;
            }
            if (compFlags & (1 << COMP_BOXCOLLIDER2D)) {
                BoxCollider2D b;
                file.read((char*)&b, sizeof(b));
                g_boxCollider2D[e] = b;
            }
            if (compFlags & (1 << COMP_ENABLED)) {
                EnabledComponent en;
                file.read((char*)&en, sizeof(en));
                g_enabled[e] = en;
            }
        }

        return true;
    }

    int Framework_Prefab_Load(const char* path) {
        if (!path) return 0;

        std::ifstream file(path, std::ios::binary | std::ios::ate);
        if (!file) return 0;

        std::streamsize size = file.tellg();
        file.seekg(0, std::ios::beg);

        PrefabData pd;
        pd.data.resize((size_t)size);
        if (!file.read((char*)pd.data.data(), size)) return 0;

        pd.valid = true;
        int h = g_nextPrefabHandle++;
        g_prefabs[h] = std::move(pd);
        return h;
    }

    int Framework_Prefab_Instantiate(int prefabH, int parentEntity, float x, float y) {
        auto it = g_prefabs.find(prefabH);
        if (it == g_prefabs.end() || !it->second.valid) return -1;

        // Parse prefab data and create entities
        // This is a simplified version - a full impl would need to remap entity IDs
        const uint8_t* data = it->second.data.data();
        size_t offset = 0;

        uint32_t magic = *(uint32_t*)(data + offset); offset += 4;
        if (magic != VGSE_MAGIC) return -1;

        uint16_t version = *(uint16_t*)(data + offset); offset += 2;
        if (version != VGSE_VERSION) return -1;

        uint32_t entityCount = *(uint32_t*)(data + offset); offset += 4;

        std::unordered_map<Entity, Entity> idRemap;
        Entity rootEntity = -1;

        // First pass: create entities and remap IDs
        for (uint32_t i = 0; i < entityCount; i++) {
            Entity oldId = *(Entity*)(data + offset); offset += sizeof(Entity);
            Entity newId = g_nextEntityId++;
            g_entities.insert(newId);
            idRemap[oldId] = newId;

            if (rootEntity == -1) rootEntity = newId;

            uint16_t compFlags = *(uint16_t*)(data + offset); offset += 2;

            if (compFlags & (1 << COMP_TRANSFORM2D)) {
                Transform2D t = *(Transform2D*)(data + offset);
                offset += sizeof(Transform2D);
                // Offset position for root entity
                if (newId == rootEntity) {
                    t.position.x += x;
                    t.position.y += y;
                }
                g_transform2D[newId] = t;
            }
            if (compFlags & (1 << COMP_SPRITE2D)) {
                Sprite2D s = *(Sprite2D*)(data + offset);
                offset += sizeof(Sprite2D);
                g_sprite2D[newId] = s;
            }
            if (compFlags & (1 << COMP_NAME)) {
                NameComponent n = *(NameComponent*)(data + offset);
                offset += sizeof(NameComponent);
                g_name[newId] = n;
            }
            if (compFlags & (1 << COMP_TAG)) {
                TagComponent t = *(TagComponent*)(data + offset);
                offset += sizeof(TagComponent);
                g_tag[newId] = t;
            }
            if (compFlags & (1 << COMP_HIERARCHY)) {
                HierarchyComponent h = *(HierarchyComponent*)(data + offset);
                offset += sizeof(HierarchyComponent);
                // Will fix up in second pass
                g_hierarchy[newId] = h;
            }
            if (compFlags & (1 << COMP_VELOCITY2D)) {
                Velocity2D v = *(Velocity2D*)(data + offset);
                offset += sizeof(Velocity2D);
                g_velocity2D[newId] = v;
            }
            if (compFlags & (1 << COMP_BOXCOLLIDER2D)) {
                BoxCollider2D b = *(BoxCollider2D*)(data + offset);
                offset += sizeof(BoxCollider2D);
                g_boxCollider2D[newId] = b;
            }
            if (compFlags & (1 << COMP_ENABLED)) {
                EnabledComponent en = *(EnabledComponent*)(data + offset);
                offset += sizeof(EnabledComponent);
                g_enabled[newId] = en;
            }
        }

        // Second pass: fix hierarchy references
        for (auto& kv : idRemap) {
            Entity newId = kv.second;
            auto hIt = g_hierarchy.find(newId);
            if (hIt != g_hierarchy.end()) {
                HierarchyComponent& h = hIt->second;
                if (h.parent != -1) {
                    auto pIt = idRemap.find(h.parent);
                    h.parent = (pIt != idRemap.end()) ? pIt->second : -1;
                }
                if (h.firstChild != -1) {
                    auto cIt = idRemap.find(h.firstChild);
                    h.firstChild = (cIt != idRemap.end()) ? cIt->second : -1;
                }
                if (h.nextSibling != -1) {
                    auto sIt = idRemap.find(h.nextSibling);
                    h.nextSibling = (sIt != idRemap.end()) ? sIt->second : -1;
                }
                if (h.prevSibling != -1) {
                    auto sIt = idRemap.find(h.prevSibling);
                    h.prevSibling = (sIt != idRemap.end()) ? sIt->second : -1;
                }
            }
        }

        // Set parent if specified
        if (parentEntity != -1 && EcsIsAlive(parentEntity) && rootEntity != -1) {
            Framework_Ecs_SetParent(rootEntity, parentEntity);
        }

        return rootEntity;
    }

    void Framework_Prefab_Unload(int prefabH) {
        g_prefabs.erase(prefabH);
    }

    bool Framework_Prefab_SaveEntity(int entity, const char* path) {
        if (!path || !EcsIsAlive(entity)) return false;

        // Collect entity and all descendants
        std::vector<Entity> entities;
        std::function<void(Entity)> collect = [&](Entity e) {
            entities.push_back(e);
            auto hIt = g_hierarchy.find(e);
            if (hIt != g_hierarchy.end()) {
                int child = hIt->second.firstChild;
                while (child != -1) {
                    collect(child);
                    auto chIt = g_hierarchy.find(child);
                    if (chIt == g_hierarchy.end()) break;
                    child = chIt->second.nextSibling;
                }
            }
        };
        collect(entity);

        std::ofstream file(path, std::ios::binary);
        if (!file) return false;

        uint32_t magic = VGSE_MAGIC;
        uint16_t version = VGSE_VERSION;
        uint32_t entityCount = (uint32_t)entities.size();

        file.write((char*)&magic, sizeof(magic));
        file.write((char*)&version, sizeof(version));
        file.write((char*)&entityCount, sizeof(entityCount));

        for (Entity e : entities) {
            file.write((char*)&e, sizeof(e));

            uint16_t compFlags = 0;
            if (g_transform2D.find(e) != g_transform2D.end()) compFlags |= (1 << COMP_TRANSFORM2D);
            if (g_sprite2D.find(e) != g_sprite2D.end()) compFlags |= (1 << COMP_SPRITE2D);
            if (g_name.find(e) != g_name.end()) compFlags |= (1 << COMP_NAME);
            if (g_tag.find(e) != g_tag.end()) compFlags |= (1 << COMP_TAG);
            if (g_hierarchy.find(e) != g_hierarchy.end()) compFlags |= (1 << COMP_HIERARCHY);
            if (g_velocity2D.find(e) != g_velocity2D.end()) compFlags |= (1 << COMP_VELOCITY2D);
            if (g_boxCollider2D.find(e) != g_boxCollider2D.end()) compFlags |= (1 << COMP_BOXCOLLIDER2D);
            if (g_enabled.find(e) != g_enabled.end()) compFlags |= (1 << COMP_ENABLED);

            file.write((char*)&compFlags, sizeof(compFlags));

            if (compFlags & (1 << COMP_TRANSFORM2D)) {
                file.write((char*)&g_transform2D[e], sizeof(Transform2D));
            }
            if (compFlags & (1 << COMP_SPRITE2D)) {
                file.write((char*)&g_sprite2D[e], sizeof(Sprite2D));
            }
            if (compFlags & (1 << COMP_NAME)) {
                file.write((char*)&g_name[e], sizeof(NameComponent));
            }
            if (compFlags & (1 << COMP_TAG)) {
                file.write((char*)&g_tag[e], sizeof(TagComponent));
            }
            if (compFlags & (1 << COMP_HIERARCHY)) {
                file.write((char*)&g_hierarchy[e], sizeof(HierarchyComponent));
            }
            if (compFlags & (1 << COMP_VELOCITY2D)) {
                file.write((char*)&g_velocity2D[e], sizeof(Velocity2D));
            }
            if (compFlags & (1 << COMP_BOXCOLLIDER2D)) {
                file.write((char*)&g_boxCollider2D[e], sizeof(BoxCollider2D));
            }
            if (compFlags & (1 << COMP_ENABLED)) {
                file.write((char*)&g_enabled[e], sizeof(EnabledComponent));
            }
        }

        return true;
    }

    // ========================================================================
    // TILEMAP SYSTEM
    // ========================================================================
    int Framework_Tileset_Create(int textureHandle, int tileWidth, int tileHeight, int columns) {
        Tileset ts;
        ts.textureHandle = textureHandle;
        ts.tileWidth = tileWidth > 0 ? tileWidth : 16;
        ts.tileHeight = tileHeight > 0 ? tileHeight : 16;
        ts.columns = columns > 0 ? columns : 1;
        ts.valid = true;
        int h = g_nextTilesetHandle++;
        g_tilesets[h] = ts;
        return h;
    }

    void Framework_Tileset_Destroy(int tilesetHandle) {
        g_tilesets.erase(tilesetHandle);
    }

    bool Framework_Tileset_IsValid(int tilesetHandle) {
        auto it = g_tilesets.find(tilesetHandle);
        return it != g_tilesets.end() && it->second.valid;
    }

    int Framework_Tileset_GetTileWidth(int tilesetHandle) {
        auto it = g_tilesets.find(tilesetHandle);
        return (it != g_tilesets.end()) ? it->second.tileWidth : 0;
    }

    int Framework_Tileset_GetTileHeight(int tilesetHandle) {
        auto it = g_tilesets.find(tilesetHandle);
        return (it != g_tilesets.end()) ? it->second.tileHeight : 0;
    }

    void Framework_Ecs_AddTilemap(int entity, int tilesetHandle, int mapWidth, int mapHeight) {
        if (!EcsIsAlive(entity)) return;
        TilemapComponent tm;
        tm.tilesetHandle = tilesetHandle;
        tm.mapWidth = mapWidth > 0 ? mapWidth : 1;
        tm.mapHeight = mapHeight > 0 ? mapHeight : 1;
        tm.tiles.resize(tm.mapWidth * tm.mapHeight, -1);
        g_tilemap[entity] = tm;
    }

    bool Framework_Ecs_HasTilemap(int entity) {
        return g_tilemap.find(entity) != g_tilemap.end();
    }

    void Framework_Ecs_RemoveTilemap(int entity) {
        g_tilemap.erase(entity);
    }

    void Framework_Ecs_SetTile(int entity, int x, int y, int tileIndex) {
        auto it = g_tilemap.find(entity);
        if (it == g_tilemap.end()) return;
        TilemapComponent& tm = it->second;
        if (x < 0 || x >= tm.mapWidth || y < 0 || y >= tm.mapHeight) return;
        tm.tiles[y * tm.mapWidth + x] = tileIndex;
    }

    int Framework_Ecs_GetTile(int entity, int x, int y) {
        auto it = g_tilemap.find(entity);
        if (it == g_tilemap.end()) return -1;
        const TilemapComponent& tm = it->second;
        if (x < 0 || x >= tm.mapWidth || y < 0 || y >= tm.mapHeight) return -1;
        return tm.tiles[y * tm.mapWidth + x];
    }

    void Framework_Ecs_FillTiles(int entity, int tileIndex) {
        auto it = g_tilemap.find(entity);
        if (it == g_tilemap.end()) return;
        std::fill(it->second.tiles.begin(), it->second.tiles.end(), tileIndex);
    }

    void Framework_Ecs_SetTileCollision(int entity, int tileIndex, bool solid) {
        auto it = g_tilemap.find(entity);
        if (it == g_tilemap.end()) return;
        if (solid) {
            it->second.solidTiles.insert(tileIndex);
        } else {
            it->second.solidTiles.erase(tileIndex);
        }
    }

    bool Framework_Ecs_GetTileCollision(int entity, int tileIndex) {
        auto it = g_tilemap.find(entity);
        if (it == g_tilemap.end()) return false;
        return it->second.solidTiles.count(tileIndex) > 0;
    }

    int Framework_Ecs_GetTilemapWidth(int entity) {
        auto it = g_tilemap.find(entity);
        return (it != g_tilemap.end()) ? it->second.mapWidth : 0;
    }

    int Framework_Ecs_GetTilemapHeight(int entity) {
        auto it = g_tilemap.find(entity);
        return (it != g_tilemap.end()) ? it->second.mapHeight : 0;
    }

    void Framework_Ecs_DrawTilemap(int entity) {
        auto tmIt = g_tilemap.find(entity);
        if (tmIt == g_tilemap.end()) return;

        const TilemapComponent& tm = tmIt->second;
        auto tsIt = g_tilesets.find(tm.tilesetHandle);
        if (tsIt == g_tilesets.end() || !tsIt->second.valid) return;

        const Tileset& ts = tsIt->second;
        auto texIt = g_texByHandle.find(ts.textureHandle);
        if (texIt == g_texByHandle.end() || !texIt->second.valid) return;

        // Get entity transform for position offset
        float offsetX = 0, offsetY = 0;
        auto trIt = g_transform2D.find(entity);
        if (trIt != g_transform2D.end()) {
            offsetX = trIt->second.position.x;
            offsetY = trIt->second.position.y;
        }

        const Texture2D& tex = texIt->second.tex;

        for (int y = 0; y < tm.mapHeight; y++) {
            for (int x = 0; x < tm.mapWidth; x++) {
                int tileIdx = tm.tiles[y * tm.mapWidth + x];
                if (tileIdx < 0) continue;

                int srcX = (tileIdx % ts.columns) * ts.tileWidth;
                int srcY = (tileIdx / ts.columns) * ts.tileHeight;

                Rectangle src = { (float)srcX, (float)srcY, (float)ts.tileWidth, (float)ts.tileHeight };
                Vector2 pos = { offsetX + x * ts.tileWidth, offsetY + y * ts.tileHeight };

                DrawTextureRec(tex, src, pos, WHITE);
            }
        }
    }

    void Framework_Tilemaps_Draw() {
        for (auto& kv : g_tilemap) {
            if (!EcsIsAlive(kv.first)) continue;
            auto enIt = g_enabled.find(kv.first);
            if (enIt != g_enabled.end() && !enIt->second.enabled) continue;
            Framework_Ecs_DrawTilemap(kv.first);
        }
    }

    bool Framework_Tilemap_PointSolid(int entity, float worldX, float worldY) {
        auto tmIt = g_tilemap.find(entity);
        if (tmIt == g_tilemap.end()) return false;

        const TilemapComponent& tm = tmIt->second;
        auto tsIt = g_tilesets.find(tm.tilesetHandle);
        if (tsIt == g_tilesets.end()) return false;

        float offsetX = 0, offsetY = 0;
        auto trIt = g_transform2D.find(entity);
        if (trIt != g_transform2D.end()) {
            offsetX = trIt->second.position.x;
            offsetY = trIt->second.position.y;
        }

        int tileX = (int)((worldX - offsetX) / tsIt->second.tileWidth);
        int tileY = (int)((worldY - offsetY) / tsIt->second.tileHeight);

        if (tileX < 0 || tileX >= tm.mapWidth || tileY < 0 || tileY >= tm.mapHeight) return false;

        int tileIdx = tm.tiles[tileY * tm.mapWidth + tileX];
        return tm.solidTiles.count(tileIdx) > 0;
    }

    bool Framework_Tilemap_BoxSolid(int entity, float worldX, float worldY, float w, float h) {
        // Check all four corners and center
        if (Framework_Tilemap_PointSolid(entity, worldX, worldY)) return true;
        if (Framework_Tilemap_PointSolid(entity, worldX + w, worldY)) return true;
        if (Framework_Tilemap_PointSolid(entity, worldX, worldY + h)) return true;
        if (Framework_Tilemap_PointSolid(entity, worldX + w, worldY + h)) return true;
        if (Framework_Tilemap_PointSolid(entity, worldX + w/2, worldY + h/2)) return true;
        return false;
    }

    // ========================================================================
    // ANIMATION SYSTEM
    // ========================================================================
    int Framework_AnimClip_Create(const char* name, int frameCount) {
        AnimClip clip;
        clip.name = name ? name : "";
        clip.frames.resize(frameCount > 0 ? frameCount : 1);
        clip.valid = true;
        int h = g_nextAnimClipHandle++;
        g_animClips[h] = clip;
        return h;
    }

    void Framework_AnimClip_Destroy(int clipHandle) {
        g_animClips.erase(clipHandle);
    }

    bool Framework_AnimClip_IsValid(int clipHandle) {
        auto it = g_animClips.find(clipHandle);
        return it != g_animClips.end() && it->second.valid;
    }

    void Framework_AnimClip_SetFrame(int clipHandle, int frameIndex,
        float srcX, float srcY, float srcW, float srcH, float duration) {
        auto it = g_animClips.find(clipHandle);
        if (it == g_animClips.end()) return;
        if (frameIndex < 0 || frameIndex >= (int)it->second.frames.size()) return;
        AnimFrame& f = it->second.frames[frameIndex];
        f.source = { srcX, srcY, srcW, srcH };
        f.duration = duration > 0 ? duration : 0.1f;
    }

    void Framework_AnimClip_SetLoopMode(int clipHandle, int loopMode) {
        auto it = g_animClips.find(clipHandle);
        if (it == g_animClips.end()) return;
        it->second.loopMode = loopMode;
    }

    int Framework_AnimClip_GetFrameCount(int clipHandle) {
        auto it = g_animClips.find(clipHandle);
        return (it != g_animClips.end()) ? (int)it->second.frames.size() : 0;
    }

    float Framework_AnimClip_GetTotalDuration(int clipHandle) {
        auto it = g_animClips.find(clipHandle);
        if (it == g_animClips.end()) return 0.0f;
        float total = 0.0f;
        for (const auto& f : it->second.frames) {
            total += f.duration;
        }
        return total;
    }

    int Framework_AnimClip_FindByName(const char* name) {
        if (!name) return -1;
        for (const auto& kv : g_animClips) {
            if (kv.second.name == name) return kv.first;
        }
        return -1;
    }

    void Framework_Ecs_AddAnimator(int entity) {
        if (!EcsIsAlive(entity)) return;
        g_animator[entity] = AnimatorComponent();
    }

    bool Framework_Ecs_HasAnimator(int entity) {
        return g_animator.find(entity) != g_animator.end();
    }

    void Framework_Ecs_RemoveAnimator(int entity) {
        g_animator.erase(entity);
    }

    void Framework_Ecs_SetAnimatorClip(int entity, int clipHandle) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.clipHandle = clipHandle;
        it->second.currentFrame = 0;
        it->second.timer = 0.0f;
        it->second.pingpongReverse = false;
    }

    int Framework_Ecs_GetAnimatorClip(int entity) {
        auto it = g_animator.find(entity);
        return (it != g_animator.end()) ? it->second.clipHandle : -1;
    }

    void Framework_Ecs_AnimatorPlay(int entity) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.playing = true;
    }

    void Framework_Ecs_AnimatorPause(int entity) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.playing = false;
    }

    void Framework_Ecs_AnimatorStop(int entity) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.playing = false;
        it->second.currentFrame = 0;
        it->second.timer = 0.0f;
        it->second.pingpongReverse = false;
    }

    void Framework_Ecs_AnimatorSetSpeed(int entity, float speed) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.speed = speed;
    }

    bool Framework_Ecs_AnimatorIsPlaying(int entity) {
        auto it = g_animator.find(entity);
        return (it != g_animator.end()) ? it->second.playing : false;
    }

    int Framework_Ecs_AnimatorGetFrame(int entity) {
        auto it = g_animator.find(entity);
        return (it != g_animator.end()) ? it->second.currentFrame : 0;
    }

    void Framework_Ecs_AnimatorSetFrame(int entity, int frameIndex) {
        auto it = g_animator.find(entity);
        if (it == g_animator.end()) return;
        it->second.currentFrame = frameIndex;
        it->second.timer = 0.0f;
    }

    void Framework_Animators_Update(float dt) {
        for (auto& kv : g_animator) {
            if (!EcsIsAlive(kv.first)) continue;
            AnimatorComponent& anim = kv.second;
            if (!anim.playing) continue;

            auto clipIt = g_animClips.find(anim.clipHandle);
            if (clipIt == g_animClips.end() || clipIt->second.frames.empty()) continue;

            const AnimClip& clip = clipIt->second;
            const AnimFrame& frame = clip.frames[anim.currentFrame];

            anim.timer += dt * anim.speed;

            if (anim.timer >= frame.duration) {
                anim.timer -= frame.duration;

                int frameCount = (int)clip.frames.size();
                if (clip.loopMode == ANIM_LOOP_PINGPONG) {
                    if (anim.pingpongReverse) {
                        anim.currentFrame--;
                        if (anim.currentFrame <= 0) {
                            anim.currentFrame = 0;
                            anim.pingpongReverse = false;
                        }
                    } else {
                        anim.currentFrame++;
                        if (anim.currentFrame >= frameCount - 1) {
                            anim.currentFrame = frameCount - 1;
                            anim.pingpongReverse = true;
                        }
                    }
                } else {
                    anim.currentFrame++;
                    if (anim.currentFrame >= frameCount) {
                        if (clip.loopMode == ANIM_LOOP_REPEAT) {
                            anim.currentFrame = 0;
                        } else {
                            anim.currentFrame = frameCount - 1;
                            anim.playing = false;
                        }
                    }
                }
            }

            // Update sprite source rect if entity has a sprite
            auto sprIt = g_sprite2D.find(kv.first);
            if (sprIt != g_sprite2D.end()) {
                sprIt->second.source = clip.frames[anim.currentFrame].source;
            }
        }
    }

    // ========================================================================
    // PARTICLE SYSTEM
    // ========================================================================
    namespace {
        float RandFloat(float minVal, float maxVal) {
            return minVal + static_cast<float>(rand()) / (static_cast<float>(RAND_MAX / (maxVal - minVal)));
        }

        unsigned char LerpByte(unsigned char a, unsigned char b, float t) {
            return (unsigned char)(a + (b - a) * t);
        }
    }

    void Framework_Ecs_AddParticleEmitter(int entity, int textureHandle) {
        if (!EcsIsAlive(entity)) return;
        ParticleEmitterComponent pe;
        pe.textureHandle = textureHandle;
        pe.particles.resize(pe.maxParticles);
        g_particleEmitter[entity] = pe;
    }

    bool Framework_Ecs_HasParticleEmitter(int entity) {
        return g_particleEmitter.find(entity) != g_particleEmitter.end();
    }

    void Framework_Ecs_RemoveParticleEmitter(int entity) {
        g_particleEmitter.erase(entity);
    }

    void Framework_Ecs_SetEmitterRate(int entity, float particlesPerSecond) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.emissionRate = particlesPerSecond;
    }

    void Framework_Ecs_SetEmitterLifetime(int entity, float minLife, float maxLife) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.lifetimeMin = minLife;
        it->second.lifetimeMax = maxLife;
    }

    void Framework_Ecs_SetEmitterVelocity(int entity, float minVx, float minVy, float maxVx, float maxVy) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.velocityMinX = minVx;
        it->second.velocityMinY = minVy;
        it->second.velocityMaxX = maxVx;
        it->second.velocityMaxY = maxVy;
    }

    void Framework_Ecs_SetEmitterColorStart(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.colorStart = { r, g, b, a };
    }

    void Framework_Ecs_SetEmitterColorEnd(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.colorEnd = { r, g, b, a };
    }

    void Framework_Ecs_SetEmitterSize(int entity, float startSize, float endSize) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.sizeStart = startSize;
        it->second.sizeEnd = endSize;
    }

    void Framework_Ecs_SetEmitterGravity(int entity, float gx, float gy) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.gravityX = gx;
        it->second.gravityY = gy;
    }

    void Framework_Ecs_SetEmitterSpread(int entity, float angleDegrees) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.spreadAngle = angleDegrees;
    }

    void Framework_Ecs_SetEmitterDirection(int entity, float dirX, float dirY) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.directionX = dirX;
        it->second.directionY = dirY;
    }

    void Framework_Ecs_SetEmitterMaxParticles(int entity, int maxParticles) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.maxParticles = maxParticles > 0 ? maxParticles : 1;
        it->second.particles.resize(it->second.maxParticles);
    }

    void Framework_Ecs_SetEmitterSourceRect(int entity, float srcX, float srcY, float srcW, float srcH) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.sourceRect = { srcX, srcY, srcW, srcH };
    }

    void Framework_Ecs_EmitterStart(int entity) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.active = true;
    }

    void Framework_Ecs_EmitterStop(int entity) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        it->second.active = false;
    }

    void Framework_Ecs_EmitterBurst(int entity, int count) {
        auto peIt = g_particleEmitter.find(entity);
        if (peIt == g_particleEmitter.end()) return;

        ParticleEmitterComponent& pe = peIt->second;

        // Get emitter world position
        float emitX = 0, emitY = 0;
        auto trIt = g_transform2D.find(entity);
        if (trIt != g_transform2D.end()) {
            emitX = trIt->second.position.x;
            emitY = trIt->second.position.y;
        }

        float baseAngle = atan2f(pe.directionY, pe.directionX);
        float spreadRad = pe.spreadAngle * DEG2RAD;

        for (int i = 0; i < count; i++) {
            // Find inactive particle
            for (auto& p : pe.particles) {
                if (!p.active) {
                    p.active = true;
                    p.x = emitX;
                    p.y = emitY;
                    p.maxLife = RandFloat(pe.lifetimeMin, pe.lifetimeMax);
                    p.life = p.maxLife;
                    p.size = pe.sizeStart;

                    // Calculate velocity with spread
                    float angle = baseAngle + RandFloat(-spreadRad/2, spreadRad/2);
                    float speed = RandFloat(
                        sqrtf(pe.velocityMinX*pe.velocityMinX + pe.velocityMinY*pe.velocityMinY),
                        sqrtf(pe.velocityMaxX*pe.velocityMaxX + pe.velocityMaxY*pe.velocityMaxY)
                    );
                    p.vx = cosf(angle) * speed;
                    p.vy = sinf(angle) * speed;
                    break;
                }
            }
        }
    }

    bool Framework_Ecs_EmitterIsActive(int entity) {
        auto it = g_particleEmitter.find(entity);
        return (it != g_particleEmitter.end()) ? it->second.active : false;
    }

    int Framework_Ecs_EmitterGetParticleCount(int entity) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return 0;
        int count = 0;
        for (const auto& p : it->second.particles) {
            if (p.active) count++;
        }
        return count;
    }

    void Framework_Ecs_EmitterClear(int entity) {
        auto it = g_particleEmitter.find(entity);
        if (it == g_particleEmitter.end()) return;
        for (auto& p : it->second.particles) {
            p.active = false;
        }
    }

    void Framework_Particles_Update(float dt) {
        for (auto& kv : g_particleEmitter) {
            if (!EcsIsAlive(kv.first)) continue;
            ParticleEmitterComponent& pe = kv.second;

            // Spawn new particles if active
            if (pe.active && pe.emissionRate > 0) {
                pe.emissionAccum += dt * pe.emissionRate;
                while (pe.emissionAccum >= 1.0f) {
                    pe.emissionAccum -= 1.0f;
                    Framework_Ecs_EmitterBurst(kv.first, 1);
                }
            }

            // Update existing particles
            for (auto& p : pe.particles) {
                if (!p.active) continue;

                p.life -= dt;
                if (p.life <= 0) {
                    p.active = false;
                    continue;
                }

                // Apply velocity and gravity
                p.vx += pe.gravityX * dt;
                p.vy += pe.gravityY * dt;
                p.x += p.vx * dt;
                p.y += p.vy * dt;

                // Interpolate size
                float t = 1.0f - (p.life / p.maxLife);
                p.size = pe.sizeStart + (pe.sizeEnd - pe.sizeStart) * t;
            }
        }
    }

    void Framework_Particles_Draw() {
        for (auto& kv : g_particleEmitter) {
            if (!EcsIsAlive(kv.first)) continue;
            ParticleEmitterComponent& pe = kv.second;

            // Get texture if available
            Texture2D* tex = nullptr;
            auto texIt = g_texByHandle.find(pe.textureHandle);
            if (texIt != g_texByHandle.end() && texIt->second.valid) {
                tex = &texIt->second.tex;
            }

            for (const auto& p : pe.particles) {
                if (!p.active) continue;

                // Calculate color
                float t = 1.0f - (p.life / p.maxLife);
                Color c;
                c.r = LerpByte(pe.colorStart.r, pe.colorEnd.r, t);
                c.g = LerpByte(pe.colorStart.g, pe.colorEnd.g, t);
                c.b = LerpByte(pe.colorStart.b, pe.colorEnd.b, t);
                c.a = LerpByte(pe.colorStart.a, pe.colorEnd.a, t);

                if (tex && pe.sourceRect.width > 0 && pe.sourceRect.height > 0) {
                    // Draw textured particle
                    Rectangle dest = { p.x - p.size/2, p.y - p.size/2, p.size, p.size };
                    DrawTexturePro(*tex, pe.sourceRect, dest, {0, 0}, 0, c);
                } else {
                    // Draw as circle
                    DrawCircle((int)p.x, (int)p.y, p.size/2, c);
                }
            }
        }
    }

    // ========================================================================
    // UI SYSTEM - Implementation
    // ========================================================================

    // UI Element structure
    struct UIElement {
        int id = -1;
        int type = UI_LABEL;
        int state = UI_STATE_NORMAL;
        int anchor = UI_ANCHOR_TOP_LEFT;
        int parent = -1;
        int layer = 0;

        // Position and size
        float x = 0, y = 0;
        float width = 100, height = 30;
        float padding[4] = {5, 5, 5, 5};  // left, top, right, bottom

        // Text properties
        std::string text;
        std::string placeholder;
        int fontHandle = 0;
        float fontSize = 20.0f;
        Color textColor = WHITE;
        int textAlign = UI_ANCHOR_CENTER_LEFT;

        // Colors
        Color bgColor = { 60, 60, 60, 255 };
        Color borderColor = { 100, 100, 100, 255 };
        Color hoverColor = { 80, 80, 80, 255 };
        Color pressedColor = { 40, 40, 40, 255 };
        Color disabledColor = { 40, 40, 40, 150 };
        float borderWidth = 1.0f;
        float cornerRadius = 0.0f;

        // Value properties (slider, progress, checkbox)
        float value = 0.0f;
        float minValue = 0.0f;
        float maxValue = 1.0f;
        bool checked = false;

        // TextInput specific
        int maxLength = 256;
        bool passwordMode = false;
        int cursorPos = 0;
        float cursorBlinkTimer = 0.0f;

        // Image specific
        int textureHandle = 0;
        Rectangle sourceRect = {0, 0, 0, 0};
        Color tint = WHITE;

        // State
        bool visible = true;
        bool enabled = true;
        bool valid = true;

        // Callbacks (stored as function pointers)
        UICallback onClick = nullptr;
        UICallback onHover = nullptr;
        UIValueCallback onValueChanged = nullptr;
        UITextCallback onTextChanged = nullptr;
    };

    // UI Storage
    static std::unordered_map<int, UIElement> g_uiElements;
    static int g_uiNextId = 1;
    static int g_uiFocusedId = -1;
    static int g_uiHoveredId = -1;

    // Helper: Get computed position based on anchor
    static Vector2 UI_GetAnchoredPosition(const UIElement& el) {
        float baseX = el.x;
        float baseY = el.y;

        // Get parent bounds or screen bounds
        float parentX = 0, parentY = 0;
        float parentW = (float)GetScreenWidth();
        float parentH = (float)GetScreenHeight();

        if (el.parent >= 0) {
            auto pit = g_uiElements.find(el.parent);
            if (pit != g_uiElements.end() && pit->second.valid) {
                Vector2 ppos = UI_GetAnchoredPosition(pit->second);
                parentX = ppos.x;
                parentY = ppos.y;
                parentW = pit->second.width;
                parentH = pit->second.height;
            }
        }

        float anchorX = parentX, anchorY = parentY;
        switch (el.anchor) {
            case UI_ANCHOR_TOP_LEFT:      anchorX = parentX; anchorY = parentY; break;
            case UI_ANCHOR_TOP_CENTER:    anchorX = parentX + parentW/2 - el.width/2; anchorY = parentY; break;
            case UI_ANCHOR_TOP_RIGHT:     anchorX = parentX + parentW - el.width; anchorY = parentY; break;
            case UI_ANCHOR_CENTER_LEFT:   anchorX = parentX; anchorY = parentY + parentH/2 - el.height/2; break;
            case UI_ANCHOR_CENTER:        anchorX = parentX + parentW/2 - el.width/2; anchorY = parentY + parentH/2 - el.height/2; break;
            case UI_ANCHOR_CENTER_RIGHT:  anchorX = parentX + parentW - el.width; anchorY = parentY + parentH/2 - el.height/2; break;
            case UI_ANCHOR_BOTTOM_LEFT:   anchorX = parentX; anchorY = parentY + parentH - el.height; break;
            case UI_ANCHOR_BOTTOM_CENTER: anchorX = parentX + parentW/2 - el.width/2; anchorY = parentY + parentH - el.height; break;
            case UI_ANCHOR_BOTTOM_RIGHT:  anchorX = parentX + parentW - el.width; anchorY = parentY + parentH - el.height; break;
        }

        return { anchorX + baseX, anchorY + baseY };
    }

    // Helper: Point in rect
    static bool UI_PointInRect(float px, float py, float rx, float ry, float rw, float rh) {
        return px >= rx && px <= rx + rw && py >= ry && py <= ry + rh;
    }

    // Helper: Draw rounded rectangle
    static void UI_DrawRoundedRect(float x, float y, float w, float h, float radius, Color color) {
        if (radius <= 0) {
            DrawRectangle((int)x, (int)y, (int)w, (int)h, color);
        } else {
            DrawRectangleRounded({x, y, w, h}, radius / (w < h ? w : h), 8, color);
        }
    }

    // Helper: Get font by handle (for UI)
    static Font UI_GetFontByHandle(int handle) {
        auto it = g_fontByHandle.find(handle);
        if (it != g_fontByHandle.end() && it->second.valid) {
            return it->second.font;
        }
        return GetFontDefault();
    }

    // Helper: Draw text with alignment
    static void UI_DrawAlignedText(const char* text, float x, float y, float w, float h, int fontH, float fontSize, int align, Color color) {
        Font font = UI_GetFontByHandle(fontH);
        Vector2 textSize = MeasureTextEx(font, text, fontSize, 1);

        float tx = x, ty = y;
        switch (align) {
            case UI_ANCHOR_TOP_LEFT:      tx = x; ty = y; break;
            case UI_ANCHOR_TOP_CENTER:    tx = x + w/2 - textSize.x/2; ty = y; break;
            case UI_ANCHOR_TOP_RIGHT:     tx = x + w - textSize.x; ty = y; break;
            case UI_ANCHOR_CENTER_LEFT:   tx = x; ty = y + h/2 - textSize.y/2; break;
            case UI_ANCHOR_CENTER:        tx = x + w/2 - textSize.x/2; ty = y + h/2 - textSize.y/2; break;
            case UI_ANCHOR_CENTER_RIGHT:  tx = x + w - textSize.x; ty = y + h/2 - textSize.y/2; break;
            case UI_ANCHOR_BOTTOM_LEFT:   tx = x; ty = y + h - textSize.y; break;
            case UI_ANCHOR_BOTTOM_CENTER: tx = x + w/2 - textSize.x/2; ty = y + h - textSize.y; break;
            case UI_ANCHOR_BOTTOM_RIGHT:  tx = x + w - textSize.x; ty = y + h - textSize.y; break;
        }

        DrawTextEx(font, text, {tx, ty}, fontSize, 1, color);
    }

    // Create functions
    int Framework_UI_CreateLabel(const char* text, float x, float y) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_LABEL;
        el.x = x; el.y = y;
        el.text = text ? text : "";
        el.bgColor = {0, 0, 0, 0};  // Transparent by default
        el.borderWidth = 0;

        // Auto-size based on text
        Vector2 textSize = MeasureTextEx(GetFontDefault(), el.text.c_str(), el.fontSize, 1);
        el.width = textSize.x + el.padding[0] + el.padding[2];
        el.height = textSize.y + el.padding[1] + el.padding[3];

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateButton(const char* text, float x, float y, float width, float height) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_BUTTON;
        el.x = x; el.y = y;
        el.width = width; el.height = height;
        el.text = text ? text : "";
        el.textAlign = UI_ANCHOR_CENTER;
        el.bgColor = { 70, 130, 180, 255 };  // Steel blue
        el.hoverColor = { 100, 149, 237, 255 };  // Cornflower blue
        el.pressedColor = { 30, 90, 140, 255 };
        el.cornerRadius = 4.0f;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreatePanel(float x, float y, float width, float height) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_PANEL;
        el.x = x; el.y = y;
        el.width = width; el.height = height;
        el.bgColor = { 45, 45, 48, 240 };
        el.borderColor = { 80, 80, 80, 255 };
        el.cornerRadius = 8.0f;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateSlider(float x, float y, float width, float minVal, float maxVal, float initialVal) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_SLIDER;
        el.x = x; el.y = y;
        el.width = width; el.height = 20;
        el.minValue = minVal; el.maxValue = maxVal;
        el.value = initialVal;
        el.bgColor = { 60, 60, 60, 255 };
        el.hoverColor = { 70, 130, 180, 255 };  // Track fill color
        el.pressedColor = { 100, 149, 237, 255 };  // Handle color
        el.cornerRadius = 4.0f;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateCheckbox(const char* text, float x, float y, bool initialState) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_CHECKBOX;
        el.x = x; el.y = y;
        el.width = 24; el.height = 24;
        el.text = text ? text : "";
        el.checked = initialState;
        el.value = initialState ? 1.0f : 0.0f;
        el.bgColor = { 60, 60, 60, 255 };
        el.hoverColor = { 80, 80, 80, 255 };
        el.pressedColor = { 70, 130, 180, 255 };  // Checked color
        el.cornerRadius = 4.0f;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateTextInput(float x, float y, float width, float height, const char* placeholder) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_TEXTINPUT;
        el.x = x; el.y = y;
        el.width = width; el.height = height;
        el.placeholder = placeholder ? placeholder : "";
        el.bgColor = { 30, 30, 30, 255 };
        el.borderColor = { 100, 100, 100, 255 };
        el.hoverColor = { 70, 130, 180, 255 };  // Focus border color
        el.cornerRadius = 4.0f;
        el.textAlign = UI_ANCHOR_CENTER_LEFT;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateProgressBar(float x, float y, float width, float height, float initialValue) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_PROGRESSBAR;
        el.x = x; el.y = y;
        el.width = width; el.height = height;
        el.value = initialValue;
        el.bgColor = { 40, 40, 40, 255 };
        el.hoverColor = { 76, 175, 80, 255 };  // Fill color (green)
        el.cornerRadius = 4.0f;

        g_uiElements[el.id] = el;
        return el.id;
    }

    int Framework_UI_CreateImage(int textureHandle, float x, float y, float width, float height) {
        UIElement el;
        el.id = g_uiNextId++;
        el.type = UI_IMAGE;
        el.x = x; el.y = y;
        el.width = width; el.height = height;
        el.textureHandle = textureHandle;
        el.tint = WHITE;
        el.bgColor = {0, 0, 0, 0};

        g_uiElements[el.id] = el;
        return el.id;
    }

    void Framework_UI_Destroy(int elementId) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.valid = false;
            g_uiElements.erase(it);
        }
        if (g_uiFocusedId == elementId) g_uiFocusedId = -1;
        if (g_uiHoveredId == elementId) g_uiHoveredId = -1;
    }

    void Framework_UI_DestroyAll() {
        g_uiElements.clear();
        g_uiFocusedId = -1;
        g_uiHoveredId = -1;
    }

    bool Framework_UI_IsValid(int elementId) {
        auto it = g_uiElements.find(elementId);
        return it != g_uiElements.end() && it->second.valid;
    }

    // Property setters - Common
    void Framework_UI_SetPosition(int elementId, float x, float y) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.x = x; it->second.y = y; }
    }

    void Framework_UI_SetSize(int elementId, float width, float height) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.width = width; it->second.height = height; }
    }

    void Framework_UI_SetAnchor(int elementId, int anchor) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.anchor = anchor; }
    }

    void Framework_UI_SetVisible(int elementId, bool visible) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.visible = visible; }
    }

    void Framework_UI_SetEnabled(int elementId, bool enabled) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.enabled = enabled;
            it->second.state = enabled ? UI_STATE_NORMAL : UI_STATE_DISABLED;
        }
    }

    void Framework_UI_SetParent(int elementId, int parentId) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.parent = parentId; }
    }

    void Framework_UI_SetLayer(int elementId, int layer) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.layer = layer; }
    }

    float Framework_UI_GetX(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.x : 0;
    }

    float Framework_UI_GetY(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.y : 0;
    }

    float Framework_UI_GetWidth(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.width : 0;
    }

    float Framework_UI_GetHeight(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.height : 0;
    }

    int Framework_UI_GetState(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.state : UI_STATE_NORMAL;
    }

    int Framework_UI_GetType(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.type : UI_LABEL;
    }

    bool Framework_UI_IsVisible(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.visible : false;
    }

    bool Framework_UI_IsEnabled(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.enabled : false;
    }

    // Property setters - Text/Font
    void Framework_UI_SetText(int elementId, const char* text) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.text = text ? text : ""; }
    }

    const char* Framework_UI_GetText(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.text.c_str() : "";
    }

    void Framework_UI_SetFont(int elementId, int fontHandle) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.fontHandle = fontHandle; }
    }

    void Framework_UI_SetFontSize(int elementId, float size) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.fontSize = size; }
    }

    void Framework_UI_SetTextColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.textColor = {r, g, b, a}; }
    }

    void Framework_UI_SetTextAlign(int elementId, int anchor) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.textAlign = anchor; }
    }

    // Property setters - Colors
    void Framework_UI_SetBackgroundColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.bgColor = {r, g, b, a}; }
    }

    void Framework_UI_SetBorderColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.borderColor = {r, g, b, a}; }
    }

    void Framework_UI_SetHoverColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.hoverColor = {r, g, b, a}; }
    }

    void Framework_UI_SetPressedColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.pressedColor = {r, g, b, a}; }
    }

    void Framework_UI_SetDisabledColor(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.disabledColor = {r, g, b, a}; }
    }

    void Framework_UI_SetBorderWidth(int elementId, float width) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.borderWidth = width; }
    }

    void Framework_UI_SetCornerRadius(int elementId, float radius) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.cornerRadius = radius; }
    }

    void Framework_UI_SetPadding(int elementId, float left, float top, float right, float bottom) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.padding[0] = left;
            it->second.padding[1] = top;
            it->second.padding[2] = right;
            it->second.padding[3] = bottom;
        }
    }

    // Property setters - Value-based
    void Framework_UI_SetValue(int elementId, float value) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            float clamped = fmaxf(it->second.minValue, fminf(it->second.maxValue, value));
            it->second.value = clamped;
        }
    }

    float Framework_UI_GetValue(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.value : 0;
    }

    void Framework_UI_SetMinMax(int elementId, float minVal, float maxVal) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.minValue = minVal;
            it->second.maxValue = maxVal;
        }
    }

    void Framework_UI_SetChecked(int elementId, bool checked) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.checked = checked;
            it->second.value = checked ? 1.0f : 0.0f;
        }
    }

    bool Framework_UI_IsChecked(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.checked : false;
    }

    // Property setters - TextInput specific
    void Framework_UI_SetPlaceholder(int elementId, const char* text) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.placeholder = text ? text : ""; }
    }

    void Framework_UI_SetMaxLength(int elementId, int maxLength) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.maxLength = maxLength; }
    }

    void Framework_UI_SetPasswordMode(int elementId, bool isPassword) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.passwordMode = isPassword; }
    }

    void Framework_UI_SetCursorPosition(int elementId, int position) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) {
            it->second.cursorPos = std::max(0, std::min(position, (int)it->second.text.length()));
        }
    }

    int Framework_UI_GetCursorPosition(int elementId) {
        auto it = g_uiElements.find(elementId);
        return (it != g_uiElements.end()) ? it->second.cursorPos : 0;
    }

    // Property setters - Image specific
    void Framework_UI_SetTexture(int elementId, int textureHandle) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.textureHandle = textureHandle; }
    }

    void Framework_UI_SetSourceRect(int elementId, float srcX, float srcY, float srcW, float srcH) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.sourceRect = {srcX, srcY, srcW, srcH}; }
    }

    void Framework_UI_SetTint(int elementId, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.tint = {r, g, b, a}; }
    }

    // Callbacks
    void Framework_UI_SetClickCallback(int elementId, UICallback callback) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.onClick = callback; }
    }

    void Framework_UI_SetHoverCallback(int elementId, UICallback callback) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.onHover = callback; }
    }

    void Framework_UI_SetValueChangedCallback(int elementId, UIValueCallback callback) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.onValueChanged = callback; }
    }

    void Framework_UI_SetTextChangedCallback(int elementId, UITextCallback callback) {
        auto it = g_uiElements.find(elementId);
        if (it != g_uiElements.end()) { it->second.onTextChanged = callback; }
    }

    // UI Update - Process input and states
    void Framework_UI_Update() {
        Vector2 mousePos = GetMousePosition();
        bool mousePressed = IsMouseButtonPressed(MOUSE_LEFT_BUTTON);
        bool mouseDown = IsMouseButtonDown(MOUSE_LEFT_BUTTON);
        bool mouseReleased = IsMouseButtonReleased(MOUSE_LEFT_BUTTON);

        int newHovered = -1;

        // Collect and sort elements by layer (higher layers on top)
        std::vector<std::pair<int, UIElement*>> sortedElements;
        for (auto& kv : g_uiElements) {
            if (kv.second.valid && kv.second.visible && kv.second.enabled) {
                sortedElements.push_back({kv.first, &kv.second});
            }
        }
        std::sort(sortedElements.begin(), sortedElements.end(),
            [](const auto& a, const auto& b) { return a.second->layer > b.second->layer; });

        // Find topmost hovered element
        for (auto& [id, el] : sortedElements) {
            Vector2 pos = UI_GetAnchoredPosition(*el);
            if (UI_PointInRect(mousePos.x, mousePos.y, pos.x, pos.y, el->width, el->height)) {
                newHovered = id;
                break;  // Topmost wins
            }
        }

        // Update hover state
        if (newHovered != g_uiHoveredId) {
            if (g_uiHoveredId >= 0) {
                auto it = g_uiElements.find(g_uiHoveredId);
                if (it != g_uiElements.end() && it->second.state == UI_STATE_HOVERED) {
                    it->second.state = UI_STATE_NORMAL;
                }
            }
            g_uiHoveredId = newHovered;
            if (newHovered >= 0) {
                auto it = g_uiElements.find(newHovered);
                if (it != g_uiElements.end()) {
                    it->second.state = UI_STATE_HOVERED;
                    if (it->second.onHover) it->second.onHover(newHovered);
                }
            }
        }

        // Process clicks
        if (mousePressed && newHovered >= 0) {
            auto it = g_uiElements.find(newHovered);
            if (it != g_uiElements.end()) {
                it->second.state = UI_STATE_PRESSED;

                // Set focus for text inputs
                if (it->second.type == UI_TEXTINPUT) {
                    g_uiFocusedId = newHovered;
                    it->second.state = UI_STATE_FOCUSED;
                }
            }
        }

        // Click outside clears focus
        if (mousePressed && newHovered < 0) {
            g_uiFocusedId = -1;
        }

        // Process release (click complete)
        if (mouseReleased && newHovered >= 0) {
            auto it = g_uiElements.find(newHovered);
            if (it != g_uiElements.end() && it->second.state == UI_STATE_PRESSED) {
                it->second.state = UI_STATE_HOVERED;

                // Handle click by type
                switch (it->second.type) {
                    case UI_BUTTON:
                        if (it->second.onClick) it->second.onClick(newHovered);
                        break;
                    case UI_CHECKBOX:
                        it->second.checked = !it->second.checked;
                        it->second.value = it->second.checked ? 1.0f : 0.0f;
                        if (it->second.onValueChanged) it->second.onValueChanged(newHovered, it->second.value);
                        break;
                }
            }
        }

        // Slider dragging
        for (auto& kv : g_uiElements) {
            if (kv.second.type == UI_SLIDER && kv.second.state == UI_STATE_PRESSED && mouseDown) {
                Vector2 pos = UI_GetAnchoredPosition(kv.second);
                float relX = mousePos.x - pos.x;
                float ratio = fmaxf(0, fminf(1, relX / kv.second.width));
                float newValue = kv.second.minValue + ratio * (kv.second.maxValue - kv.second.minValue);
                if (newValue != kv.second.value) {
                    kv.second.value = newValue;
                    if (kv.second.onValueChanged) kv.second.onValueChanged(kv.first, newValue);
                }
            }
        }

        // Text input handling
        if (g_uiFocusedId >= 0) {
            auto it = g_uiElements.find(g_uiFocusedId);
            if (it != g_uiElements.end() && it->second.type == UI_TEXTINPUT) {
                UIElement& el = it->second;
                el.cursorBlinkTimer += GetFrameTime();

                // Handle character input
                int key = GetCharPressed();
                while (key > 0) {
                    if ((int)el.text.length() < el.maxLength && key >= 32 && key <= 126) {
                        el.text.insert(el.cursorPos, 1, (char)key);
                        el.cursorPos++;
                        if (el.onTextChanged) el.onTextChanged(g_uiFocusedId, el.text.c_str());
                    }
                    key = GetCharPressed();
                }

                // Handle special keys
                if (IsKeyPressed(KEY_BACKSPACE) && el.cursorPos > 0) {
                    el.text.erase(el.cursorPos - 1, 1);
                    el.cursorPos--;
                    if (el.onTextChanged) el.onTextChanged(g_uiFocusedId, el.text.c_str());
                }
                if (IsKeyPressed(KEY_DELETE) && el.cursorPos < (int)el.text.length()) {
                    el.text.erase(el.cursorPos, 1);
                    if (el.onTextChanged) el.onTextChanged(g_uiFocusedId, el.text.c_str());
                }
                if (IsKeyPressed(KEY_LEFT) && el.cursorPos > 0) el.cursorPos--;
                if (IsKeyPressed(KEY_RIGHT) && el.cursorPos < (int)el.text.length()) el.cursorPos++;
                if (IsKeyPressed(KEY_HOME)) el.cursorPos = 0;
                if (IsKeyPressed(KEY_END)) el.cursorPos = (int)el.text.length();
            }
        }
    }

    // UI Draw
    void Framework_UI_Draw() {
        // Collect and sort elements by layer
        std::vector<std::pair<int, UIElement*>> sortedElements;
        for (auto& kv : g_uiElements) {
            if (kv.second.valid && kv.second.visible) {
                sortedElements.push_back({kv.first, &kv.second});
            }
        }
        std::sort(sortedElements.begin(), sortedElements.end(),
            [](const auto& a, const auto& b) { return a.second->layer < b.second->layer; });

        for (auto& [id, el] : sortedElements) {
            Vector2 pos = UI_GetAnchoredPosition(*el);
            float x = pos.x, y = pos.y, w = el->width, h = el->height;

            Color bgColor = el->bgColor;
            if (!el->enabled) bgColor = el->disabledColor;
            else if (el->state == UI_STATE_PRESSED) bgColor = el->pressedColor;
            else if (el->state == UI_STATE_HOVERED) bgColor = el->hoverColor;
            else if (el->state == UI_STATE_FOCUSED) bgColor = el->bgColor;

            switch (el->type) {
                case UI_LABEL: {
                    if (bgColor.a > 0) UI_DrawRoundedRect(x, y, w, h, el->cornerRadius, bgColor);
                    UI_DrawAlignedText(el->text.c_str(), x + el->padding[0], y + el->padding[1],
                        w - el->padding[0] - el->padding[2], h - el->padding[1] - el->padding[3],
                        el->fontHandle, el->fontSize, el->textAlign, el->textColor);
                    break;
                }

                case UI_BUTTON: {
                    UI_DrawRoundedRect(x, y, w, h, el->cornerRadius, bgColor);
                    if (el->borderWidth > 0) {
                        DrawRectangleLinesEx({x, y, w, h}, el->borderWidth, el->borderColor);
                    }
                    UI_DrawAlignedText(el->text.c_str(), x, y, w, h, el->fontHandle, el->fontSize, el->textAlign, el->textColor);
                    break;
                }

                case UI_PANEL: {
                    UI_DrawRoundedRect(x, y, w, h, el->cornerRadius, bgColor);
                    if (el->borderWidth > 0) {
                        DrawRectangleLinesEx({x, y, w, h}, el->borderWidth, el->borderColor);
                    }
                    break;
                }

                case UI_SLIDER: {
                    // Track background
                    UI_DrawRoundedRect(x, y + h/2 - 4, w, 8, 4, el->bgColor);
                    // Filled portion
                    float ratio = (el->value - el->minValue) / (el->maxValue - el->minValue);
                    UI_DrawRoundedRect(x, y + h/2 - 4, w * ratio, 8, 4, el->hoverColor);
                    // Handle
                    float handleX = x + w * ratio - 8;
                    DrawCircle((int)(handleX + 8), (int)(y + h/2), 10, el->pressedColor);
                    break;
                }

                case UI_CHECKBOX: {
                    // Box
                    UI_DrawRoundedRect(x, y, 24, 24, el->cornerRadius, bgColor);
                    DrawRectangleLinesEx({x, y, 24, 24}, el->borderWidth, el->borderColor);
                    // Checkmark
                    if (el->checked) {
                        DrawLine((int)(x + 5), (int)(y + 12), (int)(x + 10), (int)(y + 18), el->pressedColor);
                        DrawLine((int)(x + 10), (int)(y + 18), (int)(x + 19), (int)(y + 6), el->pressedColor);
                    }
                    // Label
                    if (!el->text.empty()) {
                        UI_DrawAlignedText(el->text.c_str(), x + 30, y, w, 24, el->fontHandle, el->fontSize, UI_ANCHOR_CENTER_LEFT, el->textColor);
                    }
                    break;
                }

                case UI_TEXTINPUT: {
                    Color borderCol = (el->state == UI_STATE_FOCUSED) ? el->hoverColor : el->borderColor;
                    UI_DrawRoundedRect(x, y, w, h, el->cornerRadius, el->bgColor);
                    DrawRectangleLinesEx({x, y, w, h}, el->borderWidth, borderCol);

                    // Text or placeholder
                    const char* displayText = el->text.empty() ? el->placeholder.c_str() : el->text.c_str();
                    Color textCol = el->text.empty() ? Color{150, 150, 150, 255} : el->textColor;

                    std::string masked = el->text;
                    if (el->passwordMode && !el->text.empty()) {
                        masked = std::string(el->text.length(), '*');
                        displayText = masked.c_str();
                    }

                    UI_DrawAlignedText(displayText, x + el->padding[0], y, w - el->padding[0] - el->padding[2], h, el->fontHandle, el->fontSize, el->textAlign, textCol);

                    // Cursor
                    if (el->state == UI_STATE_FOCUSED && fmod(el->cursorBlinkTimer, 1.0f) < 0.5f) {
                        Font font = UI_GetFontByHandle(el->fontHandle);
                        std::string beforeCursor = el->passwordMode ? std::string(el->cursorPos, '*') : el->text.substr(0, el->cursorPos);
                        Vector2 textSize = MeasureTextEx(font, beforeCursor.c_str(), el->fontSize, 1);
                        float cursorX = x + el->padding[0] + textSize.x;
                        DrawLine((int)cursorX, (int)(y + 4), (int)cursorX, (int)(y + h - 4), el->textColor);
                    }
                    break;
                }

                case UI_PROGRESSBAR: {
                    UI_DrawRoundedRect(x, y, w, h, el->cornerRadius, el->bgColor);
                    float ratio = (el->value - el->minValue) / (el->maxValue - el->minValue);
                    if (ratio > 0) {
                        UI_DrawRoundedRect(x, y, w * ratio, h, el->cornerRadius, el->hoverColor);
                    }
                    if (el->borderWidth > 0) {
                        DrawRectangleLinesEx({x, y, w, h}, el->borderWidth, el->borderColor);
                    }
                    break;
                }

                case UI_IMAGE: {
                    auto texIt = g_texByHandle.find(el->textureHandle);
                    if (texIt != g_texByHandle.end() && texIt->second.valid) {
                        Rectangle src = el->sourceRect;
                        if (src.width <= 0) src = {0, 0, (float)texIt->second.tex.width, (float)texIt->second.tex.height};
                        Rectangle dest = {x, y, w, h};
                        DrawTexturePro(texIt->second.tex, src, dest, {0, 0}, 0, el->tint);
                    }
                    break;
                }
            }
        }
    }

    int Framework_UI_GetHovered() { return g_uiHoveredId; }
    int Framework_UI_GetFocused() { return g_uiFocusedId; }

    void Framework_UI_SetFocus(int elementId) {
        if (g_uiFocusedId >= 0) {
            auto it = g_uiElements.find(g_uiFocusedId);
            if (it != g_uiElements.end()) it->second.state = UI_STATE_NORMAL;
        }
        g_uiFocusedId = elementId;
        if (elementId >= 0) {
            auto it = g_uiElements.find(elementId);
            if (it != g_uiElements.end()) it->second.state = UI_STATE_FOCUSED;
        }
    }

    bool Framework_UI_HasFocus() { return g_uiFocusedId >= 0; }

    // Layout helpers
    void Framework_UI_LayoutVertical(int parentId, float spacing, float paddingX, float paddingY) {
        std::vector<UIElement*> children;
        for (auto& kv : g_uiElements) {
            if (kv.second.parent == parentId && kv.second.valid) {
                children.push_back(&kv.second);
            }
        }

        float currentY = paddingY;
        for (auto* child : children) {
            child->x = paddingX;
            child->y = currentY;
            currentY += child->height + spacing;
        }
    }

    void Framework_UI_LayoutHorizontal(int parentId, float spacing, float paddingX, float paddingY) {
        std::vector<UIElement*> children;
        for (auto& kv : g_uiElements) {
            if (kv.second.parent == parentId && kv.second.valid) {
                children.push_back(&kv.second);
            }
        }

        float currentX = paddingX;
        for (auto* child : children) {
            child->x = currentX;
            child->y = paddingY;
            currentX += child->width + spacing;
        }
    }

    // ========================================================================
    // CLEANUP
    // ========================================================================
    void Framework_ResourcesShutdown() {
        // Textures
        for (auto& kv : g_texByHandle) {
            if (kv.second.valid) UnloadTexture(kv.second.tex);
        }
        g_texByHandle.clear();
        g_handleByTexPath.clear();

        // Fonts
        for (auto& kv : g_fontByHandle) {
            if (kv.second.valid) UnloadFont(kv.second.font);
        }
        g_fontByHandle.clear();
        g_handleByFontKey.clear();

        // Music
        for (auto& kv : g_musByHandle) {
            if (kv.second.valid) {
                StopMusicStream(kv.second.mus);
                UnloadMusicStream(kv.second.mus);
            }
        }
        g_musByHandle.clear();
        g_handleByMusPath.clear();

        // Prefabs
        g_prefabs.clear();
    }

} // extern "C"
