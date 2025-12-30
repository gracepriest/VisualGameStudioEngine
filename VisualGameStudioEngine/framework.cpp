// framework.cpp
// Visual Game Studio Engine - Framework v1.0 / Engine v0.5
#include "pch.h"
#include "framework.h"

#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <queue>
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

    // Enhanced Camera State
    struct CameraState {
        // Smooth follow
        Vector2 followTarget = { 0, 0 };
        float followLerp = 0.1f;          // 0-1, speed of follow
        bool followEnabled = false;

        // Deadzone
        float deadzoneWidth = 0;
        float deadzoneHeight = 0;
        bool deadzoneEnabled = false;

        // Look-ahead
        float lookaheadDistance = 0;
        float lookaheadSmoothing = 0.1f;
        Vector2 lookaheadVelocity = { 0, 0 };
        Vector2 currentLookahead = { 0, 0 };
        bool lookaheadEnabled = false;

        // Screen shake
        float shakeIntensity = 0;
        float shakeDuration = 0;
        float shakeTimer = 0;
        float shakeFrequency = 60.0f;     // oscillations per second
        float shakeDecay = 1.0f;          // 0-1, how fast it decays
        float shakeTime = 0;              // for noise sampling
        Vector2 shakeOffset = { 0, 0 };

        // Bounds
        float boundsMinX = 0, boundsMinY = 0;
        float boundsMaxX = 0, boundsMaxY = 0;
        bool boundsEnabled = false;

        // Zoom limits and transitions
        float minZoom = 0.1f;
        float maxZoom = 10.0f;
        float zoomFrom = 1.0f;
        float zoomTo = 1.0f;
        float zoomDuration = 0;
        float zoomTimer = 0;
        Vector2 zoomPivot = { 0, 0 };     // for ZoomAt
        bool zoomAtPivot = false;

        // Rotation transition
        float rotationFrom = 0;
        float rotationTo = 0;
        float rotationDuration = 0;
        float rotationTimer = 0;

        // Pan transition
        Vector2 panFrom = { 0, 0 };
        Vector2 panTo = { 0, 0 };
        float panDuration = 0;
        float panTimer = 0;
        bool panning = false;

        // Flash effect
        unsigned char flashR = 255, flashG = 255, flashB = 255, flashA = 255;
        float flashDuration = 0;
        float flashTimer = 0;
    };
    CameraState g_camState;

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

    // Scene Manager State
    struct SceneManagerState {
        // Transition settings
        SceneTransitionType transitionType = TRANSITION_FADE;
        TransitionEasing transitionEasing = EASE_IN_OUT_QUAD;
        float transitionDuration = 0.5f;
        Color transitionColor = { 0, 0, 0, 255 };  // Black by default

        // Transition runtime state
        TransitionState transitionState = TRANS_STATE_NONE;
        float transitionTimer = 0.0f;
        int pendingScene = -1;           // Scene to change to after transition out
        bool pendingIsPush = false;      // Is this a push or change?
        bool pendingIsPop = false;       // Is this a pop?

        // Loading screen
        bool loadingEnabled = false;
        float loadingMinDuration = 0.5f;
        float loadingTimer = 0.0f;
        float loadingProgress = 0.0f;
        LoadingCallback loadingCallback = nullptr;
        LoadingDrawCallback loadingDrawCallback = nullptr;

        // Preloading
        bool isPreloading = false;
        int preloadScene = -1;

        // Render texture for transition effects
        RenderTexture2D transitionRenderTexture = { 0 };
        bool renderTextureValid = false;
    };

    SceneManagerState g_sceneManager;

    // Easing functions
    float ApplyEasing(float t, TransitionEasing easing) {
        switch (easing) {
            case EASE_LINEAR:
                return t;
            case EASE_IN_QUAD:
                return t * t;
            case EASE_OUT_QUAD:
                return t * (2.0f - t);
            case EASE_IN_OUT_QUAD:
                return t < 0.5f ? 2.0f * t * t : -1.0f + (4.0f - 2.0f * t) * t;
            case EASE_IN_CUBIC:
                return t * t * t;
            case EASE_OUT_CUBIC: {
                float f = t - 1.0f;
                return f * f * f + 1.0f;
            }
            case EASE_IN_OUT_CUBIC:
                return t < 0.5f ? 4.0f * t * t * t : (t - 1.0f) * (2.0f * t - 2.0f) * (2.0f * t - 2.0f) + 1.0f;
            case EASE_IN_EXPO:
                return t == 0.0f ? 0.0f : powf(2.0f, 10.0f * (t - 1.0f));
            case EASE_OUT_EXPO:
                return t == 1.0f ? 1.0f : 1.0f - powf(2.0f, -10.0f * t);
            case EASE_IN_OUT_EXPO:
                if (t == 0.0f) return 0.0f;
                if (t == 1.0f) return 1.0f;
                if (t < 0.5f) return powf(2.0f, 20.0f * t - 10.0f) / 2.0f;
                return (2.0f - powf(2.0f, -20.0f * t + 10.0f)) / 2.0f;
            default:
                return t;
        }
    }

    // Initialize render texture for transitions if needed
    void EnsureTransitionRenderTexture() {
        if (!g_sceneManager.renderTextureValid) {
            int w = GetScreenWidth();
            int h = GetScreenHeight();
            if (w > 0 && h > 0) {
                g_sceneManager.transitionRenderTexture = LoadRenderTexture(w, h);
                g_sceneManager.renderTextureValid = true;
            }
        }
    }

    // Perform the actual scene switch
    void PerformSceneSwitch() {
        if (g_sceneManager.pendingIsPop) {
            // Pop operation
            if (!g_sceneStack.empty()) {
                if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
                g_sceneStack.pop_back();
                if (auto sc = TopScene(); sc && sc->cb.onResume) sc->cb.onResume();
            }
        }
        else if (g_sceneManager.pendingIsPush) {
            // Push operation
            g_sceneStack.push_back(g_sceneManager.pendingScene);
            if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
        }
        else {
            // Change operation
            if (!g_sceneStack.empty()) {
                if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
                g_sceneStack.pop_back();
            }
            g_sceneStack.push_back(g_sceneManager.pendingScene);
            if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
        }

        g_sceneManager.pendingScene = -1;
        g_sceneManager.pendingIsPush = false;
        g_sceneManager.pendingIsPop = false;
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
    // CAMERA 2D (Enhanced)
    // ========================================================================

    // Easing function for smooth transitions
    static float EaseOutQuad(float t) {
        return t * (2.0f - t);
    }

    static float EaseInOutQuad(float t) {
        return t < 0.5f ? 2.0f * t * t : 1.0f - powf(-2.0f * t + 2.0f, 2.0f) / 2.0f;
    }

    // Simple noise function for shake
    static float ShakeNoise(float x) {
        // Simple pseudo-random based on sine
        return sinf(x * 12.9898f) * cosf(x * 78.233f);
    }

    // Smooth follow
    void Framework_Camera_SetFollowTarget(float x, float y) {
        g_camState.followTarget = Vector2{ x, y };
    }

    void Framework_Camera_SetFollowLerp(float lerpSpeed) {
        g_camState.followLerp = lerpSpeed < 0.0f ? 0.0f : (lerpSpeed > 1.0f ? 1.0f : lerpSpeed);
    }

    float Framework_Camera_GetFollowLerp() {
        return g_camState.followLerp;
    }

    void Framework_Camera_SetFollowEnabled(bool enabled) {
        g_camState.followEnabled = enabled;
    }

    bool Framework_Camera_IsFollowEnabled() {
        return g_camState.followEnabled;
    }

    // Deadzone
    void Framework_Camera_SetDeadzone(float width, float height) {
        g_camState.deadzoneWidth = width < 0 ? 0 : width;
        g_camState.deadzoneHeight = height < 0 ? 0 : height;
    }

    void Framework_Camera_GetDeadzone(float* width, float* height) {
        if (width) *width = g_camState.deadzoneWidth;
        if (height) *height = g_camState.deadzoneHeight;
    }

    void Framework_Camera_SetDeadzoneEnabled(bool enabled) {
        g_camState.deadzoneEnabled = enabled;
    }

    bool Framework_Camera_IsDeadzoneEnabled() {
        return g_camState.deadzoneEnabled;
    }

    // Look-ahead
    void Framework_Camera_SetLookahead(float distance, float smoothing) {
        g_camState.lookaheadDistance = distance;
        g_camState.lookaheadSmoothing = smoothing < 0.0f ? 0.0f : (smoothing > 1.0f ? 1.0f : smoothing);
    }

    void Framework_Camera_SetLookaheadEnabled(bool enabled) {
        g_camState.lookaheadEnabled = enabled;
        if (!enabled) {
            g_camState.currentLookahead = Vector2{ 0, 0 };
        }
    }

    void Framework_Camera_SetLookaheadVelocity(float vx, float vy) {
        g_camState.lookaheadVelocity = Vector2{ vx, vy };
    }

    // Screen shake
    void Framework_Camera_Shake(float intensity, float duration) {
        g_camState.shakeIntensity = intensity;
        g_camState.shakeDuration = duration;
        g_camState.shakeTimer = duration;
        g_camState.shakeFrequency = 60.0f;
        g_camState.shakeDecay = 1.0f;
    }

    void Framework_Camera_ShakeEx(float intensity, float duration, float frequency, float decay) {
        g_camState.shakeIntensity = intensity;
        g_camState.shakeDuration = duration;
        g_camState.shakeTimer = duration;
        g_camState.shakeFrequency = frequency > 0 ? frequency : 60.0f;
        g_camState.shakeDecay = decay < 0 ? 0 : (decay > 1 ? 1 : decay);
    }

    void Framework_Camera_StopShake() {
        g_camState.shakeTimer = 0;
        g_camState.shakeOffset = Vector2{ 0, 0 };
    }

    bool Framework_Camera_IsShaking() {
        return g_camState.shakeTimer > 0;
    }

    float Framework_Camera_GetShakeIntensity() {
        if (g_camState.shakeTimer <= 0) return 0;
        float progress = 1.0f - (g_camState.shakeTimer / g_camState.shakeDuration);
        float decay = 1.0f - (progress * g_camState.shakeDecay);
        return g_camState.shakeIntensity * decay;
    }

    // Bounds
    void Framework_Camera_SetBounds(float minX, float minY, float maxX, float maxY) {
        g_camState.boundsMinX = minX;
        g_camState.boundsMinY = minY;
        g_camState.boundsMaxX = maxX;
        g_camState.boundsMaxY = maxY;
    }

    void Framework_Camera_GetBounds(float* minX, float* minY, float* maxX, float* maxY) {
        if (minX) *minX = g_camState.boundsMinX;
        if (minY) *minY = g_camState.boundsMinY;
        if (maxX) *maxX = g_camState.boundsMaxX;
        if (maxY) *maxY = g_camState.boundsMaxY;
    }

    void Framework_Camera_SetBoundsEnabled(bool enabled) {
        g_camState.boundsEnabled = enabled;
    }

    bool Framework_Camera_IsBoundsEnabled() {
        return g_camState.boundsEnabled;
    }

    void Framework_Camera_ClearBounds() {
        g_camState.boundsEnabled = false;
        g_camState.boundsMinX = g_camState.boundsMinY = 0;
        g_camState.boundsMaxX = g_camState.boundsMaxY = 0;
    }

    // Zoom controls
    void Framework_Camera_SetZoomLimits(float minZoom, float maxZoom) {
        g_camState.minZoom = minZoom > 0.01f ? minZoom : 0.01f;
        g_camState.maxZoom = maxZoom > g_camState.minZoom ? maxZoom : g_camState.minZoom;
    }

    void Framework_Camera_ZoomTo(float targetZoom, float duration) {
        targetZoom = targetZoom < g_camState.minZoom ? g_camState.minZoom :
                    (targetZoom > g_camState.maxZoom ? g_camState.maxZoom : targetZoom);

        if (duration <= 0) {
            g_camera.zoom = targetZoom;
            g_camState.zoomTimer = 0;
        } else {
            g_camState.zoomFrom = g_camera.zoom;
            g_camState.zoomTo = targetZoom;
            g_camState.zoomDuration = duration;
            g_camState.zoomTimer = duration;
            g_camState.zoomAtPivot = false;
        }
    }

    void Framework_Camera_ZoomAt(float targetZoom, float worldX, float worldY, float duration) {
        targetZoom = targetZoom < g_camState.minZoom ? g_camState.minZoom :
                    (targetZoom > g_camState.maxZoom ? g_camState.maxZoom : targetZoom);

        g_camState.zoomFrom = g_camera.zoom;
        g_camState.zoomTo = targetZoom;
        g_camState.zoomDuration = duration > 0 ? duration : 0.001f;
        g_camState.zoomTimer = g_camState.zoomDuration;
        g_camState.zoomPivot = Vector2{ worldX, worldY };
        g_camState.zoomAtPivot = true;
    }

    // Rotation
    void Framework_Camera_RotateTo(float targetRotation, float duration) {
        if (duration <= 0) {
            g_camera.rotation = targetRotation;
            g_camState.rotationTimer = 0;
        } else {
            g_camState.rotationFrom = g_camera.rotation;
            g_camState.rotationTo = targetRotation;
            g_camState.rotationDuration = duration;
            g_camState.rotationTimer = duration;
        }
    }

    // Pan
    void Framework_Camera_PanTo(float worldX, float worldY, float duration) {
        if (duration <= 0) {
            g_camera.target = Vector2{ worldX, worldY };
            g_camState.panning = false;
            g_camState.panTimer = 0;
        } else {
            g_camState.panFrom = g_camera.target;
            g_camState.panTo = Vector2{ worldX, worldY };
            g_camState.panDuration = duration;
            g_camState.panTimer = duration;
            g_camState.panning = true;
        }
    }

    void Framework_Camera_PanBy(float deltaX, float deltaY, float duration) {
        float newX = g_camera.target.x + deltaX;
        float newY = g_camera.target.y + deltaY;
        Framework_Camera_PanTo(newX, newY, duration);
    }

    bool Framework_Camera_IsPanning() {
        return g_camState.panning && g_camState.panTimer > 0;
    }

    void Framework_Camera_StopPan() {
        g_camState.panning = false;
        g_camState.panTimer = 0;
    }

    // Flash effect
    void Framework_Camera_Flash(unsigned char r, unsigned char g, unsigned char b, unsigned char a, float duration) {
        g_camState.flashR = r;
        g_camState.flashG = g;
        g_camState.flashB = b;
        g_camState.flashA = a;
        g_camState.flashDuration = duration;
        g_camState.flashTimer = duration;
    }

    bool Framework_Camera_IsFlashing() {
        return g_camState.flashTimer > 0;
    }

    void Framework_Camera_DrawFlash() {
        if (g_camState.flashTimer <= 0) return;

        float alpha = g_camState.flashTimer / g_camState.flashDuration;
        unsigned char a = (unsigned char)(g_camState.flashA * alpha);

        DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(),
            Color{ g_camState.flashR, g_camState.flashG, g_camState.flashB, a });
    }

    // Camera update - call each frame
    void Framework_Camera_Update(float dt) {
        Vector2 targetPos = g_camera.target;

        // Handle entity follow (legacy support)
        if (g_cameraFollowEntity != -1 && EcsIsAlive(g_cameraFollowEntity)) {
            Vector2 entityPos = GetWorldPositionInternal(g_cameraFollowEntity);
            g_camState.followTarget = entityPos;
            g_camState.followEnabled = true;
        }

        // Smooth follow with optional deadzone
        if (g_camState.followEnabled) {
            Vector2 diff = {
                g_camState.followTarget.x - targetPos.x,
                g_camState.followTarget.y - targetPos.y
            };

            // Apply deadzone if enabled
            if (g_camState.deadzoneEnabled) {
                float halfW = g_camState.deadzoneWidth / 2.0f;
                float halfH = g_camState.deadzoneHeight / 2.0f;

                if (fabsf(diff.x) < halfW) diff.x = 0;
                else diff.x -= (diff.x > 0 ? halfW : -halfW);

                if (fabsf(diff.y) < halfH) diff.y = 0;
                else diff.y -= (diff.y > 0 ? halfH : -halfH);
            }

            // Apply look-ahead if enabled
            if (g_camState.lookaheadEnabled && g_camState.lookaheadDistance > 0) {
                float velLen = sqrtf(g_camState.lookaheadVelocity.x * g_camState.lookaheadVelocity.x +
                                    g_camState.lookaheadVelocity.y * g_camState.lookaheadVelocity.y);
                if (velLen > 0.1f) {
                    Vector2 targetLookahead = {
                        (g_camState.lookaheadVelocity.x / velLen) * g_camState.lookaheadDistance,
                        (g_camState.lookaheadVelocity.y / velLen) * g_camState.lookaheadDistance
                    };
                    // Smooth the lookahead
                    g_camState.currentLookahead.x += (targetLookahead.x - g_camState.currentLookahead.x) * g_camState.lookaheadSmoothing;
                    g_camState.currentLookahead.y += (targetLookahead.y - g_camState.currentLookahead.y) * g_camState.lookaheadSmoothing;
                } else {
                    // Decay lookahead when stopped
                    g_camState.currentLookahead.x *= 0.95f;
                    g_camState.currentLookahead.y *= 0.95f;
                }
                diff.x += g_camState.currentLookahead.x;
                diff.y += g_camState.currentLookahead.y;
            }

            // Lerp towards target
            targetPos.x += diff.x * g_camState.followLerp;
            targetPos.y += diff.y * g_camState.followLerp;
        }

        // Pan transition (overrides follow while active)
        if (g_camState.panning && g_camState.panTimer > 0) {
            g_camState.panTimer -= dt;
            if (g_camState.panTimer <= 0) {
                targetPos = g_camState.panTo;
                g_camState.panning = false;
            } else {
                float t = 1.0f - (g_camState.panTimer / g_camState.panDuration);
                t = EaseInOutQuad(t);
                targetPos.x = g_camState.panFrom.x + (g_camState.panTo.x - g_camState.panFrom.x) * t;
                targetPos.y = g_camState.panFrom.y + (g_camState.panTo.y - g_camState.panFrom.y) * t;
            }
        }

        // Zoom transition
        if (g_camState.zoomTimer > 0) {
            g_camState.zoomTimer -= dt;
            float t = 1.0f - (g_camState.zoomTimer / g_camState.zoomDuration);
            t = EaseOutQuad(t);

            float newZoom = g_camState.zoomFrom + (g_camState.zoomTo - g_camState.zoomFrom) * t;

            // If zooming at a pivot point, adjust target to keep pivot stable
            if (g_camState.zoomAtPivot && g_camState.zoomTimer > 0) {
                Vector2 screenPivot = GetWorldToScreen2D(g_camState.zoomPivot, g_camera);
                g_camera.zoom = newZoom;
                Vector2 newWorldPivot = GetScreenToWorld2D(screenPivot, g_camera);
                targetPos.x += g_camState.zoomPivot.x - newWorldPivot.x;
                targetPos.y += g_camState.zoomPivot.y - newWorldPivot.y;
            } else {
                g_camera.zoom = newZoom;
            }

            if (g_camState.zoomTimer <= 0) {
                g_camera.zoom = g_camState.zoomTo;
            }
        }

        // Rotation transition
        if (g_camState.rotationTimer > 0) {
            g_camState.rotationTimer -= dt;
            float t = 1.0f - (g_camState.rotationTimer / g_camState.rotationDuration);
            t = EaseInOutQuad(t);
            g_camera.rotation = g_camState.rotationFrom + (g_camState.rotationTo - g_camState.rotationFrom) * t;

            if (g_camState.rotationTimer <= 0) {
                g_camera.rotation = g_camState.rotationTo;
            }
        }

        // Apply bounds constraints
        if (g_camState.boundsEnabled) {
            // Calculate visible area in world coords
            float viewW = (float)GetScreenWidth() / g_camera.zoom;
            float viewH = (float)GetScreenHeight() / g_camera.zoom;
            float halfW = viewW / 2.0f;
            float halfH = viewH / 2.0f;

            // Clamp target to keep view within bounds
            float boundsW = g_camState.boundsMaxX - g_camState.boundsMinX;
            float boundsH = g_camState.boundsMaxY - g_camState.boundsMinY;

            if (viewW < boundsW) {
                if (targetPos.x - halfW < g_camState.boundsMinX)
                    targetPos.x = g_camState.boundsMinX + halfW;
                if (targetPos.x + halfW > g_camState.boundsMaxX)
                    targetPos.x = g_camState.boundsMaxX - halfW;
            } else {
                targetPos.x = (g_camState.boundsMinX + g_camState.boundsMaxX) / 2.0f;
            }

            if (viewH < boundsH) {
                if (targetPos.y - halfH < g_camState.boundsMinY)
                    targetPos.y = g_camState.boundsMinY + halfH;
                if (targetPos.y + halfH > g_camState.boundsMaxY)
                    targetPos.y = g_camState.boundsMaxY - halfH;
            } else {
                targetPos.y = (g_camState.boundsMinY + g_camState.boundsMaxY) / 2.0f;
            }
        }

        // Screen shake
        g_camState.shakeOffset = Vector2{ 0, 0 };
        if (g_camState.shakeTimer > 0) {
            g_camState.shakeTimer -= dt;
            g_camState.shakeTime += dt;

            if (g_camState.shakeTimer > 0) {
                float progress = 1.0f - (g_camState.shakeTimer / g_camState.shakeDuration);
                float decay = 1.0f - (progress * g_camState.shakeDecay);
                float currentIntensity = g_camState.shakeIntensity * decay;

                // Use noise-based shake for more natural feel
                float t = g_camState.shakeTime * g_camState.shakeFrequency;
                g_camState.shakeOffset.x = ShakeNoise(t) * currentIntensity;
                g_camState.shakeOffset.y = ShakeNoise(t + 100.0f) * currentIntensity;
            }
        }

        // Apply final position with shake offset
        g_camera.target.x = targetPos.x + g_camState.shakeOffset.x;
        g_camera.target.y = targetPos.y + g_camState.shakeOffset.y;

        // Update flash
        if (g_camState.flashTimer > 0) {
            g_camState.flashTimer -= dt;
        }
    }

    // Reset camera to defaults
    void Framework_Camera_Reset() {
        g_camera.target = Vector2{ 0, 0 };
        g_camera.offset = Vector2{ (float)GetScreenWidth() / 2.0f, (float)GetScreenHeight() / 2.0f };
        g_camera.rotation = 0;
        g_camera.zoom = 1.0f;
        g_cameraFollowEntity = -1;
        g_camState = CameraState{};
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
    // SCENE MANAGER - Transitions & Loading Screens
    // ========================================================================
    void Framework_Scene_SetTransition(int transitionType, float duration) {
        g_sceneManager.transitionType = (SceneTransitionType)transitionType;
        g_sceneManager.transitionDuration = duration;
    }

    void Framework_Scene_SetTransitionEx(int transitionType, float duration, int easing) {
        g_sceneManager.transitionType = (SceneTransitionType)transitionType;
        g_sceneManager.transitionDuration = duration;
        g_sceneManager.transitionEasing = (TransitionEasing)easing;
    }

    void Framework_Scene_SetTransitionColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        g_sceneManager.transitionColor = { r, g, b, a };
    }

    int Framework_Scene_GetTransitionType() {
        return (int)g_sceneManager.transitionType;
    }

    float Framework_Scene_GetTransitionDuration() {
        return g_sceneManager.transitionDuration;
    }

    int Framework_Scene_GetTransitionEasing() {
        return (int)g_sceneManager.transitionEasing;
    }

    void Framework_Scene_ChangeWithTransition(int sceneHandle) {
        if (g_sceneManager.transitionState != TRANS_STATE_NONE) return;  // Already transitioning

        g_sceneManager.pendingScene = sceneHandle;
        g_sceneManager.pendingIsPush = false;
        g_sceneManager.pendingIsPop = false;

        if (g_sceneManager.transitionType == TRANSITION_NONE || g_sceneManager.transitionDuration <= 0.0f) {
            // No transition, switch immediately
            PerformSceneSwitch();
        }
        else {
            // Start transition out
            g_sceneManager.transitionState = TRANS_STATE_OUT;
            g_sceneManager.transitionTimer = 0.0f;
            EnsureTransitionRenderTexture();
        }
    }

    void Framework_Scene_ChangeWithTransitionEx(int sceneHandle, int transitionType, float duration) {
        g_sceneManager.transitionType = (SceneTransitionType)transitionType;
        g_sceneManager.transitionDuration = duration;
        Framework_Scene_ChangeWithTransition(sceneHandle);
    }

    void Framework_Scene_PushWithTransition(int sceneHandle) {
        if (g_sceneManager.transitionState != TRANS_STATE_NONE) return;

        g_sceneManager.pendingScene = sceneHandle;
        g_sceneManager.pendingIsPush = true;
        g_sceneManager.pendingIsPop = false;

        if (g_sceneManager.transitionType == TRANSITION_NONE || g_sceneManager.transitionDuration <= 0.0f) {
            PerformSceneSwitch();
        }
        else {
            g_sceneManager.transitionState = TRANS_STATE_OUT;
            g_sceneManager.transitionTimer = 0.0f;
            EnsureTransitionRenderTexture();
        }
    }

    void Framework_Scene_PopWithTransition() {
        if (g_sceneManager.transitionState != TRANS_STATE_NONE) return;
        if (g_sceneStack.empty()) return;

        g_sceneManager.pendingScene = -1;
        g_sceneManager.pendingIsPush = false;
        g_sceneManager.pendingIsPop = true;

        if (g_sceneManager.transitionType == TRANSITION_NONE || g_sceneManager.transitionDuration <= 0.0f) {
            PerformSceneSwitch();
        }
        else {
            g_sceneManager.transitionState = TRANS_STATE_OUT;
            g_sceneManager.transitionTimer = 0.0f;
            EnsureTransitionRenderTexture();
        }
    }

    bool Framework_Scene_IsTransitioning() {
        return g_sceneManager.transitionState != TRANS_STATE_NONE;
    }

    int Framework_Scene_GetTransitionState() {
        return (int)g_sceneManager.transitionState;
    }

    float Framework_Scene_GetTransitionProgress() {
        if (g_sceneManager.transitionDuration <= 0.0f) return 1.0f;
        float rawProgress = g_sceneManager.transitionTimer / g_sceneManager.transitionDuration;
        return ApplyEasing(rawProgress < 0.0f ? 0.0f : (rawProgress > 1.0f ? 1.0f : rawProgress), g_sceneManager.transitionEasing);
    }

    void Framework_Scene_SkipTransition() {
        if (g_sceneManager.transitionState == TRANS_STATE_NONE) return;

        // Skip to the end
        if (g_sceneManager.transitionState == TRANS_STATE_OUT || g_sceneManager.transitionState == TRANS_STATE_LOADING) {
            PerformSceneSwitch();
        }
        g_sceneManager.transitionState = TRANS_STATE_NONE;
        g_sceneManager.transitionTimer = 0.0f;
        g_sceneManager.loadingTimer = 0.0f;
        g_sceneManager.loadingProgress = 0.0f;
    }

    void Framework_Scene_SetLoadingEnabled(bool enabled) {
        g_sceneManager.loadingEnabled = enabled;
    }

    bool Framework_Scene_IsLoadingEnabled() {
        return g_sceneManager.loadingEnabled;
    }

    void Framework_Scene_SetLoadingMinDuration(float seconds) {
        g_sceneManager.loadingMinDuration = seconds;
    }

    float Framework_Scene_GetLoadingMinDuration() {
        return g_sceneManager.loadingMinDuration;
    }

    void Framework_Scene_SetLoadingCallback(LoadingCallback callback) {
        g_sceneManager.loadingCallback = callback;
    }

    void Framework_Scene_SetLoadingDrawCallback(LoadingDrawCallback callback) {
        g_sceneManager.loadingDrawCallback = callback;
    }

    void Framework_Scene_SetLoadingProgress(float progress) {
        g_sceneManager.loadingProgress = progress < 0.0f ? 0.0f : (progress > 1.0f ? 1.0f : progress);
    }

    float Framework_Scene_GetLoadingProgress() {
        return g_sceneManager.loadingProgress;
    }

    bool Framework_Scene_IsLoading() {
        return g_sceneManager.transitionState == TRANS_STATE_LOADING;
    }

    int Framework_Scene_GetStackSize() {
        return (int)g_sceneStack.size();
    }

    int Framework_Scene_GetSceneAt(int index) {
        if (index < 0 || index >= (int)g_sceneStack.size()) return -1;
        return g_sceneStack[index];
    }

    int Framework_Scene_GetPreviousScene() {
        if (g_sceneStack.size() < 2) return -1;
        return g_sceneStack[g_sceneStack.size() - 2];
    }

    void Framework_Scene_Update(float dt) {
        switch (g_sceneManager.transitionState) {
            case TRANS_STATE_NONE:
                // Normal scene tick
                Framework_SceneTick();
                break;

            case TRANS_STATE_OUT:
                // Transitioning out of current scene
                g_sceneManager.transitionTimer += dt;
                if (g_sceneManager.transitionTimer >= g_sceneManager.transitionDuration) {
                    // Transition out complete
                    if (g_sceneManager.loadingEnabled) {
                        // Go to loading state
                        g_sceneManager.transitionState = TRANS_STATE_LOADING;
                        g_sceneManager.loadingTimer = 0.0f;
                        g_sceneManager.loadingProgress = 0.0f;
                    }
                    else {
                        // Skip loading, do scene switch
                        PerformSceneSwitch();
                        g_sceneManager.transitionState = TRANS_STATE_IN;
                        g_sceneManager.transitionTimer = 0.0f;
                    }
                }
                break;

            case TRANS_STATE_LOADING:
                // Loading screen active
                g_sceneManager.loadingTimer += dt;
                if (g_sceneManager.loadingCallback) {
                    g_sceneManager.loadingCallback(g_sceneManager.loadingProgress);
                }
                // Check if loading is complete
                if (g_sceneManager.loadingProgress >= 1.0f &&
                    g_sceneManager.loadingTimer >= g_sceneManager.loadingMinDuration) {
                    // Perform scene switch and start transition in
                    PerformSceneSwitch();
                    g_sceneManager.transitionState = TRANS_STATE_IN;
                    g_sceneManager.transitionTimer = 0.0f;
                }
                break;

            case TRANS_STATE_IN:
                // Transitioning into new scene
                g_sceneManager.transitionTimer += dt;
                if (g_sceneManager.transitionTimer >= g_sceneManager.transitionDuration) {
                    // Transition complete
                    g_sceneManager.transitionState = TRANS_STATE_NONE;
                    g_sceneManager.transitionTimer = 0.0f;
                }
                // Update the new scene even during transition in
                Framework_SceneTick();
                break;
        }
    }

    void Framework_Scene_Draw() {
        if (g_sceneManager.transitionState == TRANS_STATE_NONE) return;

        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();
        float progress = Framework_Scene_GetTransitionProgress();

        // For transition OUT, progress goes 0->1 (fade in the effect)
        // For transition IN, progress goes 0->1 (fade out the effect)
        float effectAlpha = 0.0f;
        if (g_sceneManager.transitionState == TRANS_STATE_OUT) {
            effectAlpha = progress;  // 0 -> 1
        }
        else if (g_sceneManager.transitionState == TRANS_STATE_IN) {
            effectAlpha = 1.0f - progress;  // 1 -> 0
        }
        else if (g_sceneManager.transitionState == TRANS_STATE_LOADING) {
            effectAlpha = 1.0f;  // Fully covered during loading
        }

        Color col = g_sceneManager.transitionColor;

        switch (g_sceneManager.transitionType) {
            case TRANSITION_NONE:
                break;

            case TRANSITION_FADE:
            case TRANSITION_FADE_WHITE:
                if (g_sceneManager.transitionType == TRANSITION_FADE_WHITE) {
                    col = { 255, 255, 255, 255 };
                }
                col.a = (unsigned char)(effectAlpha * 255.0f);
                DrawRectangle(0, 0, screenWidth, screenHeight, col);
                break;

            case TRANSITION_SLIDE_LEFT:
                DrawRectangle((int)((1.0f - effectAlpha) * screenWidth), 0, screenWidth, screenHeight, col);
                break;

            case TRANSITION_SLIDE_RIGHT:
                DrawRectangle((int)(-screenWidth + effectAlpha * screenWidth), 0, screenWidth, screenHeight, col);
                break;

            case TRANSITION_SLIDE_UP:
                DrawRectangle(0, (int)((1.0f - effectAlpha) * screenHeight), screenWidth, screenHeight, col);
                break;

            case TRANSITION_SLIDE_DOWN:
                DrawRectangle(0, (int)(-screenHeight + effectAlpha * screenHeight), screenWidth, screenHeight, col);
                break;

            case TRANSITION_WIPE_LEFT:
                DrawRectangle(0, 0, (int)(effectAlpha * screenWidth), screenHeight, col);
                break;

            case TRANSITION_WIPE_RIGHT:
                DrawRectangle((int)((1.0f - effectAlpha) * screenWidth), 0, (int)(effectAlpha * screenWidth), screenHeight, col);
                break;

            case TRANSITION_WIPE_UP:
                DrawRectangle(0, 0, screenWidth, (int)(effectAlpha * screenHeight), col);
                break;

            case TRANSITION_WIPE_DOWN:
                DrawRectangle(0, (int)((1.0f - effectAlpha) * screenHeight), screenWidth, (int)(effectAlpha * screenHeight), col);
                break;

            case TRANSITION_CIRCLE_IN: {
                // Circular iris closing
                float maxRadius = sqrtf((float)(screenWidth * screenWidth + screenHeight * screenHeight)) / 2.0f;
                float radius = maxRadius * (1.0f - effectAlpha);
                // Draw four rects around a circle (simple approximation)
                // For proper circle mask, would need shaders
                DrawRectangle(0, 0, screenWidth, screenHeight, col);
                if (radius > 0) {
                    DrawCircle(screenWidth / 2, screenHeight / 2, radius, { 0, 0, 0, 0 });  // Won't work as expected without shaders
                    // Simple workaround: just fade
                    col.a = (unsigned char)(effectAlpha * 255.0f);
                    DrawRectangle(0, 0, screenWidth, screenHeight, col);
                }
                break;
            }

            case TRANSITION_CIRCLE_OUT: {
                // Circular iris opening - inverse of circle in
                float maxRadius = sqrtf((float)(screenWidth * screenWidth + screenHeight * screenHeight)) / 2.0f;
                float radius = maxRadius * effectAlpha;
                col.a = (unsigned char)(effectAlpha * 255.0f);
                DrawRectangle(0, 0, screenWidth, screenHeight, col);
                break;
            }

            case TRANSITION_PIXELATE:
            case TRANSITION_DISSOLVE:
                // These would require shaders for proper implementation
                // Fall back to fade
                col.a = (unsigned char)(effectAlpha * 255.0f);
                DrawRectangle(0, 0, screenWidth, screenHeight, col);
                break;
        }

        // Draw loading screen if in loading state
        if (g_sceneManager.transitionState == TRANS_STATE_LOADING) {
            if (g_sceneManager.loadingDrawCallback) {
                g_sceneManager.loadingDrawCallback();
            }
            else {
                // Default loading screen
                int barWidth = 400;
                int barHeight = 20;
                int barX = (screenWidth - barWidth) / 2;
                int barY = (screenHeight - barHeight) / 2 + 50;

                // Background bar
                DrawRectangle(barX, barY, barWidth, barHeight, DARKGRAY);
                // Progress bar
                DrawRectangle(barX, barY, (int)(barWidth * g_sceneManager.loadingProgress), barHeight, WHITE);
                // Border
                DrawRectangleLines(barX, barY, barWidth, barHeight, WHITE);

                // Loading text
                const char* loadingText = "Loading...";
                int textWidth = MeasureText(loadingText, 30);
                DrawText(loadingText, (screenWidth - textWidth) / 2, barY - 50, 30, WHITE);
            }
        }
    }

    void Framework_Scene_PreloadStart(int sceneHandle) {
        g_sceneManager.isPreloading = true;
        g_sceneManager.preloadScene = sceneHandle;
        g_sceneManager.loadingProgress = 0.0f;
    }

    bool Framework_Scene_IsPreloading() {
        return g_sceneManager.isPreloading;
    }

    void Framework_Scene_PreloadCancel() {
        g_sceneManager.isPreloading = false;
        g_sceneManager.preloadScene = -1;
        g_sceneManager.loadingProgress = 0.0f;
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
    // PROFILING & PERFORMANCE SYSTEM
    // ========================================================================

    struct PerfScope {
        std::string name;
        double startTime = 0;
        double lastTime = 0;
        double totalTime = 0;
        int callCount = 0;
    };

    struct ConsoleLine {
        std::string text;
        Color color;
    };

    struct DebugShape {
        enum Type { LINE, RECT, RECT_FILLED, CIRCLE, CIRCLE_FILLED, POINT, ARROW, TEXT, GRID, CROSS };
        Type type;
        float x1, y1, x2, y2;
        float size;
        Color color;
        std::string text;
    };

    // Performance state
    static std::vector<float> g_frameTimeHistory;
    static int g_perfSampleCount = 60;
    static int g_totalFrameCount = 0;
    static float g_currentFrameTime = 0;
    static double g_frameStartTime = 0;
    static int g_drawCallCount = 0;
    static int g_triangleCount = 0;

    // Profiling scopes
    static std::unordered_map<std::string, PerfScope> g_perfScopes;
    static std::vector<std::string> g_scopeStack;

    // Performance graph
    static bool g_perfGraphEnabled = false;
    static float g_perfGraphX = 10;
    static float g_perfGraphY = 100;
    static float g_perfGraphWidth = 200;
    static float g_perfGraphHeight = 60;

    // Logging
    static int g_logMinLevel = LOG_LEVEL_INFO;
    static std::ofstream g_logFile;
    static bool g_logFileOpen = false;

    // Console
    static bool g_consoleEnabled = false;
    static float g_consoleX = 10;
    static float g_consoleY = 200;
    static float g_consoleWidth = 400;
    static float g_consoleHeight = 200;
    static int g_consoleMaxLines = 50;
    static std::vector<ConsoleLine> g_consoleLines;

    // Debug drawing
    static bool g_debugDrawEnabled = false;
    static bool g_debugDrawPersistent = false;
    static std::vector<DebugShape> g_debugShapes;

    // Debug overlay flags
    static bool g_showFPS = true;
    static bool g_showFrameTime = false;
    static bool g_showDrawCalls = false;
    static bool g_showEntityCount = true;
    static bool g_showMemory = false;
    static bool g_showPhysics = false;
    static bool g_showColliders = false;
    static float g_overlayX = 10;
    static float g_overlayY = 10;
    static Color g_overlayColor = { 255, 255, 255, 255 };

    // Frame timing
    float Framework_Perf_GetFPS() {
        return (float)GetFPS();
    }

    float Framework_Perf_GetFrameTime() {
        return g_currentFrameTime;
    }

    float Framework_Perf_GetFrameTimeAvg() {
        if (g_frameTimeHistory.empty()) return 0;
        float sum = 0;
        for (float t : g_frameTimeHistory) sum += t;
        return sum / g_frameTimeHistory.size();
    }

    float Framework_Perf_GetFrameTimeMin() {
        if (g_frameTimeHistory.empty()) return 0;
        float minVal = g_frameTimeHistory[0];
        for (float t : g_frameTimeHistory) if (t < minVal) minVal = t;
        return minVal;
    }

    float Framework_Perf_GetFrameTimeMax() {
        if (g_frameTimeHistory.empty()) return 0;
        float maxVal = g_frameTimeHistory[0];
        for (float t : g_frameTimeHistory) if (t > maxVal) maxVal = t;
        return maxVal;
    }

    void Framework_Perf_SetSampleCount(int count) {
        if (count > 0) {
            g_perfSampleCount = count;
            while ((int)g_frameTimeHistory.size() > count) {
                g_frameTimeHistory.erase(g_frameTimeHistory.begin());
            }
        }
    }

    int Framework_Perf_GetFrameCount() {
        return g_totalFrameCount;
    }

    // Draw call tracking
    int Framework_Perf_GetDrawCalls() {
        return g_drawCallCount;
    }

    int Framework_Perf_GetTriangleCount() {
        return g_triangleCount;
    }

    void Framework_Perf_ResetDrawStats() {
        g_drawCallCount = 0;
        g_triangleCount = 0;
    }

    // Memory tracking
    int Framework_Perf_GetEntityCount() {
        return (int)g_entities.size();
    }

    int Framework_Perf_GetTextureCount() {
        return (int)g_texByHandle.size();
    }

    int Framework_Perf_GetSoundCount() {
        return (int)g_sounds.size();
    }

    int Framework_Perf_GetFontCount() {
        return (int)g_fontByHandle.size();
    }

    long long Framework_Perf_GetTextureMemory() {
        long long total = 0;
        for (auto& kv : g_texByHandle) {
            if (kv.second.valid) {
                total += kv.second.tex.width * kv.second.tex.height * 4;  // Assume RGBA
            }
        }
        return total;
    }

    // Profiling scopes
    void Framework_Perf_BeginScope(const char* name) {
        if (!name) return;
        g_scopeStack.push_back(name);
        g_perfScopes[name].name = name;
        g_perfScopes[name].startTime = GetTime();
    }

    void Framework_Perf_EndScope() {
        if (g_scopeStack.empty()) return;
        std::string name = g_scopeStack.back();
        g_scopeStack.pop_back();

        auto it = g_perfScopes.find(name);
        if (it != g_perfScopes.end()) {
            double elapsed = (GetTime() - it->second.startTime) * 1000.0;  // Convert to ms
            it->second.lastTime = elapsed;
            it->second.totalTime += elapsed;
            it->second.callCount++;
        }
    }

    float Framework_Perf_GetScopeTime(const char* name) {
        if (!name) return 0;
        auto it = g_perfScopes.find(name);
        return (it != g_perfScopes.end()) ? (float)it->second.lastTime : 0;
    }

    float Framework_Perf_GetScopeTimeAvg(const char* name) {
        if (!name) return 0;
        auto it = g_perfScopes.find(name);
        if (it == g_perfScopes.end() || it->second.callCount == 0) return 0;
        return (float)(it->second.totalTime / it->second.callCount);
    }

    int Framework_Perf_GetScopeCallCount(const char* name) {
        if (!name) return 0;
        auto it = g_perfScopes.find(name);
        return (it != g_perfScopes.end()) ? it->second.callCount : 0;
    }

    void Framework_Perf_ResetScopes() {
        g_perfScopes.clear();
        g_scopeStack.clear();
    }

    // Performance graphs
    void Framework_Perf_SetGraphEnabled(bool enabled) {
        g_perfGraphEnabled = enabled;
    }

    void Framework_Perf_SetGraphPosition(float x, float y) {
        g_perfGraphX = x;
        g_perfGraphY = y;
    }

    void Framework_Perf_SetGraphSize(float width, float height) {
        g_perfGraphWidth = width;
        g_perfGraphHeight = height;
    }

    void Framework_Perf_DrawGraph() {
        if (!g_perfGraphEnabled || g_frameTimeHistory.empty()) return;

        // Background
        DrawRectangle((int)g_perfGraphX, (int)g_perfGraphY, (int)g_perfGraphWidth, (int)g_perfGraphHeight, Color{ 0, 0, 0, 180 });
        DrawRectangleLinesEx(Rectangle{ g_perfGraphX, g_perfGraphY, g_perfGraphWidth, g_perfGraphHeight }, 1, Color{ 100, 100, 100, 255 });

        // Find max frame time for scaling
        float maxTime = 16.67f;  // Minimum scale of 60 FPS target
        for (float t : g_frameTimeHistory) {
            if (t > maxTime) maxTime = t;
        }

        // Draw frame time bars
        int count = (int)g_frameTimeHistory.size();
        float barWidth = g_perfGraphWidth / g_perfSampleCount;

        for (int i = 0; i < count; i++) {
            float t = g_frameTimeHistory[i];
            float height = (t / maxTime) * g_perfGraphHeight;
            float x = g_perfGraphX + i * barWidth;
            float y = g_perfGraphY + g_perfGraphHeight - height;

            Color col = GREEN;
            if (t > 16.67f) col = YELLOW;
            if (t > 33.33f) col = RED;

            DrawRectangle((int)x, (int)y, (int)barWidth - 1, (int)height, col);
        }

        // Draw 60 FPS line
        float targetY = g_perfGraphY + g_perfGraphHeight - (16.67f / maxTime) * g_perfGraphHeight;
        DrawLine((int)g_perfGraphX, (int)targetY, (int)(g_perfGraphX + g_perfGraphWidth), (int)targetY, Color{ 0, 255, 0, 128 });

        // Labels
        char buf[64];
        snprintf(buf, sizeof(buf), "%.1f ms", g_currentFrameTime);
        DrawText(buf, (int)g_perfGraphX + 2, (int)g_perfGraphY + 2, 10, WHITE);
    }

    // Console/Logging
    void Framework_Log(int level, const char* message) {
        if (!message || level < g_logMinLevel) return;

        const char* levelStr = "INFO";
        switch (level) {
            case LOG_LEVEL_TRACE: levelStr = "TRACE"; break;
            case LOG_LEVEL_DEBUG: levelStr = "DEBUG"; break;
            case LOG_LEVEL_INFO: levelStr = "INFO"; break;
            case LOG_LEVEL_WARNING: levelStr = "WARN"; break;
            case LOG_LEVEL_ERROR: levelStr = "ERROR"; break;
            case LOG_LEVEL_FATAL: levelStr = "FATAL"; break;
        }

        char buf[512];
        snprintf(buf, sizeof(buf), "[%s] %s", levelStr, message);

        // Output to raylib log
        TraceLog(LOG_INFO, "%s", buf);

        // Output to file if open
        if (g_logFileOpen && g_logFile.is_open()) {
            g_logFile << buf << std::endl;
        }

        // Add to console
        Color col = WHITE;
        switch (level) {
            case LOG_LEVEL_TRACE: col = GRAY; break;
            case LOG_LEVEL_DEBUG: col = LIGHTGRAY; break;
            case LOG_LEVEL_INFO: col = WHITE; break;
            case LOG_LEVEL_WARNING: col = YELLOW; break;
            case LOG_LEVEL_ERROR: col = RED; break;
            case LOG_LEVEL_FATAL: col = MAROON; break;
        }
        Framework_Console_PrintColored(buf, col.r, col.g, col.b);
    }

    void Framework_Log_SetMinLevel(int level) {
        g_logMinLevel = level;
    }

    int Framework_Log_GetMinLevel() {
        return g_logMinLevel;
    }

    void Framework_Log_SetFileOutput(const char* filename) {
        if (g_logFileOpen) {
            g_logFile.close();
        }
        if (filename) {
            g_logFile.open(filename, std::ios::out | std::ios::app);
            g_logFileOpen = g_logFile.is_open();
        }
    }

    void Framework_Log_CloseFile() {
        if (g_logFileOpen) {
            g_logFile.close();
            g_logFileOpen = false;
        }
    }

    // On-screen console
    void Framework_Console_SetEnabled(bool enabled) {
        g_consoleEnabled = enabled;
    }

    bool Framework_Console_IsEnabled() {
        return g_consoleEnabled;
    }

    void Framework_Console_SetPosition(float x, float y) {
        g_consoleX = x;
        g_consoleY = y;
    }

    void Framework_Console_SetSize(float width, float height) {
        g_consoleWidth = width;
        g_consoleHeight = height;
    }

    void Framework_Console_SetMaxLines(int maxLines) {
        g_consoleMaxLines = maxLines;
        while ((int)g_consoleLines.size() > maxLines) {
            g_consoleLines.erase(g_consoleLines.begin());
        }
    }

    void Framework_Console_Clear() {
        g_consoleLines.clear();
    }

    void Framework_Console_Print(const char* message) {
        Framework_Console_PrintColored(message, 255, 255, 255);
    }

    void Framework_Console_PrintColored(const char* message, unsigned char r, unsigned char g, unsigned char b) {
        if (!message) return;

        ConsoleLine line;
        line.text = message;
        line.color = Color{ r, g, b, 255 };
        g_consoleLines.push_back(line);

        while ((int)g_consoleLines.size() > g_consoleMaxLines) {
            g_consoleLines.erase(g_consoleLines.begin());
        }
    }

    void Framework_Console_Draw() {
        if (!g_consoleEnabled) return;

        // Background
        DrawRectangle((int)g_consoleX, (int)g_consoleY, (int)g_consoleWidth, (int)g_consoleHeight, Color{ 0, 0, 0, 200 });
        DrawRectangleLinesEx(Rectangle{ g_consoleX, g_consoleY, g_consoleWidth, g_consoleHeight }, 1, Color{ 100, 100, 100, 255 });

        // Draw lines from bottom up
        int lineHeight = 12;
        int maxVisible = (int)(g_consoleHeight / lineHeight) - 1;
        int startLine = std::max(0, (int)g_consoleLines.size() - maxVisible);

        float y = g_consoleY + g_consoleHeight - lineHeight - 2;
        for (int i = (int)g_consoleLines.size() - 1; i >= startLine && y > g_consoleY; i--) {
            DrawText(g_consoleLines[i].text.c_str(), (int)g_consoleX + 4, (int)y, 10, g_consoleLines[i].color);
            y -= lineHeight;
        }
    }

    // Debug drawing
    void Framework_DebugDraw_Line(float x1, float y1, float x2, float y2, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::LINE;
        shape.x1 = x1; shape.y1 = y1;
        shape.x2 = x2; shape.y2 = y2;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Rect(float x, float y, float w, float h, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::RECT;
        shape.x1 = x; shape.y1 = y;
        shape.x2 = w; shape.y2 = h;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_RectFilled(float x, float y, float w, float h, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::RECT_FILLED;
        shape.x1 = x; shape.y1 = y;
        shape.x2 = w; shape.y2 = h;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Circle(float x, float y, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::CIRCLE;
        shape.x1 = x; shape.y1 = y;
        shape.size = radius;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_CircleFilled(float x, float y, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::CIRCLE_FILLED;
        shape.x1 = x; shape.y1 = y;
        shape.size = radius;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Point(float x, float y, float size, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::POINT;
        shape.x1 = x; shape.y1 = y;
        shape.size = size;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Arrow(float x1, float y1, float x2, float y2, float headSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::ARROW;
        shape.x1 = x1; shape.y1 = y1;
        shape.x2 = x2; shape.y2 = y2;
        shape.size = headSize;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Text(float x, float y, const char* text, unsigned char r, unsigned char g, unsigned char b) {
        if (!g_debugDrawEnabled || !text) return;
        DebugShape shape;
        shape.type = DebugShape::TEXT;
        shape.x1 = x; shape.y1 = y;
        shape.text = text;
        shape.color = Color{ r, g, b, 255 };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Grid(float cellSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::GRID;
        shape.size = cellSize;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_Cross(float x, float y, float size, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        if (!g_debugDrawEnabled) return;
        DebugShape shape;
        shape.type = DebugShape::CROSS;
        shape.x1 = x; shape.y1 = y;
        shape.size = size;
        shape.color = Color{ r, g, b, a };
        g_debugShapes.push_back(shape);
    }

    void Framework_DebugDraw_SetEnabled(bool enabled) {
        g_debugDrawEnabled = enabled;
    }

    bool Framework_DebugDraw_IsEnabled() {
        return g_debugDrawEnabled;
    }

    void Framework_DebugDraw_SetPersistent(bool persistent) {
        g_debugDrawPersistent = persistent;
    }

    void Framework_DebugDraw_Clear() {
        g_debugShapes.clear();
    }

    void Framework_DebugDraw_Flush() {
        if (!g_debugDrawEnabled) return;

        for (auto& shape : g_debugShapes) {
            switch (shape.type) {
                case DebugShape::LINE:
                    DrawLineV(Vector2{ shape.x1, shape.y1 }, Vector2{ shape.x2, shape.y2 }, shape.color);
                    break;
                case DebugShape::RECT:
                    DrawRectangleLinesEx(Rectangle{ shape.x1, shape.y1, shape.x2, shape.y2 }, 1, shape.color);
                    break;
                case DebugShape::RECT_FILLED:
                    DrawRectangle((int)shape.x1, (int)shape.y1, (int)shape.x2, (int)shape.y2, shape.color);
                    break;
                case DebugShape::CIRCLE:
                    DrawCircleLines((int)shape.x1, (int)shape.y1, shape.size, shape.color);
                    break;
                case DebugShape::CIRCLE_FILLED:
                    DrawCircle((int)shape.x1, (int)shape.y1, shape.size, shape.color);
                    break;
                case DebugShape::POINT:
                    DrawCircle((int)shape.x1, (int)shape.y1, shape.size, shape.color);
                    break;
                case DebugShape::ARROW: {
                    DrawLineV(Vector2{ shape.x1, shape.y1 }, Vector2{ shape.x2, shape.y2 }, shape.color);
                    // Draw arrow head
                    float dx = shape.x2 - shape.x1;
                    float dy = shape.y2 - shape.y1;
                    float len = sqrtf(dx * dx + dy * dy);
                    if (len > 0) {
                        dx /= len; dy /= len;
                        float px = -dy, py = dx;  // Perpendicular
                        float ax = shape.x2 - dx * shape.size;
                        float ay = shape.y2 - dy * shape.size;
                        DrawLineV(Vector2{ shape.x2, shape.y2 }, Vector2{ ax + px * shape.size * 0.5f, ay + py * shape.size * 0.5f }, shape.color);
                        DrawLineV(Vector2{ shape.x2, shape.y2 }, Vector2{ ax - px * shape.size * 0.5f, ay - py * shape.size * 0.5f }, shape.color);
                    }
                    break;
                }
                case DebugShape::TEXT:
                    DrawText(shape.text.c_str(), (int)shape.x1, (int)shape.y1, 10, shape.color);
                    break;
                case DebugShape::GRID: {
                    int screenW = GetScreenWidth();
                    int screenH = GetScreenHeight();
                    for (float x = 0; x < screenW; x += shape.size) {
                        DrawLine((int)x, 0, (int)x, screenH, shape.color);
                    }
                    for (float y = 0; y < screenH; y += shape.size) {
                        DrawLine(0, (int)y, screenW, (int)y, shape.color);
                    }
                    break;
                }
                case DebugShape::CROSS:
                    DrawLine((int)(shape.x1 - shape.size), (int)shape.y1, (int)(shape.x1 + shape.size), (int)shape.y1, shape.color);
                    DrawLine((int)shape.x1, (int)(shape.y1 - shape.size), (int)shape.x1, (int)(shape.y1 + shape.size), shape.color);
                    break;
            }
        }

        if (!g_debugDrawPersistent) {
            g_debugShapes.clear();
        }
    }

    // System overlays
    void Framework_Debug_SetShowFPS(bool show) { g_showFPS = show; }
    void Framework_Debug_SetShowFrameTime(bool show) { g_showFrameTime = show; }
    void Framework_Debug_SetShowDrawCalls(bool show) { g_showDrawCalls = show; }
    void Framework_Debug_SetShowEntityCount(bool show) { g_showEntityCount = show; }
    void Framework_Debug_SetShowMemory(bool show) { g_showMemory = show; }
    void Framework_Debug_SetShowPhysics(bool show) { g_showPhysics = show; }
    void Framework_Debug_SetShowColliders(bool show) { g_showColliders = show; }

    void Framework_Debug_SetOverlayPosition(float x, float y) {
        g_overlayX = x;
        g_overlayY = y;
    }

    void Framework_Debug_SetOverlayColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        g_overlayColor = Color{ r, g, b, a };
    }

    // Frame profiling
    void Framework_Perf_BeginFrame() {
        g_frameStartTime = GetTime();
        Framework_Perf_ResetDrawStats();
    }

    void Framework_Perf_EndFrame() {
        g_currentFrameTime = (float)((GetTime() - g_frameStartTime) * 1000.0);
        g_totalFrameCount++;

        // Add to history
        g_frameTimeHistory.push_back(g_currentFrameTime);
        while ((int)g_frameTimeHistory.size() > g_perfSampleCount) {
            g_frameTimeHistory.erase(g_frameTimeHistory.begin());
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
    // PHYSICS SYSTEM - 2D Rigid Body Physics Implementation
    // ========================================================================

    // Physics body structure
    struct PhysicsBody {
        int handle = -1;
        int type = BODY_DYNAMIC;
        bool valid = true;

        // Transform
        float x = 0, y = 0;
        float rotation = 0;  // radians

        // Dynamics
        float vx = 0, vy = 0;
        float angularVelocity = 0;
        float forceX = 0, forceY = 0;
        float torque = 0;

        // Properties
        float mass = 1.0f;
        float invMass = 1.0f;  // 1/mass, 0 for static
        float inertia = 1.0f;
        float invInertia = 1.0f;
        float restitution = 0.2f;  // Bounciness
        float friction = 0.3f;
        float gravityScale = 1.0f;
        float linearDamping = 0.0f;
        float angularDamping = 0.0f;
        bool fixedRotation = false;
        bool sleepingAllowed = true;
        bool awake = true;

        // Shape
        int shapeType = SHAPE_BOX;
        float shapeRadius = 16.0f;  // For circle
        float shapeWidth = 32.0f;   // For box
        float shapeHeight = 32.0f;
        float shapeOffsetX = 0, shapeOffsetY = 0;
        std::vector<float> polygonVerts;  // x,y pairs for polygon

        // Collision filtering
        unsigned int layer = 1;
        unsigned int mask = 0xFFFFFFFF;
        bool isTrigger = false;

        // Entity binding
        int boundEntity = -1;
        int userData = 0;
    };

    // Physics world state
    static std::unordered_map<int, PhysicsBody> g_physicsBodies;
    static int g_physicsNextHandle = 1;
    static float g_gravityX = 0.0f;
    static float g_gravityY = 980.0f;  // Default gravity (pixels/s^2)
    static int g_velocityIterations = 8;
    static int g_positionIterations = 3;
    static bool g_physicsEnabled = true;
    static bool g_physicsDebugDraw = false;

    // Collision callbacks
    static PhysicsCollisionCallback g_onCollisionEnter = nullptr;
    static PhysicsCollisionCallback g_onCollisionStay = nullptr;
    static PhysicsCollisionCallback g_onCollisionExit = nullptr;
    static PhysicsCollisionCallback g_onTriggerEnter = nullptr;
    static PhysicsCollisionCallback g_onTriggerExit = nullptr;

    // Collision tracking for enter/stay/exit detection
    struct CollisionPair {
        int bodyA, bodyB;
        bool operator==(const CollisionPair& o) const { return (bodyA == o.bodyA && bodyB == o.bodyB) || (bodyA == o.bodyB && bodyB == o.bodyA); }
    };
    struct CollisionPairHash {
        size_t operator()(const CollisionPair& p) const {
            int a = p.bodyA < p.bodyB ? p.bodyA : p.bodyB;
            int b = p.bodyA < p.bodyB ? p.bodyB : p.bodyA;
            return std::hash<long long>()(((long long)a << 32) | b);
        }
    };
    static std::unordered_set<CollisionPair, CollisionPairHash> g_activeCollisions;
    static std::unordered_set<CollisionPair, CollisionPairHash> g_prevCollisions;

    // Entity-to-body mapping
    static std::unordered_map<int, int> g_entityToBody;

    // Physics helper functions
    namespace {
        float Physics_Dot(float ax, float ay, float bx, float by) {
            return ax * bx + ay * by;
        }

        float Physics_Cross(float ax, float ay, float bx, float by) {
            return ax * by - ay * bx;
        }

        float Physics_Length(float x, float y) {
            return sqrtf(x * x + y * y);
        }

        void Physics_Normalize(float& x, float& y) {
            float len = Physics_Length(x, y);
            if (len > 0.0001f) { x /= len; y /= len; }
        }

        // Get AABB bounds for a body
        void Physics_GetAABB(const PhysicsBody& body, float& minX, float& minY, float& maxX, float& maxY) {
            if (body.shapeType == SHAPE_CIRCLE) {
                minX = body.x + body.shapeOffsetX - body.shapeRadius;
                minY = body.y + body.shapeOffsetY - body.shapeRadius;
                maxX = body.x + body.shapeOffsetX + body.shapeRadius;
                maxY = body.y + body.shapeOffsetY + body.shapeRadius;
            } else {  // BOX or POLYGON (use box bounds)
                float hw = body.shapeWidth / 2;
                float hh = body.shapeHeight / 2;
                minX = body.x + body.shapeOffsetX - hw;
                minY = body.y + body.shapeOffsetY - hh;
                maxX = body.x + body.shapeOffsetX + hw;
                maxY = body.y + body.shapeOffsetY + hh;
            }
        }

        // Circle vs Circle collision
        bool Physics_CircleVsCircle(const PhysicsBody& a, const PhysicsBody& b,
            float& normalX, float& normalY, float& depth) {
            float ax = a.x + a.shapeOffsetX;
            float ay = a.y + a.shapeOffsetY;
            float bx = b.x + b.shapeOffsetX;
            float by = b.y + b.shapeOffsetY;

            float dx = bx - ax;
            float dy = by - ay;
            float dist = Physics_Length(dx, dy);
            float sumRadii = a.shapeRadius + b.shapeRadius;

            if (dist >= sumRadii) return false;

            if (dist > 0.0001f) {
                normalX = dx / dist;
                normalY = dy / dist;
            } else {
                normalX = 1; normalY = 0;
            }
            depth = sumRadii - dist;
            return true;
        }

        // Box vs Box collision (AABB)
        bool Physics_BoxVsBox(const PhysicsBody& a, const PhysicsBody& b,
            float& normalX, float& normalY, float& depth) {
            float aMinX, aMinY, aMaxX, aMaxY;
            float bMinX, bMinY, bMaxX, bMaxY;
            Physics_GetAABB(a, aMinX, aMinY, aMaxX, aMaxY);
            Physics_GetAABB(b, bMinX, bMinY, bMaxX, bMaxY);

            float overlapX = fminf(aMaxX, bMaxX) - fmaxf(aMinX, bMinX);
            float overlapY = fminf(aMaxY, bMaxY) - fmaxf(aMinY, bMinY);

            if (overlapX <= 0 || overlapY <= 0) return false;

            if (overlapX < overlapY) {
                normalX = (a.x < b.x) ? -1.0f : 1.0f;
                normalY = 0;
                depth = overlapX;
            } else {
                normalX = 0;
                normalY = (a.y < b.y) ? -1.0f : 1.0f;
                depth = overlapY;
            }
            return true;
        }

        // Circle vs Box collision
        bool Physics_CircleVsBox(const PhysicsBody& circle, const PhysicsBody& box,
            float& normalX, float& normalY, float& depth) {
            float cx = circle.x + circle.shapeOffsetX;
            float cy = circle.y + circle.shapeOffsetY;

            float bMinX, bMinY, bMaxX, bMaxY;
            Physics_GetAABB(box, bMinX, bMinY, bMaxX, bMaxY);

            // Find closest point on box to circle center
            float closestX = fmaxf(bMinX, fminf(cx, bMaxX));
            float closestY = fmaxf(bMinY, fminf(cy, bMaxY));

            float dx = cx - closestX;
            float dy = cy - closestY;
            float dist = Physics_Length(dx, dy);

            if (dist >= circle.shapeRadius) return false;

            if (dist > 0.0001f) {
                normalX = dx / dist;
                normalY = dy / dist;
            } else {
                // Circle center is inside box
                float toLeft = cx - bMinX;
                float toRight = bMaxX - cx;
                float toTop = cy - bMinY;
                float toBottom = bMaxY - cy;
                float minDist = fminf(fminf(toLeft, toRight), fminf(toTop, toBottom));
                if (minDist == toLeft) { normalX = -1; normalY = 0; }
                else if (minDist == toRight) { normalX = 1; normalY = 0; }
                else if (minDist == toTop) { normalX = 0; normalY = -1; }
                else { normalX = 0; normalY = 1; }
            }
            depth = circle.shapeRadius - dist;
            return true;
        }

        // Test collision between two bodies
        bool Physics_TestCollision(const PhysicsBody& a, const PhysicsBody& b,
            float& normalX, float& normalY, float& depth) {
            // Check layer masks
            if (!(a.layer & b.mask) || !(b.layer & a.mask)) return false;

            if (a.shapeType == SHAPE_CIRCLE && b.shapeType == SHAPE_CIRCLE) {
                return Physics_CircleVsCircle(a, b, normalX, normalY, depth);
            } else if (a.shapeType == SHAPE_BOX && b.shapeType == SHAPE_BOX) {
                return Physics_BoxVsBox(a, b, normalX, normalY, depth);
            } else if (a.shapeType == SHAPE_CIRCLE && b.shapeType == SHAPE_BOX) {
                return Physics_CircleVsBox(a, b, normalX, normalY, depth);
            } else if (a.shapeType == SHAPE_BOX && b.shapeType == SHAPE_CIRCLE) {
                bool result = Physics_CircleVsBox(b, a, normalX, normalY, depth);
                normalX = -normalX; normalY = -normalY;
                return result;
            }
            // Polygon collision would go here - using AABB approximation
            return Physics_BoxVsBox(a, b, normalX, normalY, depth);
        }

        // Resolve collision between two bodies
        void Physics_ResolveCollision(PhysicsBody& a, PhysicsBody& b,
            float normalX, float normalY, float depth) {
            // Skip triggers
            if (a.isTrigger || b.isTrigger) return;

            // Calculate inverse masses
            float invMassA = (a.type == BODY_STATIC) ? 0 : a.invMass;
            float invMassB = (b.type == BODY_STATIC) ? 0 : b.invMass;
            float totalInvMass = invMassA + invMassB;

            if (totalInvMass == 0) return;  // Both static

            // Separate bodies (positional correction)
            float correctionPercent = 0.8f;
            float slop = 0.01f;  // Small penetration allowed
            float correction = fmaxf(depth - slop, 0.0f) / totalInvMass * correctionPercent;

            if (a.type != BODY_STATIC) {
                a.x -= normalX * correction * invMassA;
                a.y -= normalY * correction * invMassA;
            }
            if (b.type != BODY_STATIC) {
                b.x += normalX * correction * invMassB;
                b.y += normalY * correction * invMassB;
            }

            // Calculate relative velocity
            float relVelX = b.vx - a.vx;
            float relVelY = b.vy - a.vy;
            float relVelNormal = Physics_Dot(relVelX, relVelY, normalX, normalY);

            // Don't resolve if velocities are separating
            if (relVelNormal > 0) return;

            // Calculate restitution (bounce)
            float e = fminf(a.restitution, b.restitution);

            // Calculate impulse scalar
            float j = -(1 + e) * relVelNormal / totalInvMass;

            // Apply impulse
            if (a.type != BODY_STATIC) {
                a.vx -= j * invMassA * normalX;
                a.vy -= j * invMassA * normalY;
            }
            if (b.type != BODY_STATIC) {
                b.vx += j * invMassB * normalX;
                b.vy += j * invMassB * normalY;
            }

            // Friction impulse
            float tangentX = relVelX - relVelNormal * normalX;
            float tangentY = relVelY - relVelNormal * normalY;
            float tangentLen = Physics_Length(tangentX, tangentY);

            if (tangentLen > 0.0001f) {
                tangentX /= tangentLen;
                tangentY /= tangentLen;

                float jt = -Physics_Dot(relVelX, relVelY, tangentX, tangentY) / totalInvMass;
                float mu = sqrtf(a.friction * b.friction);  // Friction coefficient

                // Clamp friction impulse
                float maxFriction = fabsf(j) * mu;
                jt = fmaxf(-maxFriction, fminf(maxFriction, jt));

                if (a.type != BODY_STATIC) {
                    a.vx -= jt * invMassA * tangentX;
                    a.vy -= jt * invMassA * tangentY;
                }
                if (b.type != BODY_STATIC) {
                    b.vx += jt * invMassB * tangentX;
                    b.vy += jt * invMassB * tangentY;
                }
            }
        }
    }

    // World settings
    void Framework_Physics_SetGravity(float gx, float gy) {
        g_gravityX = gx;
        g_gravityY = gy;
    }

    void Framework_Physics_GetGravity(float* gx, float* gy) {
        if (gx) *gx = g_gravityX;
        if (gy) *gy = g_gravityY;
    }

    void Framework_Physics_SetIterations(int velocityIterations, int positionIterations) {
        g_velocityIterations = velocityIterations > 0 ? velocityIterations : 1;
        g_positionIterations = positionIterations > 0 ? positionIterations : 1;
    }

    void Framework_Physics_SetEnabled(bool enabled) { g_physicsEnabled = enabled; }
    bool Framework_Physics_IsEnabled() { return g_physicsEnabled; }

    // Body creation/destruction
    int Framework_Physics_CreateBody(int bodyType, float x, float y) {
        PhysicsBody body;
        body.handle = g_physicsNextHandle++;
        body.type = bodyType;
        body.x = x;
        body.y = y;

        if (bodyType == BODY_STATIC) {
            body.invMass = 0;
            body.invInertia = 0;
        }

        g_physicsBodies[body.handle] = body;
        return body.handle;
    }

    void Framework_Physics_DestroyBody(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) {
            if (it->second.boundEntity >= 0) {
                g_entityToBody.erase(it->second.boundEntity);
            }
            g_physicsBodies.erase(it);
        }
    }

    bool Framework_Physics_IsBodyValid(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return it != g_physicsBodies.end() && it->second.valid;
    }

    void Framework_Physics_DestroyAllBodies() {
        g_physicsBodies.clear();
        g_entityToBody.clear();
        g_activeCollisions.clear();
        g_prevCollisions.clear();
    }

    // Body type
    void Framework_Physics_SetBodyType(int bodyHandle, int bodyType) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end()) return;
        it->second.type = bodyType;
        if (bodyType == BODY_STATIC) {
            it->second.invMass = 0;
            it->second.invInertia = 0;
            it->second.vx = it->second.vy = 0;
        } else {
            it->second.invMass = 1.0f / it->second.mass;
            it->second.invInertia = 1.0f / it->second.inertia;
        }
    }

    int Framework_Physics_GetBodyType(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.type : BODY_STATIC;
    }

    // Body transform
    void Framework_Physics_SetBodyPosition(int bodyHandle, float x, float y) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.x = x; it->second.y = y; }
    }

    void Framework_Physics_GetBodyPosition(int bodyHandle, float* x, float* y) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) {
            if (x) *x = it->second.x;
            if (y) *y = it->second.y;
        }
    }

    void Framework_Physics_SetBodyRotation(int bodyHandle, float radians) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.rotation = radians; }
    }

    float Framework_Physics_GetBodyRotation(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.rotation : 0;
    }

    // Body dynamics
    void Framework_Physics_SetBodyVelocity(int bodyHandle, float vx, float vy) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.vx = vx; it->second.vy = vy; }
    }

    void Framework_Physics_GetBodyVelocity(int bodyHandle, float* vx, float* vy) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) {
            if (vx) *vx = it->second.vx;
            if (vy) *vy = it->second.vy;
        }
    }

    void Framework_Physics_SetBodyAngularVelocity(int bodyHandle, float omega) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.angularVelocity = omega; }
    }

    float Framework_Physics_GetBodyAngularVelocity(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.angularVelocity : 0;
    }

    void Framework_Physics_ApplyForce(int bodyHandle, float fx, float fy) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end() && it->second.type != BODY_STATIC) {
            it->second.forceX += fx;
            it->second.forceY += fy;
            it->second.awake = true;
        }
    }

    void Framework_Physics_ApplyForceAtPoint(int bodyHandle, float fx, float fy, float px, float py) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end() || it->second.type == BODY_STATIC) return;
        it->second.forceX += fx;
        it->second.forceY += fy;
        // Calculate torque from force at point
        float rx = px - it->second.x;
        float ry = py - it->second.y;
        it->second.torque += Physics_Cross(rx, ry, fx, fy);
        it->second.awake = true;
    }

    void Framework_Physics_ApplyImpulse(int bodyHandle, float ix, float iy) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end() && it->second.type != BODY_STATIC) {
            it->second.vx += ix * it->second.invMass;
            it->second.vy += iy * it->second.invMass;
            it->second.awake = true;
        }
    }

    void Framework_Physics_ApplyTorque(int bodyHandle, float torque) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end() && it->second.type != BODY_STATIC) {
            it->second.torque += torque;
            it->second.awake = true;
        }
    }

    // Body properties
    void Framework_Physics_SetBodyMass(int bodyHandle, float mass) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) {
            it->second.mass = mass > 0.0001f ? mass : 0.0001f;
            if (it->second.type != BODY_STATIC) {
                it->second.invMass = 1.0f / it->second.mass;
            }
        }
    }

    float Framework_Physics_GetBodyMass(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.mass : 0;
    }

    void Framework_Physics_SetBodyRestitution(int bodyHandle, float restitution) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.restitution = fmaxf(0, fminf(1, restitution)); }
    }

    float Framework_Physics_GetBodyRestitution(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.restitution : 0;
    }

    void Framework_Physics_SetBodyFriction(int bodyHandle, float friction) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.friction = fmaxf(0, fminf(1, friction)); }
    }

    float Framework_Physics_GetBodyFriction(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.friction : 0;
    }

    void Framework_Physics_SetBodyGravityScale(int bodyHandle, float scale) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.gravityScale = scale; }
    }

    float Framework_Physics_GetBodyGravityScale(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.gravityScale : 1.0f;
    }

    void Framework_Physics_SetBodyLinearDamping(int bodyHandle, float damping) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.linearDamping = fmaxf(0, damping); }
    }

    void Framework_Physics_SetBodyAngularDamping(int bodyHandle, float damping) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.angularDamping = fmaxf(0, damping); }
    }

    void Framework_Physics_SetBodyFixedRotation(int bodyHandle, bool fixed) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.fixedRotation = fixed; }
    }

    bool Framework_Physics_IsBodyFixedRotation(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.fixedRotation : false;
    }

    void Framework_Physics_SetBodySleepingAllowed(int bodyHandle, bool allowed) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.sleepingAllowed = allowed; }
    }

    void Framework_Physics_WakeBody(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.awake = true; }
    }

    bool Framework_Physics_IsBodyAwake(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.awake : false;
    }

    // Collision shapes
    void Framework_Physics_SetBodyCircle(int bodyHandle, float radius) {
        Framework_Physics_SetBodyCircleOffset(bodyHandle, radius, 0, 0);
    }

    void Framework_Physics_SetBodyCircleOffset(int bodyHandle, float radius, float offsetX, float offsetY) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end()) return;
        it->second.shapeType = SHAPE_CIRCLE;
        it->second.shapeRadius = radius;
        it->second.shapeOffsetX = offsetX;
        it->second.shapeOffsetY = offsetY;
        // Update inertia for circle: I = 0.5 * m * r^2
        it->second.inertia = 0.5f * it->second.mass * radius * radius;
        if (it->second.type != BODY_STATIC) {
            it->second.invInertia = 1.0f / it->second.inertia;
        }
    }

    void Framework_Physics_SetBodyBox(int bodyHandle, float width, float height) {
        Framework_Physics_SetBodyBoxOffset(bodyHandle, width, height, 0, 0);
    }

    void Framework_Physics_SetBodyBoxOffset(int bodyHandle, float width, float height, float offsetX, float offsetY) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end()) return;
        it->second.shapeType = SHAPE_BOX;
        it->second.shapeWidth = width;
        it->second.shapeHeight = height;
        it->second.shapeOffsetX = offsetX;
        it->second.shapeOffsetY = offsetY;
        // Update inertia for box: I = (1/12) * m * (w^2 + h^2)
        it->second.inertia = (1.0f / 12.0f) * it->second.mass * (width * width + height * height);
        if (it->second.type != BODY_STATIC) {
            it->second.invInertia = 1.0f / it->second.inertia;
        }
    }

    void Framework_Physics_SetBodyPolygon(int bodyHandle, const float* vertices, int vertexCount) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end() || !vertices || vertexCount < 3) return;
        it->second.shapeType = SHAPE_POLYGON;
        it->second.polygonVerts.clear();
        it->second.polygonVerts.assign(vertices, vertices + vertexCount * 2);
        // Calculate bounding box for AABB approximation
        float minX = vertices[0], maxX = vertices[0];
        float minY = vertices[1], maxY = vertices[1];
        for (int i = 1; i < vertexCount; i++) {
            minX = fminf(minX, vertices[i * 2]);
            maxX = fmaxf(maxX, vertices[i * 2]);
            minY = fminf(minY, vertices[i * 2 + 1]);
            maxY = fmaxf(maxY, vertices[i * 2 + 1]);
        }
        it->second.shapeWidth = maxX - minX;
        it->second.shapeHeight = maxY - minY;
    }

    int Framework_Physics_GetBodyShapeType(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.shapeType : SHAPE_BOX;
    }

    // Collision filtering
    void Framework_Physics_SetBodyLayer(int bodyHandle, unsigned int layer) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.layer = layer; }
    }

    void Framework_Physics_SetBodyMask(int bodyHandle, unsigned int mask) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.mask = mask; }
    }

    void Framework_Physics_SetBodyTrigger(int bodyHandle, bool isTrigger) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.isTrigger = isTrigger; }
    }

    bool Framework_Physics_IsBodyTrigger(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.isTrigger : false;
    }

    // Entity binding
    void Framework_Physics_BindToEntity(int bodyHandle, int entityId) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it == g_physicsBodies.end()) return;
        // Unbind previous entity if any
        if (it->second.boundEntity >= 0) {
            g_entityToBody.erase(it->second.boundEntity);
        }
        it->second.boundEntity = entityId;
        if (entityId >= 0) {
            g_entityToBody[entityId] = bodyHandle;
        }
    }

    int Framework_Physics_GetBoundEntity(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.boundEntity : -1;
    }

    int Framework_Physics_GetEntityBody(int entityId) {
        auto it = g_entityToBody.find(entityId);
        return (it != g_entityToBody.end()) ? it->second : -1;
    }

    // User data
    void Framework_Physics_SetBodyUserData(int bodyHandle, int userData) {
        auto it = g_physicsBodies.find(bodyHandle);
        if (it != g_physicsBodies.end()) { it->second.userData = userData; }
    }

    int Framework_Physics_GetBodyUserData(int bodyHandle) {
        auto it = g_physicsBodies.find(bodyHandle);
        return (it != g_physicsBodies.end()) ? it->second.userData : 0;
    }

    // Collision callbacks
    void Framework_Physics_SetCollisionEnterCallback(PhysicsCollisionCallback callback) { g_onCollisionEnter = callback; }
    void Framework_Physics_SetCollisionStayCallback(PhysicsCollisionCallback callback) { g_onCollisionStay = callback; }
    void Framework_Physics_SetCollisionExitCallback(PhysicsCollisionCallback callback) { g_onCollisionExit = callback; }
    void Framework_Physics_SetTriggerEnterCallback(PhysicsCollisionCallback callback) { g_onTriggerEnter = callback; }
    void Framework_Physics_SetTriggerExitCallback(PhysicsCollisionCallback callback) { g_onTriggerExit = callback; }

    // Physics queries
    int Framework_Physics_RaycastFirst(float startX, float startY, float dirX, float dirY, float maxDist,
        float* hitX, float* hitY, float* hitNormalX, float* hitNormalY) {
        Physics_Normalize(dirX, dirY);
        float closestT = maxDist;
        int closestBody = -1;
        float closestNX = 0, closestNY = 0;

        for (auto& kv : g_physicsBodies) {
            const PhysicsBody& body = kv.second;
            if (!body.valid) continue;

            // Simple ray-AABB intersection for now
            float minX, minY, maxX, maxY;
            Physics_GetAABB(body, minX, minY, maxX, maxY);

            float tmin = 0, tmax = maxDist;
            float nx = 0, ny = 0;

            // X slab
            if (fabsf(dirX) > 0.0001f) {
                float t1 = (minX - startX) / dirX;
                float t2 = (maxX - startX) / dirX;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) { tmin = t1; nx = -dirX / fabsf(dirX); ny = 0; }
                if (t2 < tmax) tmax = t2;
            } else if (startX < minX || startX > maxX) continue;

            // Y slab
            if (fabsf(dirY) > 0.0001f) {
                float t1 = (minY - startY) / dirY;
                float t2 = (maxY - startY) / dirY;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) { tmin = t1; nx = 0; ny = -dirY / fabsf(dirY); }
                if (t2 < tmax) tmax = t2;
            } else if (startY < minY || startY > maxY) continue;

            if (tmin <= tmax && tmin > 0 && tmin < closestT) {
                closestT = tmin;
                closestBody = kv.first;
                closestNX = nx;
                closestNY = ny;
            }
        }

        if (closestBody >= 0) {
            if (hitX) *hitX = startX + dirX * closestT;
            if (hitY) *hitY = startY + dirY * closestT;
            if (hitNormalX) *hitNormalX = closestNX;
            if (hitNormalY) *hitNormalY = closestNY;
        }
        return closestBody;
    }

    int Framework_Physics_RaycastAll(float startX, float startY, float dirX, float dirY, float maxDist,
        int* bodyBuffer, int bufferSize) {
        if (!bodyBuffer || bufferSize <= 0) return 0;
        Physics_Normalize(dirX, dirY);
        int count = 0;

        for (auto& kv : g_physicsBodies) {
            if (count >= bufferSize) break;
            const PhysicsBody& body = kv.second;
            if (!body.valid) continue;

            float minX, minY, maxX, maxY;
            Physics_GetAABB(body, minX, minY, maxX, maxY);

            // Ray-AABB test
            float tmin = 0, tmax = maxDist;
            if (fabsf(dirX) > 0.0001f) {
                float t1 = (minX - startX) / dirX;
                float t2 = (maxX - startX) / dirX;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tmin = fmaxf(tmin, t1);
                tmax = fminf(tmax, t2);
            } else if (startX < minX || startX > maxX) continue;

            if (fabsf(dirY) > 0.0001f) {
                float t1 = (minY - startY) / dirY;
                float t2 = (maxY - startY) / dirY;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tmin = fmaxf(tmin, t1);
                tmax = fminf(tmax, t2);
            } else if (startY < minY || startY > maxY) continue;

            if (tmin <= tmax && tmin > 0) {
                bodyBuffer[count++] = kv.first;
            }
        }
        return count;
    }

    int Framework_Physics_QueryCircle(float x, float y, float radius, int* bodyBuffer, int bufferSize) {
        if (!bodyBuffer || bufferSize <= 0) return 0;
        int count = 0;

        for (auto& kv : g_physicsBodies) {
            if (count >= bufferSize) break;
            const PhysicsBody& body = kv.second;
            if (!body.valid) continue;

            float minX, minY, maxX, maxY;
            Physics_GetAABB(body, minX, minY, maxX, maxY);

            // Expand AABB by radius and check point
            if (x >= minX - radius && x <= maxX + radius && y >= minY - radius && y <= maxY + radius) {
                bodyBuffer[count++] = kv.first;
            }
        }
        return count;
    }

    int Framework_Physics_QueryBox(float x, float y, float width, float height, int* bodyBuffer, int bufferSize) {
        if (!bodyBuffer || bufferSize <= 0) return 0;
        int count = 0;

        float qMinX = x - width / 2, qMinY = y - height / 2;
        float qMaxX = x + width / 2, qMaxY = y + height / 2;

        for (auto& kv : g_physicsBodies) {
            if (count >= bufferSize) break;
            const PhysicsBody& body = kv.second;
            if (!body.valid) continue;

            float minX, minY, maxX, maxY;
            Physics_GetAABB(body, minX, minY, maxX, maxY);

            if (qMaxX >= minX && qMinX <= maxX && qMaxY >= minY && qMinY <= maxY) {
                bodyBuffer[count++] = kv.first;
            }
        }
        return count;
    }

    bool Framework_Physics_TestOverlap(int bodyA, int bodyB) {
        auto itA = g_physicsBodies.find(bodyA);
        auto itB = g_physicsBodies.find(bodyB);
        if (itA == g_physicsBodies.end() || itB == g_physicsBodies.end()) return false;

        float nx, ny, depth;
        return Physics_TestCollision(itA->second, itB->second, nx, ny, depth);
    }

    // Simulation
    void Framework_Physics_Step(float dt) {
        if (!g_physicsEnabled || dt <= 0) return;

        // Integrate forces for dynamic bodies
        for (auto& kv : g_physicsBodies) {
            PhysicsBody& body = kv.second;
            if (!body.valid || body.type == BODY_STATIC || !body.awake) continue;

            if (body.type == BODY_DYNAMIC) {
                // Apply gravity
                body.vx += g_gravityX * body.gravityScale * dt;
                body.vy += g_gravityY * body.gravityScale * dt;

                // Apply accumulated forces
                body.vx += body.forceX * body.invMass * dt;
                body.vy += body.forceY * body.invMass * dt;
                body.forceX = body.forceY = 0;

                // Apply torque
                if (!body.fixedRotation) {
                    body.angularVelocity += body.torque * body.invInertia * dt;
                    body.torque = 0;
                }

                // Apply damping
                body.vx *= 1.0f / (1.0f + body.linearDamping * dt);
                body.vy *= 1.0f / (1.0f + body.linearDamping * dt);
                body.angularVelocity *= 1.0f / (1.0f + body.angularDamping * dt);
            }

            // Integrate velocity
            body.x += body.vx * dt;
            body.y += body.vy * dt;
            if (!body.fixedRotation) {
                body.rotation += body.angularVelocity * dt;
            }
        }

        // Collect active bodies
        std::vector<int> bodies;
        for (auto& kv : g_physicsBodies) {
            if (kv.second.valid) bodies.push_back(kv.first);
        }

        // Detect and resolve collisions
        g_activeCollisions.clear();

        for (int iter = 0; iter < g_positionIterations; iter++) {
            for (size_t i = 0; i < bodies.size(); i++) {
                for (size_t j = i + 1; j < bodies.size(); j++) {
                    int hA = bodies[i], hB = bodies[j];
                    PhysicsBody& a = g_physicsBodies[hA];
                    PhysicsBody& b = g_physicsBodies[hB];

                    // Skip if both are static
                    if (a.type == BODY_STATIC && b.type == BODY_STATIC) continue;

                    float normalX, normalY, depth;
                    if (Physics_TestCollision(a, b, normalX, normalY, depth)) {
                        CollisionPair pair = {hA, hB};
                        g_activeCollisions.insert(pair);

                        // Fire callbacks on first detection (iter == 0)
                        if (iter == 0) {
                            bool wasColliding = g_prevCollisions.count(pair) > 0;

                            if (a.isTrigger || b.isTrigger) {
                                if (!wasColliding && g_onTriggerEnter) {
                                    g_onTriggerEnter(hA, hB, normalX, normalY, depth);
                                }
                            } else {
                                if (!wasColliding && g_onCollisionEnter) {
                                    g_onCollisionEnter(hA, hB, normalX, normalY, depth);
                                } else if (wasColliding && g_onCollisionStay) {
                                    g_onCollisionStay(hA, hB, normalX, normalY, depth);
                                }
                            }
                        }

                        // Resolve collision
                        Physics_ResolveCollision(a, b, normalX, normalY, depth);
                    }
                }
            }
        }

        // Fire exit callbacks
        for (const auto& pair : g_prevCollisions) {
            if (g_activeCollisions.count(pair) == 0) {
                auto itA = g_physicsBodies.find(pair.bodyA);
                auto itB = g_physicsBodies.find(pair.bodyB);
                if (itA != g_physicsBodies.end() && itB != g_physicsBodies.end()) {
                    if (itA->second.isTrigger || itB->second.isTrigger) {
                        if (g_onTriggerExit) g_onTriggerExit(pair.bodyA, pair.bodyB, 0, 0, 0);
                    } else {
                        if (g_onCollisionExit) g_onCollisionExit(pair.bodyA, pair.bodyB, 0, 0, 0);
                    }
                }
            }
        }

        g_prevCollisions = g_activeCollisions;
    }

    void Framework_Physics_SyncToEntities() {
        for (auto& kv : g_physicsBodies) {
            PhysicsBody& body = kv.second;
            if (!body.valid || body.boundEntity < 0) continue;

            // Update entity transform from physics body
            auto trIt = g_transform2D.find(body.boundEntity);
            if (trIt != g_transform2D.end()) {
                trIt->second.position.x = body.x;
                trIt->second.position.y = body.y;
                trIt->second.rotation = body.rotation * RAD2DEG;
            }
        }
    }

    // Debug rendering
    void Framework_Physics_SetDebugDraw(bool enabled) { g_physicsDebugDraw = enabled; }
    bool Framework_Physics_IsDebugDrawEnabled() { return g_physicsDebugDraw; }

    void Framework_Physics_DrawDebug() {
        if (!g_physicsDebugDraw) return;

        for (auto& kv : g_physicsBodies) {
            const PhysicsBody& body = kv.second;
            if (!body.valid) continue;

            Color color;
            switch (body.type) {
                case BODY_STATIC: color = { 100, 100, 100, 200 }; break;
                case BODY_DYNAMIC: color = { 0, 200, 0, 200 }; break;
                case BODY_KINEMATIC: color = { 200, 200, 0, 200 }; break;
                default: color = WHITE; break;
            }

            if (body.isTrigger) {
                color = { 0, 150, 255, 100 };
            }

            if (body.shapeType == SHAPE_CIRCLE) {
                DrawCircleLines(
                    (int)(body.x + body.shapeOffsetX),
                    (int)(body.y + body.shapeOffsetY),
                    body.shapeRadius, color);
            } else {
                float hw = body.shapeWidth / 2;
                float hh = body.shapeHeight / 2;
                DrawRectangleLines(
                    (int)(body.x + body.shapeOffsetX - hw),
                    (int)(body.y + body.shapeOffsetY - hh),
                    (int)body.shapeWidth, (int)body.shapeHeight, color);
            }

            // Draw velocity vector
            if (body.type == BODY_DYNAMIC && (fabsf(body.vx) > 1 || fabsf(body.vy) > 1)) {
                DrawLine((int)body.x, (int)body.y,
                    (int)(body.x + body.vx * 0.1f), (int)(body.y + body.vy * 0.1f),
                    RED);
            }
        }
    }

    // ========================================================================
    // AUDIO MANAGER - Advanced Audio System Implementation
    // ========================================================================

    // Audio group state
    struct AudioGroupState {
        float volume = 1.0f;
        float targetVolume = 1.0f;
        float fadeSpeed = 0.0f;
        bool muted = false;
    };
    static AudioGroupState g_audioGroups[AUDIO_GROUP_COUNT];

    // Managed sound with group
    struct ManagedSound {
        Sound sound;
        int group = AUDIO_GROUP_SFX;
        float baseVolume = 1.0f;
        bool valid = false;
    };
    static std::unordered_map<int, ManagedSound> g_managedSounds;
    static int g_nextSoundHandle = 1;

    // Managed music with advanced features
    struct ManagedMusic {
        Music music;
        float baseVolume = 1.0f;
        float targetVolume = 1.0f;
        float fadeSpeed = 0.0f;
        bool looping = true;
        bool valid = false;
        bool playing = false;
    };
    static std::unordered_map<int, ManagedMusic> g_managedMusic;
    // Note: g_nextMusicHandle is defined in the anonymous namespace above

    // Sound pool for frequent sounds
    struct SoundPool {
        std::vector<Sound> sounds;
        int nextIndex = 0;
        int group = AUDIO_GROUP_SFX;
        bool valid = false;
    };
    static std::unordered_map<int, SoundPool> g_soundPools;
    static int g_nextPoolHandle = 1;

    // Playlist
    struct Playlist {
        std::vector<int> tracks;  // Music handles
        int currentIndex = 0;
        bool shuffle = false;
        int repeatMode = 1;  // 0=none, 1=all, 2=one
        float crossfadeDuration = 0.0f;
        bool playing = false;
        bool valid = false;
        std::vector<int> shuffleOrder;
    };
    static std::unordered_map<int, Playlist> g_playlists;
    static int g_nextPlaylistHandle = 1;
    static int g_activePlaylist = -1;

    // Spatial audio
    static float g_listenerX = 0.0f;
    static float g_listenerY = 0.0f;
    static float g_spatialMinDist = 100.0f;
    static float g_spatialMaxDist = 1000.0f;
    static bool g_spatialEnabled = true;

    // Crossfade state
    static int g_crossfadeFrom = -1;
    static int g_crossfadeTo = -1;
    static float g_crossfadeProgress = 0.0f;
    static float g_crossfadeDuration = 0.0f;

    // Helper: Calculate effective volume for a group
    static float Audio_GetEffectiveVolume(int group, float baseVolume) {
        if (group < 0 || group >= AUDIO_GROUP_COUNT) return baseVolume;

        float groupVol = g_audioGroups[group].muted ? 0.0f : g_audioGroups[group].volume;
        float masterVol = g_audioGroups[AUDIO_GROUP_MASTER].muted ? 0.0f : g_audioGroups[AUDIO_GROUP_MASTER].volume;

        return baseVolume * groupVol * masterVol;
    }

    // Helper: Calculate spatial pan and volume
    static void Audio_CalculateSpatial(float soundX, float soundY, float& outVolume, float& outPan) {
        if (!g_spatialEnabled) {
            outVolume = 1.0f;
            outPan = 0.5f;
            return;
        }

        float dx = soundX - g_listenerX;
        float dy = soundY - g_listenerY;
        float distance = sqrtf(dx * dx + dy * dy);

        // Volume falloff
        if (distance <= g_spatialMinDist) {
            outVolume = 1.0f;
        } else if (distance >= g_spatialMaxDist) {
            outVolume = 0.0f;
        } else {
            float t = (distance - g_spatialMinDist) / (g_spatialMaxDist - g_spatialMinDist);
            outVolume = 1.0f - t;
        }

        // Pan based on x position (-1 to 1, then convert to 0-1)
        float screenWidth = (float)GetScreenWidth();
        if (screenWidth > 0 && distance > 0.01f) {
            float normalizedX = dx / fmaxf(distance, g_spatialMaxDist);
            outPan = 0.5f + normalizedX * 0.5f;
            outPan = fmaxf(0.0f, fminf(1.0f, outPan));
        } else {
            outPan = 0.5f;
        }
    }

    // Group volume control
    void Framework_Audio_SetGroupVolume(int group, float volume) {
        if (group >= 0 && group < AUDIO_GROUP_COUNT) {
            g_audioGroups[group].volume = fmaxf(0.0f, fminf(1.0f, volume));
            g_audioGroups[group].targetVolume = g_audioGroups[group].volume;
            g_audioGroups[group].fadeSpeed = 0.0f;
        }
    }

    float Framework_Audio_GetGroupVolume(int group) {
        if (group >= 0 && group < AUDIO_GROUP_COUNT) {
            return g_audioGroups[group].volume;
        }
        return 0.0f;
    }

    void Framework_Audio_SetGroupMuted(int group, bool muted) {
        if (group >= 0 && group < AUDIO_GROUP_COUNT) {
            g_audioGroups[group].muted = muted;
        }
    }

    bool Framework_Audio_IsGroupMuted(int group) {
        if (group >= 0 && group < AUDIO_GROUP_COUNT) {
            return g_audioGroups[group].muted;
        }
        return false;
    }

    void Framework_Audio_FadeGroupVolume(int group, float targetVolume, float duration) {
        if (group >= 0 && group < AUDIO_GROUP_COUNT && duration > 0.0f) {
            g_audioGroups[group].targetVolume = fmaxf(0.0f, fminf(1.0f, targetVolume));
            g_audioGroups[group].fadeSpeed = (g_audioGroups[group].targetVolume - g_audioGroups[group].volume) / duration;
        }
    }

    // Sound with group assignment
    int Framework_Audio_LoadSound(const char* path, int group) {
        if (!path) return -1;

        Sound snd = LoadSound(path);
        if (!IsSoundValid(snd)) return -1;

        int handle = g_nextSoundHandle++;
        ManagedSound ms;
        ms.sound = snd;
        ms.group = (group >= 0 && group < AUDIO_GROUP_COUNT) ? group : AUDIO_GROUP_SFX;
        ms.baseVolume = 1.0f;
        ms.valid = true;
        g_managedSounds[handle] = ms;
        return handle;
    }

    void Framework_Audio_UnloadSound(int handle) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && it->second.valid) {
            UnloadSound(it->second.sound);
            g_managedSounds.erase(it);
        }
    }

    void Framework_Audio_PlaySound(int handle) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && it->second.valid) {
            float vol = Audio_GetEffectiveVolume(it->second.group, it->second.baseVolume);
            SetSoundVolume(it->second.sound, vol);
            PlaySound(it->second.sound);
        }
    }

    void Framework_Audio_PlaySoundEx(int handle, float volume, float pitch, float pan) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && it->second.valid) {
            float vol = Audio_GetEffectiveVolume(it->second.group, volume);
            SetSoundVolume(it->second.sound, vol);
            SetSoundPitch(it->second.sound, pitch);
            SetSoundPan(it->second.sound, pan);
            PlaySound(it->second.sound);
        }
    }

    void Framework_Audio_StopSound(int handle) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && it->second.valid) {
            StopSound(it->second.sound);
        }
    }

    void Framework_Audio_SetSoundGroup(int handle, int group) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && group >= 0 && group < AUDIO_GROUP_COUNT) {
            it->second.group = group;
        }
    }

    int Framework_Audio_GetSoundGroup(int handle) {
        auto it = g_managedSounds.find(handle);
        return (it != g_managedSounds.end()) ? it->second.group : -1;
    }

    // Spatial audio
    void Framework_Audio_SetListenerPosition(float x, float y) {
        g_listenerX = x;
        g_listenerY = y;
    }

    void Framework_Audio_GetListenerPosition(float* x, float* y) {
        if (x) *x = g_listenerX;
        if (y) *y = g_listenerY;
    }

    void Framework_Audio_PlaySoundAt(int handle, float x, float y) {
        Framework_Audio_PlaySoundAtEx(handle, x, y, 1.0f, 1.0f);
    }

    void Framework_Audio_PlaySoundAtEx(int handle, float x, float y, float volume, float pitch) {
        auto it = g_managedSounds.find(handle);
        if (it != g_managedSounds.end() && it->second.valid) {
            float spatialVol, pan;
            Audio_CalculateSpatial(x, y, spatialVol, pan);

            float finalVol = Audio_GetEffectiveVolume(it->second.group, volume * spatialVol);
            SetSoundVolume(it->second.sound, finalVol);
            SetSoundPitch(it->second.sound, pitch);
            SetSoundPan(it->second.sound, pan);
            PlaySound(it->second.sound);
        }
    }

    void Framework_Audio_SetSpatialFalloff(float minDist, float maxDist) {
        g_spatialMinDist = fmaxf(1.0f, minDist);
        g_spatialMaxDist = fmaxf(g_spatialMinDist + 1.0f, maxDist);
    }

    void Framework_Audio_SetSpatialEnabled(bool enabled) {
        g_spatialEnabled = enabled;
    }

    // Sound pooling
    int Framework_Audio_CreatePool(const char* path, int poolSize, int group) {
        if (!path || poolSize <= 0) return -1;

        SoundPool pool;
        pool.group = (group >= 0 && group < AUDIO_GROUP_COUNT) ? group : AUDIO_GROUP_SFX;
        pool.valid = true;

        for (int i = 0; i < poolSize; i++) {
            Sound snd = LoadSound(path);
            if (IsSoundValid(snd)) {
                pool.sounds.push_back(snd);
            }
        }

        if (pool.sounds.empty()) return -1;

        int handle = g_nextPoolHandle++;
        g_soundPools[handle] = pool;
        return handle;
    }

    void Framework_Audio_DestroyPool(int poolHandle) {
        auto it = g_soundPools.find(poolHandle);
        if (it != g_soundPools.end()) {
            for (auto& snd : it->second.sounds) {
                UnloadSound(snd);
            }
            g_soundPools.erase(it);
        }
    }

    void Framework_Audio_PlayFromPool(int poolHandle) {
        auto it = g_soundPools.find(poolHandle);
        if (it == g_soundPools.end() || !it->second.valid || it->second.sounds.empty()) return;

        SoundPool& pool = it->second;
        Sound& snd = pool.sounds[pool.nextIndex];

        float vol = Audio_GetEffectiveVolume(pool.group, 1.0f);
        SetSoundVolume(snd, vol);
        SetSoundPan(snd, 0.5f);
        PlaySound(snd);

        pool.nextIndex = (pool.nextIndex + 1) % (int)pool.sounds.size();
    }

    void Framework_Audio_PlayFromPoolAt(int poolHandle, float x, float y) {
        auto it = g_soundPools.find(poolHandle);
        if (it == g_soundPools.end() || !it->second.valid || it->second.sounds.empty()) return;

        SoundPool& pool = it->second;
        Sound& snd = pool.sounds[pool.nextIndex];

        float spatialVol, pan;
        Audio_CalculateSpatial(x, y, spatialVol, pan);

        float vol = Audio_GetEffectiveVolume(pool.group, spatialVol);
        SetSoundVolume(snd, vol);
        SetSoundPan(snd, pan);
        PlaySound(snd);

        pool.nextIndex = (pool.nextIndex + 1) % (int)pool.sounds.size();
    }

    void Framework_Audio_PlayFromPoolEx(int poolHandle, float volume, float pitch, float pan) {
        auto it = g_soundPools.find(poolHandle);
        if (it == g_soundPools.end() || !it->second.valid || it->second.sounds.empty()) return;

        SoundPool& pool = it->second;
        Sound& snd = pool.sounds[pool.nextIndex];

        float vol = Audio_GetEffectiveVolume(pool.group, volume);
        SetSoundVolume(snd, vol);
        SetSoundPitch(snd, pitch);
        SetSoundPan(snd, pan);
        PlaySound(snd);

        pool.nextIndex = (pool.nextIndex + 1) % (int)pool.sounds.size();
    }

    void Framework_Audio_StopPool(int poolHandle) {
        auto it = g_soundPools.find(poolHandle);
        if (it != g_soundPools.end()) {
            for (auto& snd : it->second.sounds) {
                StopSound(snd);
            }
        }
    }

    // Music with advanced features
    int Framework_Audio_LoadMusic(const char* path) {
        if (!path) return -1;

        Music mus = LoadMusicStream(path);
        if (!IsMusicValid(mus)) return -1;

        int handle = g_nextMusicHandle++;
        ManagedMusic mm;
        mm.music = mus;
        mm.baseVolume = 1.0f;
        mm.targetVolume = 1.0f;
        mm.looping = true;
        mm.valid = true;
        mm.playing = false;
        g_managedMusic[handle] = mm;
        return handle;
    }

    void Framework_Audio_UnloadMusic(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            StopMusicStream(it->second.music);
            UnloadMusicStream(it->second.music);
            g_managedMusic.erase(it);
        }
    }

    void Framework_Audio_PlayMusic(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            it->second.music.looping = it->second.looping;
            float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, it->second.baseVolume);
            SetMusicVolume(it->second.music, vol);
            PlayMusicStream(it->second.music);
            it->second.playing = true;
        }
    }

    void Framework_Audio_StopMusic(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            StopMusicStream(it->second.music);
            it->second.playing = false;
        }
    }

    void Framework_Audio_PauseMusic(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            PauseMusicStream(it->second.music);
        }
    }

    void Framework_Audio_ResumeMusic(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            ResumeMusicStream(it->second.music);
        }
    }

    void Framework_Audio_SetMusicVolume(int handle, float volume) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            it->second.baseVolume = fmaxf(0.0f, fminf(1.0f, volume));
            it->second.targetVolume = it->second.baseVolume;
            float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, it->second.baseVolume);
            SetMusicVolume(it->second.music, vol);
        }
    }

    void Framework_Audio_SetMusicPitch(int handle, float pitch) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            SetMusicPitch(it->second.music, pitch);
        }
    }

    void Framework_Audio_SetMusicLooping(int handle, bool looping) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            it->second.looping = looping;
            it->second.music.looping = looping;
        }
    }

    bool Framework_Audio_IsMusicPlaying(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            return IsMusicStreamPlaying(it->second.music);
        }
        return false;
    }

    float Framework_Audio_GetMusicLength(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            return GetMusicTimeLength(it->second.music);
        }
        return 0.0f;
    }

    float Framework_Audio_GetMusicPosition(int handle) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            return GetMusicTimePlayed(it->second.music);
        }
        return 0.0f;
    }

    void Framework_Audio_SeekMusic(int handle, float position) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            SeekMusicStream(it->second.music, position);
        }
    }

    // Music crossfading
    void Framework_Audio_CrossfadeTo(int newMusicHandle, float duration) {
        if (duration <= 0.0f) {
            // Instant switch
            if (g_crossfadeFrom >= 0) Framework_Audio_StopMusic(g_crossfadeFrom);
            Framework_Audio_PlayMusic(newMusicHandle);
            g_crossfadeFrom = -1;
            g_crossfadeTo = -1;
            return;
        }

        // Find currently playing music
        int currentPlaying = -1;
        for (auto& kv : g_managedMusic) {
            if (kv.second.valid && kv.second.playing && IsMusicStreamPlaying(kv.second.music)) {
                currentPlaying = kv.first;
                break;
            }
        }

        g_crossfadeFrom = currentPlaying;
        g_crossfadeTo = newMusicHandle;
        g_crossfadeProgress = 0.0f;
        g_crossfadeDuration = duration;

        // Start new track at zero volume
        auto it = g_managedMusic.find(newMusicHandle);
        if (it != g_managedMusic.end() && it->second.valid) {
            SetMusicVolume(it->second.music, 0.0f);
            PlayMusicStream(it->second.music);
            it->second.playing = true;
        }
    }

    void Framework_Audio_FadeOutMusic(int handle, float duration) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid && duration > 0.0f) {
            it->second.targetVolume = 0.0f;
            it->second.fadeSpeed = -it->second.baseVolume / duration;
        }
    }

    void Framework_Audio_FadeInMusic(int handle, float duration, float targetVolume) {
        auto it = g_managedMusic.find(handle);
        if (it != g_managedMusic.end() && it->second.valid) {
            it->second.baseVolume = 0.0f;
            SetMusicVolume(it->second.music, 0.0f);
            PlayMusicStream(it->second.music);
            it->second.playing = true;

            if (duration > 0.0f) {
                it->second.targetVolume = fmaxf(0.0f, fminf(1.0f, targetVolume));
                it->second.fadeSpeed = it->second.targetVolume / duration;
            }
        }
    }

    bool Framework_Audio_IsCrossfading() {
        return g_crossfadeTo >= 0;
    }

    // Playlist system
    int Framework_Audio_CreatePlaylist() {
        int handle = g_nextPlaylistHandle++;
        Playlist pl;
        pl.valid = true;
        g_playlists[handle] = pl;
        return handle;
    }

    void Framework_Audio_DestroyPlaylist(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            if (g_activePlaylist == playlistHandle) {
                g_activePlaylist = -1;
            }
            g_playlists.erase(it);
        }
    }

    void Framework_Audio_PlaylistAdd(int playlistHandle, int musicHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end() && it->second.valid) {
            it->second.tracks.push_back(musicHandle);
        }
    }

    void Framework_Audio_PlaylistRemove(int playlistHandle, int index) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end() && index >= 0 && index < (int)it->second.tracks.size()) {
            it->second.tracks.erase(it->second.tracks.begin() + index);
        }
    }

    void Framework_Audio_PlaylistClear(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            it->second.tracks.clear();
            it->second.shuffleOrder.clear();
            it->second.currentIndex = 0;
        }
    }

    void Framework_Audio_PlaylistPlay(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it == g_playlists.end() || it->second.tracks.empty()) return;

        Playlist& pl = it->second;
        pl.playing = true;
        pl.currentIndex = 0;
        g_activePlaylist = playlistHandle;

        // Generate shuffle order if needed
        if (pl.shuffle) {
            pl.shuffleOrder.resize(pl.tracks.size());
            for (size_t i = 0; i < pl.tracks.size(); i++) pl.shuffleOrder[i] = (int)i;
            for (size_t i = pl.tracks.size() - 1; i > 0; i--) {
                int j = rand() % (i + 1);
                std::swap(pl.shuffleOrder[i], pl.shuffleOrder[j]);
            }
        }

        int trackIndex = pl.shuffle ? pl.shuffleOrder[0] : 0;
        if (pl.crossfadeDuration > 0) {
            Framework_Audio_FadeInMusic(pl.tracks[trackIndex], pl.crossfadeDuration, 1.0f);
        } else {
            Framework_Audio_PlayMusic(pl.tracks[trackIndex]);
        }
    }

    void Framework_Audio_PlaylistStop(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it == g_playlists.end()) return;

        Playlist& pl = it->second;
        pl.playing = false;

        for (int trackHandle : pl.tracks) {
            Framework_Audio_StopMusic(trackHandle);
        }

        if (g_activePlaylist == playlistHandle) {
            g_activePlaylist = -1;
        }
    }

    void Framework_Audio_PlaylistNext(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it == g_playlists.end() || it->second.tracks.empty()) return;

        Playlist& pl = it->second;
        int currentTrackIndex = pl.shuffle ? pl.shuffleOrder[pl.currentIndex] : pl.currentIndex;

        pl.currentIndex++;
        if (pl.currentIndex >= (int)pl.tracks.size()) {
            if (pl.repeatMode == 1) {  // Repeat all
                pl.currentIndex = 0;
                if (pl.shuffle) {
                    // Reshuffle
                    for (size_t i = pl.tracks.size() - 1; i > 0; i--) {
                        int j = rand() % (i + 1);
                        std::swap(pl.shuffleOrder[i], pl.shuffleOrder[j]);
                    }
                }
            } else {
                pl.currentIndex = (int)pl.tracks.size() - 1;
                pl.playing = false;
                return;
            }
        }

        int newTrackIndex = pl.shuffle ? pl.shuffleOrder[pl.currentIndex] : pl.currentIndex;

        if (pl.crossfadeDuration > 0) {
            Framework_Audio_CrossfadeTo(pl.tracks[newTrackIndex], pl.crossfadeDuration);
        } else {
            Framework_Audio_StopMusic(pl.tracks[currentTrackIndex]);
            Framework_Audio_PlayMusic(pl.tracks[newTrackIndex]);
        }
    }

    void Framework_Audio_PlaylistPrev(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it == g_playlists.end() || it->second.tracks.empty()) return;

        Playlist& pl = it->second;
        int currentTrackIndex = pl.shuffle ? pl.shuffleOrder[pl.currentIndex] : pl.currentIndex;

        pl.currentIndex--;
        if (pl.currentIndex < 0) {
            if (pl.repeatMode == 1) {
                pl.currentIndex = (int)pl.tracks.size() - 1;
            } else {
                pl.currentIndex = 0;
                return;
            }
        }

        int newTrackIndex = pl.shuffle ? pl.shuffleOrder[pl.currentIndex] : pl.currentIndex;

        if (pl.crossfadeDuration > 0) {
            Framework_Audio_CrossfadeTo(pl.tracks[newTrackIndex], pl.crossfadeDuration);
        } else {
            Framework_Audio_StopMusic(pl.tracks[currentTrackIndex]);
            Framework_Audio_PlayMusic(pl.tracks[newTrackIndex]);
        }
    }

    void Framework_Audio_PlaylistSetShuffle(int playlistHandle, bool shuffle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            it->second.shuffle = shuffle;
        }
    }

    void Framework_Audio_PlaylistSetRepeat(int playlistHandle, int mode) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            it->second.repeatMode = mode;
        }
    }

    int Framework_Audio_PlaylistGetCurrent(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            return it->second.currentIndex;
        }
        return -1;
    }

    int Framework_Audio_PlaylistGetCount(int playlistHandle) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            return (int)it->second.tracks.size();
        }
        return 0;
    }

    void Framework_Audio_PlaylistSetCrossfade(int playlistHandle, float duration) {
        auto it = g_playlists.find(playlistHandle);
        if (it != g_playlists.end()) {
            it->second.crossfadeDuration = fmaxf(0.0f, duration);
        }
    }

    // Audio manager update
    void Framework_Audio_Update(float dt) {
        // Update group volume fading
        for (int i = 0; i < AUDIO_GROUP_COUNT; i++) {
            AudioGroupState& group = g_audioGroups[i];
            if (group.fadeSpeed != 0.0f) {
                group.volume += group.fadeSpeed * dt;
                if ((group.fadeSpeed > 0 && group.volume >= group.targetVolume) ||
                    (group.fadeSpeed < 0 && group.volume <= group.targetVolume)) {
                    group.volume = group.targetVolume;
                    group.fadeSpeed = 0.0f;
                }
            }
        }

        // Update music streams and fading
        for (auto& kv : g_managedMusic) {
            ManagedMusic& mm = kv.second;
            if (!mm.valid) continue;

            // Update stream
            if (mm.playing) {
                UpdateMusicStream(mm.music);
            }

            // Handle volume fading
            if (mm.fadeSpeed != 0.0f) {
                mm.baseVolume += mm.fadeSpeed * dt;
                if ((mm.fadeSpeed > 0 && mm.baseVolume >= mm.targetVolume) ||
                    (mm.fadeSpeed < 0 && mm.baseVolume <= mm.targetVolume)) {
                    mm.baseVolume = mm.targetVolume;
                    mm.fadeSpeed = 0.0f;

                    // Stop if faded to zero
                    if (mm.baseVolume <= 0.0f) {
                        StopMusicStream(mm.music);
                        mm.playing = false;
                    }
                }
                float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, mm.baseVolume);
                SetMusicVolume(mm.music, vol);
            }
        }

        // Handle crossfading
        if (g_crossfadeTo >= 0 && g_crossfadeDuration > 0.0f) {
            g_crossfadeProgress += dt;
            float t = g_crossfadeProgress / g_crossfadeDuration;

            if (t >= 1.0f) {
                // Crossfade complete
                if (g_crossfadeFrom >= 0) {
                    Framework_Audio_StopMusic(g_crossfadeFrom);
                }
                auto itTo = g_managedMusic.find(g_crossfadeTo);
                if (itTo != g_managedMusic.end()) {
                    itTo->second.baseVolume = 1.0f;
                    float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, 1.0f);
                    SetMusicVolume(itTo->second.music, vol);
                }
                g_crossfadeFrom = -1;
                g_crossfadeTo = -1;
            } else {
                // Update volumes
                if (g_crossfadeFrom >= 0) {
                    auto itFrom = g_managedMusic.find(g_crossfadeFrom);
                    if (itFrom != g_managedMusic.end()) {
                        float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, 1.0f - t);
                        SetMusicVolume(itFrom->second.music, vol);
                    }
                }
                auto itTo = g_managedMusic.find(g_crossfadeTo);
                if (itTo != g_managedMusic.end()) {
                    float vol = Audio_GetEffectiveVolume(AUDIO_GROUP_MUSIC, t);
                    SetMusicVolume(itTo->second.music, vol);
                }
            }
        }

        // Handle playlist auto-advance
        if (g_activePlaylist >= 0) {
            auto it = g_playlists.find(g_activePlaylist);
            if (it != g_playlists.end() && it->second.playing && !it->second.tracks.empty()) {
                Playlist& pl = it->second;
                int trackIndex = pl.shuffle ? pl.shuffleOrder[pl.currentIndex] : pl.currentIndex;

                if (!Framework_Audio_IsMusicPlaying(pl.tracks[trackIndex]) && !Framework_Audio_IsCrossfading()) {
                    if (pl.repeatMode == 2) {  // Repeat one
                        Framework_Audio_PlayMusic(pl.tracks[trackIndex]);
                    } else {
                        Framework_Audio_PlaylistNext(g_activePlaylist);
                    }
                }
            }
        }
    }

    // ========================================================================
    // INPUT MANAGER - Action-based Input System Implementation
    // ========================================================================

    // Binding structures
    struct KeyBinding {
        int keyCode;
    };

    struct MouseButtonBinding {
        int button;  // 0=left, 1=right, 2=middle
    };

    struct GamepadButtonBinding {
        int button;
    };

    struct AxisBinding {
        int sourceType;  // InputSourceType
        int axis;
        float scale;
    };

    // Input action
    struct InputAction {
        std::string name;
        std::vector<KeyBinding> keyBindings;
        std::vector<MouseButtonBinding> mouseBindings;
        std::vector<GamepadButtonBinding> gamepadBindings;
        std::vector<AxisBinding> axisBindings;

        float deadzone = 0.1f;
        float sensitivity = 1.0f;

        // Current frame state
        bool pressed = false;    // Just pressed
        bool down = false;       // Currently held
        bool released = false;   // Just released
        float value = 0.0f;      // Analog value
        float rawValue = 0.0f;   // Raw unprocessed

        // Previous frame state
        bool wasDown = false;

        bool valid = true;
    };

    static std::unordered_map<int, InputAction> g_inputActions;
    static std::unordered_map<std::string, int> g_actionByName;
    static int g_nextActionHandle = 1;
    static int g_activeGamepad = 0;

    // Rebinding state
    static bool g_isListening = false;
    static int g_listeningAction = -1;
    static bool g_bindingCaptured = false;
    static int g_capturedSourceType = 0;
    static int g_capturedCode = 0;

    // Vibration state
    struct VibrationState {
        float leftMotor = 0;
        float rightMotor = 0;
        float duration = 0;
        float timer = 0;
    };
    static VibrationState g_vibration[4];

    // Action management
    int Framework_Input_CreateAction(const char* name) {
        if (!name) return -1;

        std::string actionName(name);

        // Check if already exists
        auto it = g_actionByName.find(actionName);
        if (it != g_actionByName.end()) {
            return it->second;
        }

        int handle = g_nextActionHandle++;
        InputAction& action = g_inputActions[handle];
        action.name = actionName;
        action.valid = true;
        g_actionByName[actionName] = handle;

        return handle;
    }

    void Framework_Input_DestroyAction(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        if (it != g_inputActions.end()) {
            g_actionByName.erase(it->second.name);
            g_inputActions.erase(it);
        }
    }

    int Framework_Input_GetAction(const char* name) {
        if (!name) return -1;
        auto it = g_actionByName.find(std::string(name));
        return (it != g_actionByName.end()) ? it->second : -1;
    }

    bool Framework_Input_IsActionValid(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() && it->second.valid;
    }

    void Framework_Input_ClearAllActions() {
        g_inputActions.clear();
        g_actionByName.clear();
        g_nextActionHandle = 1;
    }

    // Keyboard bindings
    void Framework_Input_BindKey(int actionHandle, int keyCode) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        // Check if already bound
        for (const auto& kb : it->second.keyBindings) {
            if (kb.keyCode == keyCode) return;
        }
        it->second.keyBindings.push_back({ keyCode });
    }

    void Framework_Input_UnbindKey(int actionHandle, int keyCode) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        auto& bindings = it->second.keyBindings;
        bindings.erase(std::remove_if(bindings.begin(), bindings.end(),
            [keyCode](const KeyBinding& kb) { return kb.keyCode == keyCode; }), bindings.end());
    }

    void Framework_Input_ClearKeyBindings(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        if (it != g_inputActions.end()) {
            it->second.keyBindings.clear();
        }
    }

    // Mouse button bindings
    void Framework_Input_BindMouseButton(int actionHandle, int button) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        for (const auto& mb : it->second.mouseBindings) {
            if (mb.button == button) return;
        }
        it->second.mouseBindings.push_back({ button });
    }

    void Framework_Input_UnbindMouseButton(int actionHandle, int button) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        auto& bindings = it->second.mouseBindings;
        bindings.erase(std::remove_if(bindings.begin(), bindings.end(),
            [button](const MouseButtonBinding& mb) { return mb.button == button; }), bindings.end());
    }

    // Gamepad button bindings
    void Framework_Input_BindGamepadButton(int actionHandle, int button) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        for (const auto& gb : it->second.gamepadBindings) {
            if (gb.button == button) return;
        }
        it->second.gamepadBindings.push_back({ button });
    }

    void Framework_Input_UnbindGamepadButton(int actionHandle, int button) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        auto& bindings = it->second.gamepadBindings;
        bindings.erase(std::remove_if(bindings.begin(), bindings.end(),
            [button](const GamepadButtonBinding& gb) { return gb.button == button; }), bindings.end());
    }

    // Axis bindings
    void Framework_Input_BindMouseAxis(int actionHandle, int axis, float scale) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        it->second.axisBindings.push_back({ INPUT_SOURCE_MOUSE_AXIS, axis, scale });
    }

    void Framework_Input_BindGamepadAxis(int actionHandle, int axis, float scale) {
        auto it = g_inputActions.find(actionHandle);
        if (it == g_inputActions.end()) return;

        it->second.axisBindings.push_back({ INPUT_SOURCE_GAMEPAD_AXIS, axis, scale });
    }

    void Framework_Input_ClearAxisBindings(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        if (it != g_inputActions.end()) {
            it->second.axisBindings.clear();
        }
    }

    // Action state queries
    bool Framework_Input_IsActionPressed(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() && it->second.pressed;
    }

    bool Framework_Input_IsActionDown(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() && it->second.down;
    }

    bool Framework_Input_IsActionReleased(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() && it->second.released;
    }

    float Framework_Input_GetActionValue(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() ? it->second.value : 0.0f;
    }

    float Framework_Input_GetActionRawValue(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() ? it->second.rawValue : 0.0f;
    }

    // Action configuration
    void Framework_Input_SetActionDeadzone(int actionHandle, float deadzone) {
        auto it = g_inputActions.find(actionHandle);
        if (it != g_inputActions.end()) {
            it->second.deadzone = deadzone < 0 ? 0 : (deadzone > 1 ? 1 : deadzone);
        }
    }

    float Framework_Input_GetActionDeadzone(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() ? it->second.deadzone : 0.1f;
    }

    void Framework_Input_SetActionSensitivity(int actionHandle, float sensitivity) {
        auto it = g_inputActions.find(actionHandle);
        if (it != g_inputActions.end()) {
            it->second.sensitivity = sensitivity > 0 ? sensitivity : 1.0f;
        }
    }

    float Framework_Input_GetActionSensitivity(int actionHandle) {
        auto it = g_inputActions.find(actionHandle);
        return it != g_inputActions.end() ? it->second.sensitivity : 1.0f;
    }

    // Gamepad management
    bool Framework_Input_IsGamepadAvailable(int gamepadId) {
        return IsGamepadAvailable(gamepadId);
    }

    const char* Framework_Input_GetGamepadName(int gamepadId) {
        if (!IsGamepadAvailable(gamepadId)) return "";
        return GetGamepadName(gamepadId);
    }

    int Framework_Input_GetGamepadCount() {
        int count = 0;
        for (int i = 0; i < 4; i++) {
            if (IsGamepadAvailable(i)) count++;
        }
        return count;
    }

    void Framework_Input_SetActiveGamepad(int gamepadId) {
        g_activeGamepad = (gamepadId >= 0 && gamepadId < 4) ? gamepadId : 0;
    }

    int Framework_Input_GetActiveGamepad() {
        return g_activeGamepad;
    }

    // Direct gamepad queries
    bool Framework_Input_IsGamepadButtonPressed(int gamepadId, int button) {
        return IsGamepadAvailable(gamepadId) && IsGamepadButtonPressed(gamepadId, button);
    }

    bool Framework_Input_IsGamepadButtonDown(int gamepadId, int button) {
        return IsGamepadAvailable(gamepadId) && IsGamepadButtonDown(gamepadId, button);
    }

    bool Framework_Input_IsGamepadButtonReleased(int gamepadId, int button) {
        return IsGamepadAvailable(gamepadId) && IsGamepadButtonReleased(gamepadId, button);
    }

    float Framework_Input_GetGamepadAxisValue(int gamepadId, int axis) {
        if (!IsGamepadAvailable(gamepadId)) return 0.0f;
        return GetGamepadAxisMovement(gamepadId, axis);
    }

    // Rebinding support
    void Framework_Input_StartListening(int actionHandle) {
        if (!Framework_Input_IsActionValid(actionHandle)) return;
        g_isListening = true;
        g_listeningAction = actionHandle;
        g_bindingCaptured = false;
        g_capturedSourceType = 0;
        g_capturedCode = 0;
    }

    bool Framework_Input_IsListening() {
        return g_isListening;
    }

    void Framework_Input_StopListening() {
        g_isListening = false;
        g_listeningAction = -1;
    }

    bool Framework_Input_WasBindingCaptured() {
        return g_bindingCaptured;
    }

    int Framework_Input_GetCapturedSourceType() {
        return g_capturedSourceType;
    }

    int Framework_Input_GetCapturedCode() {
        return g_capturedCode;
    }

    // Rumble/vibration
    void Framework_Input_SetGamepadVibration(int gamepadId, float leftMotor, float rightMotor, float duration) {
        if (gamepadId < 0 || gamepadId >= 4) return;
        g_vibration[gamepadId].leftMotor = leftMotor < 0 ? 0 : (leftMotor > 1 ? 1 : leftMotor);
        g_vibration[gamepadId].rightMotor = rightMotor < 0 ? 0 : (rightMotor > 1 ? 1 : rightMotor);
        g_vibration[gamepadId].duration = duration;
        g_vibration[gamepadId].timer = duration;
        // Note: raylib doesn't have built-in vibration, this is a placeholder
        // Could be extended with platform-specific code
    }

    void Framework_Input_StopGamepadVibration(int gamepadId) {
        if (gamepadId < 0 || gamepadId >= 4) return;
        g_vibration[gamepadId].leftMotor = 0;
        g_vibration[gamepadId].rightMotor = 0;
        g_vibration[gamepadId].timer = 0;
    }

    // Input system update
    void Framework_Input_Update() {
        float dt = GetFrameTime();

        // Handle rebinding mode
        if (g_isListening) {
            // Check keyboard
            for (int key = 0; key < 350; key++) {
                if (IsKeyPressed(key)) {
                    g_capturedSourceType = INPUT_SOURCE_KEYBOARD;
                    g_capturedCode = key;
                    g_bindingCaptured = true;
                    Framework_Input_BindKey(g_listeningAction, key);
                    g_isListening = false;
                    g_listeningAction = -1;
                    return;
                }
            }

            // Check mouse buttons
            for (int btn = 0; btn < 3; btn++) {
                if (IsMouseButtonPressed(btn)) {
                    g_capturedSourceType = INPUT_SOURCE_MOUSE_BUTTON;
                    g_capturedCode = btn;
                    g_bindingCaptured = true;
                    Framework_Input_BindMouseButton(g_listeningAction, btn);
                    g_isListening = false;
                    g_listeningAction = -1;
                    return;
                }
            }

            // Check gamepad buttons
            if (IsGamepadAvailable(g_activeGamepad)) {
                for (int btn = 0; btn < 18; btn++) {
                    if (IsGamepadButtonPressed(g_activeGamepad, btn)) {
                        g_capturedSourceType = INPUT_SOURCE_GAMEPAD_BUTTON;
                        g_capturedCode = btn;
                        g_bindingCaptured = true;
                        Framework_Input_BindGamepadButton(g_listeningAction, btn);
                        g_isListening = false;
                        g_listeningAction = -1;
                        return;
                    }
                }
            }
        }

        // Update vibration timers
        for (int i = 0; i < 4; i++) {
            if (g_vibration[i].timer > 0) {
                g_vibration[i].timer -= dt;
                if (g_vibration[i].timer <= 0) {
                    g_vibration[i].leftMotor = 0;
                    g_vibration[i].rightMotor = 0;
                }
            }
        }

        // Update all actions
        for (auto& kv : g_inputActions) {
            InputAction& action = kv.second;

            // Store previous state
            action.wasDown = action.down;

            // Check digital inputs (keyboard, mouse, gamepad buttons)
            bool isDown = false;

            // Keyboard
            for (const auto& kb : action.keyBindings) {
                if (IsKeyDown(kb.keyCode)) {
                    isDown = true;
                    break;
                }
            }

            // Mouse buttons
            if (!isDown) {
                for (const auto& mb : action.mouseBindings) {
                    if (IsMouseButtonDown(mb.button)) {
                        isDown = true;
                        break;
                    }
                }
            }

            // Gamepad buttons
            if (!isDown && IsGamepadAvailable(g_activeGamepad)) {
                for (const auto& gb : action.gamepadBindings) {
                    if (IsGamepadButtonDown(g_activeGamepad, gb.button)) {
                        isDown = true;
                        break;
                    }
                }
            }

            action.down = isDown;
            action.pressed = isDown && !action.wasDown;
            action.released = !isDown && action.wasDown;

            // Calculate analog value from axis bindings
            float analogValue = 0.0f;

            for (const auto& ab : action.axisBindings) {
                float axisValue = 0.0f;

                if (ab.sourceType == INPUT_SOURCE_MOUSE_AXIS) {
                    Vector2 delta = GetMouseDelta();
                    switch (ab.axis) {
                        case MOUSE_AXIS_X: axisValue = delta.x; break;
                        case MOUSE_AXIS_Y: axisValue = delta.y; break;
                        case MOUSE_AXIS_WHEEL: axisValue = GetMouseWheelMove(); break;
                        case MOUSE_AXIS_WHEEL_H: axisValue = GetMouseWheelMoveV().x; break;
                    }
                } else if (ab.sourceType == INPUT_SOURCE_GAMEPAD_AXIS && IsGamepadAvailable(g_activeGamepad)) {
                    axisValue = GetGamepadAxisMovement(g_activeGamepad, ab.axis);
                }

                analogValue += axisValue * ab.scale;
            }

            // If digital input is active, use 1.0/-1.0
            if (isDown && fabsf(analogValue) < 0.001f) {
                analogValue = 1.0f;
            }

            action.rawValue = analogValue;

            // Apply deadzone
            if (fabsf(analogValue) < action.deadzone) {
                analogValue = 0.0f;
            } else {
                // Remap to 0-1 range after deadzone
                float sign = analogValue > 0 ? 1.0f : -1.0f;
                analogValue = sign * ((fabsf(analogValue) - action.deadzone) / (1.0f - action.deadzone));
            }

            // Apply sensitivity
            analogValue *= action.sensitivity;

            // Clamp to -1 to 1
            action.value = analogValue < -1.0f ? -1.0f : (analogValue > 1.0f ? 1.0f : analogValue);
        }
    }

    // Serialization
    bool Framework_Input_SaveBindings(const char* filename) {
        if (!filename) return false;

        std::string path = ResolveAssetPath(filename);
        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "w") != 0 || !f) return false;

        fprintf(f, "# Input Bindings\n");
        fprintf(f, "version 1\n\n");

        for (const auto& kv : g_inputActions) {
            const InputAction& action = kv.second;
            fprintf(f, "action %s\n", action.name.c_str());

            for (const auto& kb : action.keyBindings) {
                fprintf(f, "  key %d\n", kb.keyCode);
            }
            for (const auto& mb : action.mouseBindings) {
                fprintf(f, "  mouse %d\n", mb.button);
            }
            for (const auto& gb : action.gamepadBindings) {
                fprintf(f, "  gamepad %d\n", gb.button);
            }
            for (const auto& ab : action.axisBindings) {
                fprintf(f, "  axis %d %d %f\n", ab.sourceType, ab.axis, ab.scale);
            }

            fprintf(f, "  deadzone %f\n", action.deadzone);
            fprintf(f, "  sensitivity %f\n", action.sensitivity);
            fprintf(f, "end\n\n");
        }

        fclose(f);
        return true;
    }

    bool Framework_Input_LoadBindings(const char* filename) {
        if (!filename) return false;

        std::string path = ResolveAssetPath(filename);
        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "r") != 0 || !f) return false;

        char line[256];
        int currentAction = -1;

        while (fgets(line, sizeof(line), f)) {
            // Skip comments and empty lines
            if (line[0] == '#' || line[0] == '\n' || line[0] == '\r') continue;

            char arg1[128];
            int i1, i2;
            float f1;

            if (sscanf_s(line, "action %127s", arg1, (unsigned)sizeof(arg1)) == 1) {
                currentAction = Framework_Input_CreateAction(arg1);
                // Clear existing bindings
                if (currentAction != -1) {
                    auto it = g_inputActions.find(currentAction);
                    if (it != g_inputActions.end()) {
                        it->second.keyBindings.clear();
                        it->second.mouseBindings.clear();
                        it->second.gamepadBindings.clear();
                        it->second.axisBindings.clear();
                    }
                }
            } else if (sscanf_s(line, " key %d", &i1) == 1) {
                if (currentAction != -1) Framework_Input_BindKey(currentAction, i1);
            } else if (sscanf_s(line, " mouse %d", &i1) == 1) {
                if (currentAction != -1) Framework_Input_BindMouseButton(currentAction, i1);
            } else if (sscanf_s(line, " gamepad %d", &i1) == 1) {
                if (currentAction != -1) Framework_Input_BindGamepadButton(currentAction, i1);
            } else if (sscanf_s(line, " axis %d %d %f", &i1, &i2, &f1) == 3) {
                if (currentAction != -1) {
                    auto it = g_inputActions.find(currentAction);
                    if (it != g_inputActions.end()) {
                        it->second.axisBindings.push_back({ i1, i2, f1 });
                    }
                }
            } else if (sscanf_s(line, " deadzone %f", &f1) == 1) {
                if (currentAction != -1) Framework_Input_SetActionDeadzone(currentAction, f1);
            } else if (sscanf_s(line, " sensitivity %f", &f1) == 1) {
                if (currentAction != -1) Framework_Input_SetActionSensitivity(currentAction, f1);
            } else if (strstr(line, "end")) {
                currentAction = -1;
            }
        }

        fclose(f);
        return true;
    }

    // ========================================================================
    // SAVE/LOAD SYSTEM - Game State Persistence Implementation
    // ========================================================================

    // Save system state
    static std::string g_saveDirectory = "saves";
    static std::unordered_map<std::string, std::string> g_saveData;       // Current save being built/read
    static std::unordered_map<std::string, std::string> g_saveMetadata;   // Metadata for current save
    static int g_currentSaveSlot = -1;
    static bool g_isSaving = false;
    static bool g_isLoading = false;
    static std::string g_tempStringResult;  // For returning string pointers

    // Auto-save state
    static bool g_autoSaveEnabled = false;
    static float g_autoSaveInterval = 300.0f;  // 5 minutes default
    static float g_autoSaveTimer = 0.0f;
    static int g_autoSaveSlot = -1;  // -1 = rotating, else specific slot
    static int g_autoSaveRotation = 0;

    // Settings (separate from game saves)
    static std::unordered_map<std::string, std::string> g_settings;

    // Helper: Get save file path for a slot
    static std::string GetSaveFilePath(int slot) {
        return g_saveDirectory + "/save_" + std::to_string(slot) + ".sav";
    }

    static std::string GetSettingsFilePath() {
        return g_saveDirectory + "/settings.cfg";
    }

    // Helper: Ensure save directory exists (use raylib's DirectoryExists)
    static bool EnsureSaveDirectory() {
        std::string path = ResolveAssetPath(g_saveDirectory.c_str());
        // Use raylib's directory check - if not exists, create it
        if (!DirectoryExists(path.c_str())) {
            // Create directory using system call
            std::string cmd = "mkdir \"" + path + "\"";
            system(cmd.c_str());
        }
        return true;
    }

    // Helper: Check if file exists (use raylib's FileExists)
    static bool SaveFileExists(const std::string& path) {
        return ::FileExists(path.c_str());
    }

    // Save slot management
    void Framework_Save_SetDirectory(const char* directory) {
        if (directory) g_saveDirectory = directory;
    }

    const char* Framework_Save_GetDirectory() {
        return g_saveDirectory.c_str();
    }

    int Framework_Save_GetSlotCount() {
        int count = 0;
        for (int i = 0; i < 100; i++) {  // Check up to 100 slots
            if (Framework_Save_SlotExists(i)) count++;
        }
        return count;
    }

    bool Framework_Save_SlotExists(int slot) {
        std::string path = ResolveAssetPath(GetSaveFilePath(slot).c_str());
        return SaveFileExists(path);
    }

    bool Framework_Save_DeleteSlot(int slot) {
        std::string path = ResolveAssetPath(GetSaveFilePath(slot).c_str());
        return remove(path.c_str()) == 0;
    }

    bool Framework_Save_CopySlot(int fromSlot, int toSlot) {
        if (!Framework_Save_SlotExists(fromSlot)) return false;

        std::string fromPath = ResolveAssetPath(GetSaveFilePath(fromSlot).c_str());
        std::string toPath = ResolveAssetPath(GetSaveFilePath(toSlot).c_str());

        FILE* src = nullptr;
        FILE* dst = nullptr;

        if (fopen_s(&src, fromPath.c_str(), "rb") != 0 || !src) return false;
        if (fopen_s(&dst, toPath.c_str(), "wb") != 0 || !dst) {
            fclose(src);
            return false;
        }

        char buffer[4096];
        size_t bytes;
        while ((bytes = fread(buffer, 1, sizeof(buffer), src)) > 0) {
            fwrite(buffer, 1, bytes, dst);
        }

        fclose(src);
        fclose(dst);
        return true;
    }

    const char* Framework_Save_GetSlotInfo(int slot) {
        if (!Framework_Save_SlotExists(slot)) {
            g_tempStringResult = "";
            return g_tempStringResult.c_str();
        }

        // Load just metadata from slot
        std::string path = ResolveAssetPath(GetSaveFilePath(slot).c_str());
        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "r") != 0 || !f) {
            g_tempStringResult = "";
            return g_tempStringResult.c_str();
        }

        g_tempStringResult = "";
        char line[512];
        while (fgets(line, sizeof(line), f)) {
            if (strncmp(line, "[META]", 6) == 0) {
                // Read metadata section
                while (fgets(line, sizeof(line), f)) {
                    if (line[0] == '[') break;  // Next section
                    g_tempStringResult += line;
                }
                break;
            }
        }

        fclose(f);
        return g_tempStringResult.c_str();
    }

    // Save/Load operations
    bool Framework_Save_BeginSave(int slot) {
        if (g_isSaving || g_isLoading) return false;

        g_saveData.clear();
        g_saveMetadata.clear();
        g_currentSaveSlot = slot;
        g_isSaving = true;

        // Add timestamp metadata
        time_t now = time(nullptr);
        char timeBuf[64];
        ctime_s(timeBuf, sizeof(timeBuf), &now);
        timeBuf[strlen(timeBuf) - 1] = '\0';  // Remove newline
        g_saveMetadata["timestamp"] = timeBuf;

        return true;
    }

    bool Framework_Save_EndSave() {
        if (!g_isSaving) return false;

        EnsureSaveDirectory();
        std::string path = ResolveAssetPath(GetSaveFilePath(g_currentSaveSlot).c_str());

        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "w") != 0 || !f) {
            g_isSaving = false;
            return false;
        }

        fprintf(f, "# Game Save - Slot %d\n", g_currentSaveSlot);
        fprintf(f, "version 1\n\n");

        // Write metadata
        fprintf(f, "[META]\n");
        for (const auto& kv : g_saveMetadata) {
            fprintf(f, "%s=%s\n", kv.first.c_str(), kv.second.c_str());
        }
        fprintf(f, "\n");

        // Write data
        fprintf(f, "[DATA]\n");
        for (const auto& kv : g_saveData) {
            fprintf(f, "%s=%s\n", kv.first.c_str(), kv.second.c_str());
        }

        fclose(f);
        g_isSaving = false;
        g_currentSaveSlot = -1;
        return true;
    }

    bool Framework_Save_BeginLoad(int slot) {
        if (g_isSaving || g_isLoading) return false;
        if (!Framework_Save_SlotExists(slot)) return false;

        std::string path = ResolveAssetPath(GetSaveFilePath(slot).c_str());
        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "r") != 0 || !f) return false;

        g_saveData.clear();
        g_saveMetadata.clear();
        g_currentSaveSlot = slot;
        g_isLoading = true;

        char line[1024];
        bool inMeta = false;
        bool inData = false;

        while (fgets(line, sizeof(line), f)) {
            // Remove newline
            size_t len = strlen(line);
            if (len > 0 && line[len - 1] == '\n') line[len - 1] = '\0';
            if (len > 1 && line[len - 2] == '\r') line[len - 2] = '\0';

            if (line[0] == '#' || line[0] == '\0') continue;

            if (strcmp(line, "[META]") == 0) { inMeta = true; inData = false; continue; }
            if (strcmp(line, "[DATA]") == 0) { inMeta = false; inData = true; continue; }

            // Parse key=value
            char* eq = strchr(line, '=');
            if (eq) {
                *eq = '\0';
                std::string key = line;
                std::string val = eq + 1;

                if (inMeta) g_saveMetadata[key] = val;
                else if (inData) g_saveData[key] = val;
            }
        }

        fclose(f);
        return true;
    }

    bool Framework_Save_EndLoad() {
        if (!g_isLoading) return false;
        g_isLoading = false;
        g_currentSaveSlot = -1;
        return true;
    }

    // Data serialization - Write
    void Framework_Save_WriteInt(const char* key, int value) {
        if (!g_isSaving || !key) return;
        g_saveData[key] = std::to_string(value);
    }

    void Framework_Save_WriteFloat(const char* key, float value) {
        if (!g_isSaving || !key) return;
        char buf[64];
        snprintf(buf, sizeof(buf), "%.6f", value);
        g_saveData[key] = buf;
    }

    void Framework_Save_WriteBool(const char* key, bool value) {
        if (!g_isSaving || !key) return;
        g_saveData[key] = value ? "true" : "false";
    }

    void Framework_Save_WriteString(const char* key, const char* value) {
        if (!g_isSaving || !key) return;
        g_saveData[key] = value ? value : "";
    }

    void Framework_Save_WriteVector2(const char* key, float x, float y) {
        if (!g_isSaving || !key) return;
        char buf[128];
        snprintf(buf, sizeof(buf), "%.6f,%.6f", x, y);
        g_saveData[key] = buf;
    }

    void Framework_Save_WriteIntArray(const char* key, const int* values, int count) {
        if (!g_isSaving || !key || !values || count <= 0) return;
        std::string result;
        for (int i = 0; i < count; i++) {
            if (i > 0) result += ",";
            result += std::to_string(values[i]);
        }
        g_saveData[key] = result;
    }

    void Framework_Save_WriteFloatArray(const char* key, const float* values, int count) {
        if (!g_isSaving || !key || !values || count <= 0) return;
        std::string result;
        char buf[32];
        for (int i = 0; i < count; i++) {
            if (i > 0) result += ",";
            snprintf(buf, sizeof(buf), "%.6f", values[i]);
            result += buf;
        }
        g_saveData[key] = result;
    }

    // Data serialization - Read
    int Framework_Save_ReadInt(const char* key, int defaultValue) {
        if (!g_isLoading || !key) return defaultValue;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return defaultValue;
        return atoi(it->second.c_str());
    }

    float Framework_Save_ReadFloat(const char* key, float defaultValue) {
        if (!g_isLoading || !key) return defaultValue;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return defaultValue;
        return (float)atof(it->second.c_str());
    }

    bool Framework_Save_ReadBool(const char* key, bool defaultValue) {
        if (!g_isLoading || !key) return defaultValue;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return defaultValue;
        return it->second == "true" || it->second == "1";
    }

    const char* Framework_Save_ReadString(const char* key, const char* defaultValue) {
        if (!g_isLoading || !key) return defaultValue;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return defaultValue;
        g_tempStringResult = it->second;
        return g_tempStringResult.c_str();
    }

    void Framework_Save_ReadVector2(const char* key, float* x, float* y, float defX, float defY) {
        if (!g_isLoading || !key) {
            if (x) *x = defX;
            if (y) *y = defY;
            return;
        }
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) {
            if (x) *x = defX;
            if (y) *y = defY;
            return;
        }
        float fx = defX, fy = defY;
        sscanf_s(it->second.c_str(), "%f,%f", &fx, &fy);
        if (x) *x = fx;
        if (y) *y = fy;
    }

    int Framework_Save_ReadIntArray(const char* key, int* buffer, int bufferSize) {
        if (!g_isLoading || !key || !buffer || bufferSize <= 0) return 0;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return 0;

        int count = 0;
        const char* str = it->second.c_str();
        const char* p = str;

        while (*p && count < bufferSize) {
            buffer[count++] = atoi(p);
            p = strchr(p, ',');
            if (!p) break;
            p++;  // Skip comma
        }
        return count;
    }

    int Framework_Save_ReadFloatArray(const char* key, float* buffer, int bufferSize) {
        if (!g_isLoading || !key || !buffer || bufferSize <= 0) return 0;
        auto it = g_saveData.find(key);
        if (it == g_saveData.end()) return 0;

        int count = 0;
        const char* str = it->second.c_str();
        const char* p = str;

        while (*p && count < bufferSize) {
            buffer[count++] = (float)atof(p);
            p = strchr(p, ',');
            if (!p) break;
            p++;  // Skip comma
        }
        return count;
    }

    bool Framework_Save_HasKey(const char* key) {
        if (!g_isLoading || !key) return false;
        return g_saveData.find(key) != g_saveData.end();
    }

    // Metadata
    void Framework_Save_SetMetadata(const char* key, const char* value) {
        if (!g_isSaving || !key) return;
        g_saveMetadata[key] = value ? value : "";
    }

    const char* Framework_Save_GetMetadata(int slot, const char* key) {
        // Need to load the slot temporarily to get metadata
        if (!key) return "";

        std::string path = ResolveAssetPath(GetSaveFilePath(slot).c_str());
        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "r") != 0 || !f) return "";

        char line[1024];
        bool inMeta = false;
        g_tempStringResult = "";

        while (fgets(line, sizeof(line), f)) {
            size_t len = strlen(line);
            if (len > 0 && line[len - 1] == '\n') line[len - 1] = '\0';

            if (strcmp(line, "[META]") == 0) { inMeta = true; continue; }
            if (line[0] == '[') { inMeta = false; continue; }

            if (inMeta) {
                char* eq = strchr(line, '=');
                if (eq) {
                    *eq = '\0';
                    if (strcmp(line, key) == 0) {
                        g_tempStringResult = eq + 1;
                        break;
                    }
                }
            }
        }

        fclose(f);
        return g_tempStringResult.c_str();
    }

    // Auto-save
    void Framework_Save_SetAutoSaveEnabled(bool enabled) {
        g_autoSaveEnabled = enabled;
        g_autoSaveTimer = 0;
    }

    bool Framework_Save_IsAutoSaveEnabled() {
        return g_autoSaveEnabled;
    }

    void Framework_Save_SetAutoSaveInterval(float seconds) {
        g_autoSaveInterval = seconds > 1.0f ? seconds : 1.0f;
    }

    float Framework_Save_GetAutoSaveInterval() {
        return g_autoSaveInterval;
    }

    void Framework_Save_SetAutoSaveSlot(int slot) {
        g_autoSaveSlot = slot;
    }

    int Framework_Save_GetAutoSaveSlot() {
        return g_autoSaveSlot;
    }

    void Framework_Save_TriggerAutoSave() {
        int slot = g_autoSaveSlot;
        if (slot < 0) {
            // Rotating slots: use 90-99 for auto-saves
            slot = 90 + (g_autoSaveRotation % 10);
            g_autoSaveRotation++;
        }

        // Quick auto-save - just mark that auto-save should happen
        // Game code needs to handle the actual save in their update loop
        if (Framework_Save_BeginSave(slot)) {
            Framework_Save_SetMetadata("type", "autosave");
            // Note: Game code must call WriteXxx functions and EndSave
        }
    }

    void Framework_Save_Update(float dt) {
        if (!g_autoSaveEnabled) return;

        g_autoSaveTimer += dt;
        if (g_autoSaveTimer >= g_autoSaveInterval) {
            g_autoSaveTimer = 0;
            Framework_Save_TriggerAutoSave();
        }
    }

    // Quick save/load
    bool Framework_Save_QuickSave() {
        return Framework_Save_BeginSave(0);
        // Note: Caller must write data and call EndSave
    }

    bool Framework_Save_QuickLoad() {
        return Framework_Save_BeginLoad(0);
        // Note: Caller must read data and call EndLoad
    }

    // Settings (persistent across sessions)
    void Framework_Settings_SetInt(const char* key, int value) {
        if (!key) return;
        g_settings[key] = std::to_string(value);
    }

    int Framework_Settings_GetInt(const char* key, int defaultValue) {
        if (!key) return defaultValue;
        auto it = g_settings.find(key);
        if (it == g_settings.end()) return defaultValue;
        return atoi(it->second.c_str());
    }

    void Framework_Settings_SetFloat(const char* key, float value) {
        if (!key) return;
        char buf[64];
        snprintf(buf, sizeof(buf), "%.6f", value);
        g_settings[key] = buf;
    }

    float Framework_Settings_GetFloat(const char* key, float defaultValue) {
        if (!key) return defaultValue;
        auto it = g_settings.find(key);
        if (it == g_settings.end()) return defaultValue;
        return (float)atof(it->second.c_str());
    }

    void Framework_Settings_SetBool(const char* key, bool value) {
        if (!key) return;
        g_settings[key] = value ? "true" : "false";
    }

    bool Framework_Settings_GetBool(const char* key, bool defaultValue) {
        if (!key) return defaultValue;
        auto it = g_settings.find(key);
        if (it == g_settings.end()) return defaultValue;
        return it->second == "true" || it->second == "1";
    }

    void Framework_Settings_SetString(const char* key, const char* value) {
        if (!key) return;
        g_settings[key] = value ? value : "";
    }

    const char* Framework_Settings_GetString(const char* key, const char* defaultValue) {
        if (!key) return defaultValue;
        auto it = g_settings.find(key);
        if (it == g_settings.end()) return defaultValue;
        g_tempStringResult = it->second;
        return g_tempStringResult.c_str();
    }

    bool Framework_Settings_Save() {
        EnsureSaveDirectory();
        std::string path = ResolveAssetPath(GetSettingsFilePath().c_str());

        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "w") != 0 || !f) return false;

        fprintf(f, "# Game Settings\n");
        fprintf(f, "version 1\n\n");

        for (const auto& kv : g_settings) {
            fprintf(f, "%s=%s\n", kv.first.c_str(), kv.second.c_str());
        }

        fclose(f);
        return true;
    }

    bool Framework_Settings_Load() {
        std::string path = ResolveAssetPath(GetSettingsFilePath().c_str());

        FILE* f = nullptr;
        if (fopen_s(&f, path.c_str(), "r") != 0 || !f) return false;

        g_settings.clear();
        char line[1024];

        while (fgets(line, sizeof(line), f)) {
            size_t len = strlen(line);
            if (len > 0 && line[len - 1] == '\n') line[len - 1] = '\0';
            if (len > 1 && line[len - 2] == '\r') line[len - 2] = '\0';

            if (line[0] == '#' || line[0] == '\0') continue;
            if (strncmp(line, "version", 7) == 0) continue;

            char* eq = strchr(line, '=');
            if (eq) {
                *eq = '\0';
                g_settings[line] = eq + 1;
            }
        }

        fclose(f);
        return true;
    }

    void Framework_Settings_Clear() {
        g_settings.clear();
    }

    // ========================================================================
    // TWEENING SYSTEM - Property Animation & Interpolation
    // ========================================================================
}  // Temporarily close extern "C" for namespace

namespace {
    // Tween type enumeration
    enum TweenType {
        TWEEN_TYPE_FLOAT,
        TWEEN_TYPE_VECTOR2,
        TWEEN_TYPE_COLOR
    };

    // Tween data structure
    struct Tween {
        int id = 0;
        TweenType type = TWEEN_TYPE_FLOAT;
        TweenState state = TWEEN_STATE_IDLE;
        TweenEasing easing = TWEEN_LINEAR;
        TweenLoopMode loopMode = TWEEN_LOOP_NONE;

        // Timing
        float duration = 1.0f;
        float elapsed = 0.0f;
        float delay = 0.0f;
        float delayElapsed = 0.0f;
        float timeScale = 1.0f;

        // Loop
        int loopCount = 0;       // 0 = once, -1 = infinite
        int currentLoop = 0;
        bool yoyoReverse = false;

        // Values
        float fromFloat = 0, toFloat = 0, currentFloat = 0;
        float fromX = 0, fromY = 0, toX = 0, toY = 0, currentX = 0, currentY = 0;
        unsigned char fromR = 0, fromG = 0, fromB = 0, fromA = 0;
        unsigned char toR = 0, toG = 0, toB = 0, toA = 0;
        unsigned char currentR = 0, currentG = 0, currentB = 0, currentA = 0;

        // Target pointers (for direct tweening)
        float* targetFloat = nullptr;
        float* targetX = nullptr;
        float* targetY = nullptr;

        // Entity target (for convenience tweens)
        int targetEntity = -1;

        // Callbacks
        TweenCallback onStart = nullptr;
        TweenUpdateCallback onUpdate = nullptr;
        TweenCallback onComplete = nullptr;
        TweenCallback onLoop = nullptr;
        TweenCallback onKill = nullptr;

        // Options
        bool autoKill = true;
        bool started = false;
    };

    // Sequence entry
    struct SequenceEntry {
        int tweenId = -1;
        float startTime = 0;
        TweenCallback callback = nullptr;
        bool isCallback = false;
        bool isDelay = false;
        float delayDuration = 0;
    };

    // Sequence structure
    struct TweenSequence {
        int id = 0;
        std::vector<SequenceEntry> entries;
        float duration = 0;
        float elapsed = 0;
        TweenState state = TWEEN_STATE_IDLE;
        bool autoKill = true;
    };

    // Global tween state
    std::unordered_map<int, Tween> g_tweens;
    std::unordered_map<int, TweenSequence> g_sequences;
    int g_nextTweenId = 1;
    int g_nextSequenceId = 1;
    float g_globalTweenTimeScale = 1.0f;
    bool g_tweensPaused = false;

    // Extended easing function
    const float TWEEN_PI = 3.14159265358979323846f;

    float ApplyTweenEasing(float t, TweenEasing easing) {
        const float c1 = 1.70158f;
        const float c2 = c1 * 1.525f;
        const float c3 = c1 + 1.0f;
        const float c4 = (2.0f * TWEEN_PI) / 3.0f;
        const float c5 = (2.0f * TWEEN_PI) / 4.5f;

        switch (easing) {
            case TWEEN_LINEAR: return t;
            case TWEEN_IN_QUAD: return t * t;
            case TWEEN_OUT_QUAD: return 1.0f - (1.0f - t) * (1.0f - t);
            case TWEEN_IN_OUT_QUAD: return t < 0.5f ? 2.0f * t * t : 1.0f - powf(-2.0f * t + 2.0f, 2.0f) / 2.0f;
            case TWEEN_IN_CUBIC: return t * t * t;
            case TWEEN_OUT_CUBIC: return 1.0f - powf(1.0f - t, 3.0f);
            case TWEEN_IN_OUT_CUBIC: return t < 0.5f ? 4.0f * t * t * t : 1.0f - powf(-2.0f * t + 2.0f, 3.0f) / 2.0f;
            case TWEEN_IN_EXPO: return t == 0.0f ? 0.0f : powf(2.0f, 10.0f * t - 10.0f);
            case TWEEN_OUT_EXPO: return t == 1.0f ? 1.0f : 1.0f - powf(2.0f, -10.0f * t);
            case TWEEN_IN_OUT_EXPO:
                if (t == 0.0f) return 0.0f;
                if (t == 1.0f) return 1.0f;
                return t < 0.5f ? powf(2.0f, 20.0f * t - 10.0f) / 2.0f : (2.0f - powf(2.0f, -20.0f * t + 10.0f)) / 2.0f;
            case TWEEN_IN_SINE: return 1.0f - cosf((t * TWEEN_PI) / 2.0f);
            case TWEEN_OUT_SINE: return sinf((t * TWEEN_PI) / 2.0f);
            case TWEEN_IN_OUT_SINE: return -(cosf(TWEEN_PI * t) - 1.0f) / 2.0f;
            case TWEEN_IN_BACK: return c3 * t * t * t - c1 * t * t;
            case TWEEN_OUT_BACK: return 1.0f + c3 * powf(t - 1.0f, 3.0f) + c1 * powf(t - 1.0f, 2.0f);
            case TWEEN_IN_OUT_BACK:
                return t < 0.5f
                    ? (powf(2.0f * t, 2.0f) * ((c2 + 1.0f) * 2.0f * t - c2)) / 2.0f
                    : (powf(2.0f * t - 2.0f, 2.0f) * ((c2 + 1.0f) * (t * 2.0f - 2.0f) + c2) + 2.0f) / 2.0f;
            case TWEEN_IN_ELASTIC:
                if (t == 0.0f) return 0.0f;
                if (t == 1.0f) return 1.0f;
                return -powf(2.0f, 10.0f * t - 10.0f) * sinf((t * 10.0f - 10.75f) * c4);
            case TWEEN_OUT_ELASTIC:
                if (t == 0.0f) return 0.0f;
                if (t == 1.0f) return 1.0f;
                return powf(2.0f, -10.0f * t) * sinf((t * 10.0f - 0.75f) * c4) + 1.0f;
            case TWEEN_IN_OUT_ELASTIC:
                if (t == 0.0f) return 0.0f;
                if (t == 1.0f) return 1.0f;
                return t < 0.5f
                    ? -(powf(2.0f, 20.0f * t - 10.0f) * sinf((20.0f * t - 11.125f) * c5)) / 2.0f
                    : (powf(2.0f, -20.0f * t + 10.0f) * sinf((20.0f * t - 11.125f) * c5)) / 2.0f + 1.0f;
            case TWEEN_IN_BOUNCE: return 1.0f - ApplyTweenEasing(1.0f - t, TWEEN_OUT_BOUNCE);
            case TWEEN_OUT_BOUNCE: {
                const float n1 = 7.5625f;
                const float d1 = 2.75f;
                if (t < 1.0f / d1) return n1 * t * t;
                if (t < 2.0f / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
                if (t < 2.5f / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
                return n1 * (t -= 2.625f / d1) * t + 0.984375f;
            }
            case TWEEN_IN_OUT_BOUNCE:
                return t < 0.5f
                    ? (1.0f - ApplyTweenEasing(1.0f - 2.0f * t, TWEEN_OUT_BOUNCE)) / 2.0f
                    : (1.0f + ApplyTweenEasing(2.0f * t - 1.0f, TWEEN_OUT_BOUNCE)) / 2.0f;
            default: return t;
        }
    }

    // Update a single tween
    void UpdateTween(Tween& tw, float dt) {
        if (tw.state != TWEEN_STATE_PLAYING) return;

        // Handle delay
        if (tw.delayElapsed < tw.delay) {
            tw.delayElapsed += dt;
            if (tw.delayElapsed < tw.delay) return;
            // Start callback
            if (!tw.started && tw.onStart) {
                tw.started = true;
                tw.onStart(tw.id);
            }
        }

        if (!tw.started && tw.onStart) {
            tw.started = true;
            tw.onStart(tw.id);
        }

        // Update elapsed time
        tw.elapsed += dt * tw.timeScale;

        // Calculate progress
        float progress = tw.duration > 0.0f ? tw.elapsed / tw.duration : 1.0f;
        if (progress > 1.0f) progress = 1.0f;

        // Handle yoyo reverse
        float easedProgress = progress;
        if (tw.yoyoReverse) {
            easedProgress = ApplyTweenEasing(1.0f - progress, tw.easing);
        } else {
            easedProgress = ApplyTweenEasing(progress, tw.easing);
        }

        // Update values based on type
        switch (tw.type) {
            case TWEEN_TYPE_FLOAT:
                tw.currentFloat = tw.fromFloat + (tw.toFloat - tw.fromFloat) * easedProgress;
                if (tw.targetFloat) *tw.targetFloat = tw.currentFloat;
                break;
            case TWEEN_TYPE_VECTOR2:
                tw.currentX = tw.fromX + (tw.toX - tw.fromX) * easedProgress;
                tw.currentY = tw.fromY + (tw.toY - tw.fromY) * easedProgress;
                if (tw.targetX) *tw.targetX = tw.currentX;
                if (tw.targetY) *tw.targetY = tw.currentY;
                break;
            case TWEEN_TYPE_COLOR:
                tw.currentR = (unsigned char)(tw.fromR + (tw.toR - tw.fromR) * easedProgress);
                tw.currentG = (unsigned char)(tw.fromG + (tw.toG - tw.fromG) * easedProgress);
                tw.currentB = (unsigned char)(tw.fromB + (tw.toB - tw.fromB) * easedProgress);
                tw.currentA = (unsigned char)(tw.fromA + (tw.toA - tw.fromA) * easedProgress);
                break;
        }

        // Update callback
        if (tw.onUpdate) {
            tw.onUpdate(tw.id, tw.currentFloat);
        }

        // Check completion
        if (progress >= 1.0f) {
            // Handle looping
            bool shouldLoop = false;
            if (tw.loopCount < 0 || tw.currentLoop < tw.loopCount) {
                shouldLoop = true;
            }

            if (shouldLoop && tw.loopMode != TWEEN_LOOP_NONE) {
                tw.currentLoop++;
                tw.elapsed = 0.0f;

                if (tw.onLoop) tw.onLoop(tw.id);

                if (tw.loopMode == TWEEN_LOOP_YOYO) {
                    tw.yoyoReverse = !tw.yoyoReverse;
                }
                else if (tw.loopMode == TWEEN_LOOP_INCREMENT) {
                    // Shift values for incremental loops
                    float delta = tw.toFloat - tw.fromFloat;
                    tw.fromFloat = tw.toFloat;
                    tw.toFloat += delta;

                    float deltaX = tw.toX - tw.fromX;
                    float deltaY = tw.toY - tw.fromY;
                    tw.fromX = tw.toX;
                    tw.fromY = tw.toY;
                    tw.toX += deltaX;
                    tw.toY += deltaY;
                }
            }
            else {
                tw.state = TWEEN_STATE_COMPLETED;
                if (tw.onComplete) tw.onComplete(tw.id);
            }
        }
    }

    Tween* GetTween(int id) {
        auto it = g_tweens.find(id);
        return it != g_tweens.end() ? &it->second : nullptr;
    }

    TweenSequence* GetSequence(int id) {
        auto it = g_sequences.find(id);
        return it != g_sequences.end() ? &it->second : nullptr;
    }
}

// Forward declarations for ECS functions used by entity tweens
extern "C" {
    bool Framework_Ecs_IsAlive(int entity);
    bool Framework_Ecs_HasTransform2D(int entity);
    Vector2 Framework_Ecs_GetTransformPosition(int entity);
    void Framework_Ecs_SetTransformPosition(int entity, float x, float y);
    float Framework_Ecs_GetTransformRotation(int entity);
    void Framework_Ecs_SetTransformRotation(int entity, float rotation);
    Vector2 Framework_Ecs_GetTransformScale(int entity);
    void Framework_Ecs_SetTransformScale(int entity, float sx, float sy);
    bool Framework_Ecs_HasSprite2D(int entity);
    void Framework_Ecs_SetSpriteTint(int entity, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
}

extern "C" {
    // Float tweens
    int Framework_Tween_Float(float from, float to, float duration, int easing) {
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_FLOAT;
        tw.fromFloat = from;
        tw.toFloat = to;
        tw.currentFloat = from;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    int Framework_Tween_FloatTo(float* target, float to, float duration, int easing) {
        if (!target) return -1;
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_FLOAT;
        tw.targetFloat = target;
        tw.fromFloat = *target;
        tw.toFloat = to;
        tw.currentFloat = *target;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    int Framework_Tween_FloatFromTo(float* target, float from, float to, float duration, int easing) {
        if (!target) return -1;
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_FLOAT;
        tw.targetFloat = target;
        tw.fromFloat = from;
        tw.toFloat = to;
        tw.currentFloat = from;
        *target = from;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    // Vector2 tweens
    int Framework_Tween_Vector2(float fromX, float fromY, float toX, float toY, float duration, int easing) {
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_VECTOR2;
        tw.fromX = fromX; tw.fromY = fromY;
        tw.toX = toX; tw.toY = toY;
        tw.currentX = fromX; tw.currentY = fromY;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    int Framework_Tween_Vector2To(float* targetX, float* targetY, float toX, float toY, float duration, int easing) {
        if (!targetX || !targetY) return -1;
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_VECTOR2;
        tw.targetX = targetX;
        tw.targetY = targetY;
        tw.fromX = *targetX; tw.fromY = *targetY;
        tw.toX = toX; tw.toY = toY;
        tw.currentX = *targetX; tw.currentY = *targetY;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    // Color tweens
    int Framework_Tween_Color(unsigned char fromR, unsigned char fromG, unsigned char fromB, unsigned char fromA,
                              unsigned char toR, unsigned char toG, unsigned char toB, unsigned char toA,
                              float duration, int easing) {
        Tween tw;
        tw.id = g_nextTweenId++;
        tw.type = TWEEN_TYPE_COLOR;
        tw.fromR = fromR; tw.fromG = fromG; tw.fromB = fromB; tw.fromA = fromA;
        tw.toR = toR; tw.toG = toG; tw.toB = toB; tw.toA = toA;
        tw.currentR = fromR; tw.currentG = fromG; tw.currentB = fromB; tw.currentA = fromA;
        tw.duration = duration;
        tw.easing = (TweenEasing)easing;
        tw.state = TWEEN_STATE_PLAYING;
        g_tweens[tw.id] = tw;
        return tw.id;
    }

    // Tween control
    void Framework_Tween_Play(int tweenId) {
        if (auto* tw = GetTween(tweenId)) tw->state = TWEEN_STATE_PLAYING;
    }

    void Framework_Tween_Pause(int tweenId) {
        if (auto* tw = GetTween(tweenId)) {
            if (tw->state == TWEEN_STATE_PLAYING) tw->state = TWEEN_STATE_PAUSED;
        }
    }

    void Framework_Tween_Resume(int tweenId) {
        if (auto* tw = GetTween(tweenId)) {
            if (tw->state == TWEEN_STATE_PAUSED) tw->state = TWEEN_STATE_PLAYING;
        }
    }

    void Framework_Tween_Stop(int tweenId) {
        if (auto* tw = GetTween(tweenId)) tw->state = TWEEN_STATE_IDLE;
    }

    void Framework_Tween_Restart(int tweenId) {
        if (auto* tw = GetTween(tweenId)) {
            tw->elapsed = 0.0f;
            tw->delayElapsed = 0.0f;
            tw->currentLoop = 0;
            tw->yoyoReverse = false;
            tw->started = false;
            tw->state = TWEEN_STATE_PLAYING;
        }
    }

    void Framework_Tween_Kill(int tweenId) {
        auto it = g_tweens.find(tweenId);
        if (it != g_tweens.end()) {
            if (it->second.onKill) it->second.onKill(tweenId);
            g_tweens.erase(it);
        }
    }

    void Framework_Tween_Complete(int tweenId) {
        if (auto* tw = GetTween(tweenId)) {
            tw->elapsed = tw->duration;
            UpdateTween(*tw, 0);  // Force final update
        }
    }

    // Tween state queries
    bool Framework_Tween_IsValid(int tweenId) {
        return g_tweens.find(tweenId) != g_tweens.end();
    }

    int Framework_Tween_GetState(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return (int)tw->state;
        return (int)TWEEN_STATE_IDLE;
    }

    bool Framework_Tween_IsPlaying(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->state == TWEEN_STATE_PLAYING;
        return false;
    }

    bool Framework_Tween_IsPaused(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->state == TWEEN_STATE_PAUSED;
        return false;
    }

    bool Framework_Tween_IsCompleted(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->state == TWEEN_STATE_COMPLETED;
        return false;
    }

    float Framework_Tween_GetProgress(int tweenId) {
        if (auto* tw = GetTween(tweenId)) {
            return tw->duration > 0 ? tw->elapsed / tw->duration : 1.0f;
        }
        return 0.0f;
    }

    float Framework_Tween_GetElapsed(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->elapsed;
        return 0.0f;
    }

    float Framework_Tween_GetDuration(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->duration;
        return 0.0f;
    }

    // Tween value getters
    float Framework_Tween_GetFloat(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->currentFloat;
        return 0.0f;
    }

    void Framework_Tween_GetVector2(int tweenId, float* x, float* y) {
        if (auto* tw = GetTween(tweenId)) {
            if (x) *x = tw->currentX;
            if (y) *y = tw->currentY;
        }
    }

    void Framework_Tween_GetColor(int tweenId, unsigned char* r, unsigned char* g, unsigned char* b, unsigned char* a) {
        if (auto* tw = GetTween(tweenId)) {
            if (r) *r = tw->currentR;
            if (g) *g = tw->currentG;
            if (b) *b = tw->currentB;
            if (a) *a = tw->currentA;
        }
    }

    // Tween configuration
    void Framework_Tween_SetDelay(int tweenId, float delay) {
        if (auto* tw = GetTween(tweenId)) tw->delay = delay;
    }

    float Framework_Tween_GetDelay(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->delay;
        return 0.0f;
    }

    void Framework_Tween_SetLoopMode(int tweenId, int loopMode) {
        if (auto* tw = GetTween(tweenId)) tw->loopMode = (TweenLoopMode)loopMode;
    }

    int Framework_Tween_GetLoopMode(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return (int)tw->loopMode;
        return (int)TWEEN_LOOP_NONE;
    }

    void Framework_Tween_SetLoopCount(int tweenId, int count) {
        if (auto* tw = GetTween(tweenId)) tw->loopCount = count;
    }

    int Framework_Tween_GetLoopCount(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->loopCount;
        return 0;
    }

    int Framework_Tween_GetCurrentLoop(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->currentLoop;
        return 0;
    }

    void Framework_Tween_SetTimeScale(int tweenId, float scale) {
        if (auto* tw = GetTween(tweenId)) tw->timeScale = scale;
    }

    float Framework_Tween_GetTimeScale(int tweenId) {
        if (auto* tw = GetTween(tweenId)) return tw->timeScale;
        return 1.0f;
    }

    void Framework_Tween_SetAutoKill(int tweenId, bool autoKill) {
        if (auto* tw = GetTween(tweenId)) tw->autoKill = autoKill;
    }

    // Tween callbacks
    void Framework_Tween_SetOnStart(int tweenId, TweenCallback callback) {
        if (auto* tw = GetTween(tweenId)) tw->onStart = callback;
    }

    void Framework_Tween_SetOnUpdate(int tweenId, TweenUpdateCallback callback) {
        if (auto* tw = GetTween(tweenId)) tw->onUpdate = callback;
    }

    void Framework_Tween_SetOnComplete(int tweenId, TweenCallback callback) {
        if (auto* tw = GetTween(tweenId)) tw->onComplete = callback;
    }

    void Framework_Tween_SetOnLoop(int tweenId, TweenCallback callback) {
        if (auto* tw = GetTween(tweenId)) tw->onLoop = callback;
    }

    void Framework_Tween_SetOnKill(int tweenId, TweenCallback callback) {
        if (auto* tw = GetTween(tweenId)) tw->onKill = callback;
    }

    // Sequence building
    int Framework_Tween_CreateSequence() {
        TweenSequence seq;
        seq.id = g_nextSequenceId++;
        g_sequences[seq.id] = seq;
        return seq.id;
    }

    void Framework_Tween_SequenceAppend(int seqId, int tweenId) {
        auto* seq = GetSequence(seqId);
        auto* tw = GetTween(tweenId);
        if (!seq || !tw) return;

        SequenceEntry entry;
        entry.tweenId = tweenId;
        entry.startTime = seq->duration;
        seq->entries.push_back(entry);
        seq->duration += tw->duration + tw->delay;

        // Pause the tween until sequence starts
        tw->state = TWEEN_STATE_PAUSED;
    }

    void Framework_Tween_SequenceJoin(int seqId, int tweenId) {
        auto* seq = GetSequence(seqId);
        auto* tw = GetTween(tweenId);
        if (!seq || !tw || seq->entries.empty()) return;

        // Find start time of last entry
        float lastStart = seq->entries.back().startTime;

        SequenceEntry entry;
        entry.tweenId = tweenId;
        entry.startTime = lastStart;
        seq->entries.push_back(entry);

        // Extend duration if this tween is longer
        float entryEnd = lastStart + tw->duration + tw->delay;
        if (entryEnd > seq->duration) seq->duration = entryEnd;

        tw->state = TWEEN_STATE_PAUSED;
    }

    void Framework_Tween_SequenceInsert(int seqId, float atTime, int tweenId) {
        auto* seq = GetSequence(seqId);
        auto* tw = GetTween(tweenId);
        if (!seq || !tw) return;

        SequenceEntry entry;
        entry.tweenId = tweenId;
        entry.startTime = atTime;
        seq->entries.push_back(entry);

        float entryEnd = atTime + tw->duration + tw->delay;
        if (entryEnd > seq->duration) seq->duration = entryEnd;

        tw->state = TWEEN_STATE_PAUSED;
    }

    void Framework_Tween_SequenceAppendDelay(int seqId, float delay) {
        auto* seq = GetSequence(seqId);
        if (!seq) return;

        SequenceEntry entry;
        entry.isDelay = true;
        entry.delayDuration = delay;
        entry.startTime = seq->duration;
        seq->entries.push_back(entry);
        seq->duration += delay;
    }

    void Framework_Tween_SequenceAppendCallback(int seqId, TweenCallback callback) {
        auto* seq = GetSequence(seqId);
        if (!seq) return;

        SequenceEntry entry;
        entry.isCallback = true;
        entry.callback = callback;
        entry.startTime = seq->duration;
        seq->entries.push_back(entry);
    }

    void Framework_Tween_PlaySequence(int seqId) {
        auto* seq = GetSequence(seqId);
        if (!seq) return;
        seq->state = TWEEN_STATE_PLAYING;
        seq->elapsed = 0;
    }

    void Framework_Tween_PauseSequence(int seqId) {
        auto* seq = GetSequence(seqId);
        if (!seq) return;
        if (seq->state == TWEEN_STATE_PLAYING) seq->state = TWEEN_STATE_PAUSED;
    }

    void Framework_Tween_StopSequence(int seqId) {
        auto* seq = GetSequence(seqId);
        if (!seq) return;
        seq->state = TWEEN_STATE_IDLE;
    }

    void Framework_Tween_KillSequence(int seqId) {
        auto it = g_sequences.find(seqId);
        if (it != g_sequences.end()) {
            // Kill all tweens in sequence
            for (auto& entry : it->second.entries) {
                if (entry.tweenId >= 0) {
                    Framework_Tween_Kill(entry.tweenId);
                }
            }
            g_sequences.erase(it);
        }
    }

    bool Framework_Tween_IsSequenceValid(int seqId) {
        return g_sequences.find(seqId) != g_sequences.end();
    }

    bool Framework_Tween_IsSequencePlaying(int seqId) {
        auto* seq = GetSequence(seqId);
        return seq && seq->state == TWEEN_STATE_PLAYING;
    }

    float Framework_Tween_GetSequenceDuration(int seqId) {
        auto* seq = GetSequence(seqId);
        return seq ? seq->duration : 0.0f;
    }

    // Entity property tweens
    int Framework_Tween_EntityPosition(int entity, float toX, float toY, float duration, int easing) {
        if (!Framework_Ecs_HasTransform2D(entity)) return -1;

        Vector2 pos = Framework_Ecs_GetTransformPosition(entity);

        int tweenId = Framework_Tween_Vector2(pos.x, pos.y, toX, toY, duration, easing);
        if (auto* tw = GetTween(tweenId)) {
            tw->targetEntity = entity;
        }
        return tweenId;
    }

    int Framework_Tween_EntityRotation(int entity, float toRotation, float duration, int easing) {
        if (!Framework_Ecs_HasTransform2D(entity)) return -1;

        float rot = Framework_Ecs_GetTransformRotation(entity);
        int tweenId = Framework_Tween_Float(rot, toRotation, duration, easing);
        if (auto* tw = GetTween(tweenId)) {
            tw->targetEntity = entity;
        }
        return tweenId;
    }

    int Framework_Tween_EntityScale(int entity, float toScaleX, float toScaleY, float duration, int easing) {
        if (!Framework_Ecs_HasTransform2D(entity)) return -1;

        Vector2 scale = Framework_Ecs_GetTransformScale(entity);

        int tweenId = Framework_Tween_Vector2(scale.x, scale.y, toScaleX, toScaleY, duration, easing);
        if (auto* tw = GetTween(tweenId)) {
            tw->targetEntity = entity;
        }
        return tweenId;
    }

    int Framework_Tween_EntityAlpha(int entity, unsigned char toAlpha, float duration, int easing) {
        if (!Framework_Ecs_HasSprite2D(entity)) return -1;

        // Get current alpha from sprite component directly
        auto it = g_sprite2D.find(entity);
        if (it == g_sprite2D.end()) return -1;
        unsigned char a = it->second.tint.a;

        int tweenId = Framework_Tween_Float((float)a, (float)toAlpha, duration, easing);
        if (auto* tw = GetTween(tweenId)) {
            tw->targetEntity = entity;
        }
        return tweenId;
    }

    // Global tween management
    void Framework_Tween_Update(float dt) {
        if (g_tweensPaused) return;

        float scaledDt = dt * g_globalTweenTimeScale;

        // Update all tweens
        std::vector<int> toRemove;
        for (auto& pair : g_tweens) {
            UpdateTween(pair.second, scaledDt);

            // Update entity properties if applicable
            Tween& tw = pair.second;
            if (tw.targetEntity >= 0 && Framework_Ecs_IsAlive(tw.targetEntity)) {
                if (tw.type == TWEEN_TYPE_VECTOR2 && Framework_Ecs_HasTransform2D(tw.targetEntity)) {
                    Framework_Ecs_SetTransformPosition(tw.targetEntity, tw.currentX, tw.currentY);
                }
                else if (tw.type == TWEEN_TYPE_FLOAT) {
                    // Could be rotation or alpha
                    if (Framework_Ecs_HasTransform2D(tw.targetEntity)) {
                        Framework_Ecs_SetTransformRotation(tw.targetEntity, tw.currentFloat);
                    }
                }
            }

            if (tw.state == TWEEN_STATE_COMPLETED && tw.autoKill) {
                toRemove.push_back(pair.first);
            }
        }

        // Remove completed auto-kill tweens
        for (int id : toRemove) {
            g_tweens.erase(id);
        }

        // Update sequences
        std::vector<int> seqToRemove;
        for (auto& pair : g_sequences) {
            TweenSequence& seq = pair.second;
            if (seq.state != TWEEN_STATE_PLAYING) continue;

            float prevElapsed = seq.elapsed;
            seq.elapsed += scaledDt;

            // Check entries that should start
            for (auto& entry : seq.entries) {
                if (entry.startTime >= prevElapsed && entry.startTime < seq.elapsed) {
                    if (entry.isCallback && entry.callback) {
                        entry.callback(seq.id);
                    }
                    else if (entry.tweenId >= 0) {
                        Framework_Tween_Play(entry.tweenId);
                    }
                }
            }

            if (seq.elapsed >= seq.duration) {
                seq.state = TWEEN_STATE_COMPLETED;
                if (seq.autoKill) seqToRemove.push_back(pair.first);
            }
        }

        for (int id : seqToRemove) {
            Framework_Tween_KillSequence(id);
        }
    }

    void Framework_Tween_PauseAll() {
        g_tweensPaused = true;
    }

    void Framework_Tween_ResumeAll() {
        g_tweensPaused = false;
    }

    void Framework_Tween_KillAll() {
        g_tweens.clear();
        g_sequences.clear();
    }

    int Framework_Tween_GetActiveCount() {
        int count = 0;
        for (const auto& pair : g_tweens) {
            if (pair.second.state == TWEEN_STATE_PLAYING) count++;
        }
        return count;
    }

    void Framework_Tween_SetGlobalTimeScale(float scale) {
        g_globalTweenTimeScale = scale;
    }

    float Framework_Tween_GetGlobalTimeScale() {
        return g_globalTweenTimeScale;
    }

    // Easing function utility
    float Framework_Tween_Ease(float t, int easing) {
        return ApplyTweenEasing(t, (TweenEasing)easing);
    }

    // ========================================================================
    // EVENT SYSTEM - Publish/Subscribe messaging
    // ========================================================================

    // Subscription types to handle different callback signatures
    enum SubscriptionType {
        SUB_TYPE_BASIC = 0,
        SUB_TYPE_INT = 1,
        SUB_TYPE_FLOAT = 2,
        SUB_TYPE_STRING = 3,
        SUB_TYPE_VECTOR2 = 4,
        SUB_TYPE_ENTITY = 5
    };

    struct Subscription {
        int id;
        int eventId;
        SubscriptionType type;
        void* callback;
        void* userData;
        int priority;
        bool enabled;
        bool oneShot;
        int targetEntity;  // -1 for global, >= 0 for entity-specific
    };

    struct RegisteredEvent {
        int id;
        std::string name;
        std::vector<int> subscriptionIds;  // Sorted by priority
    };

    struct QueuedEvent {
        int eventId;
        EventDataType dataType;
        int intValue;
        float floatValue;
        std::string stringValue;
        float x, y;
        float delay;
        float elapsed;
        int targetEntity;  // -1 for global
    };

    // Event system globals
    static std::unordered_map<int, RegisteredEvent> g_events;
    static std::unordered_map<std::string, int> g_eventIdByName;
    static std::unordered_map<int, Subscription> g_subscriptions;
    static std::vector<QueuedEvent> g_eventQueue;
    static int g_nextEventId = 1;
    static int g_nextSubscriptionId = 1;
    static bool g_eventsPaused = false;

    // Helper to get subscription
    Subscription* GetSubscription(int subId) {
        auto it = g_subscriptions.find(subId);
        return it != g_subscriptions.end() ? &it->second : nullptr;
    }

    // Helper to get event
    RegisteredEvent* GetEvent(int eventId) {
        auto it = g_events.find(eventId);
        return it != g_events.end() ? &it->second : nullptr;
    }

    // Helper to sort subscriptions by priority
    void SortEventSubscriptions(int eventId) {
        auto* evt = GetEvent(eventId);
        if (!evt) return;

        std::sort(evt->subscriptionIds.begin(), evt->subscriptionIds.end(),
            [](int a, int b) {
                auto* subA = GetSubscription(a);
                auto* subB = GetSubscription(b);
                if (!subA || !subB) return false;
                return subA->priority > subB->priority;  // Higher priority first
            });
    }

    // Event registration
    int Framework_Event_Register(const char* eventName) {
        if (!eventName) return -1;

        std::string name(eventName);
        auto it = g_eventIdByName.find(name);
        if (it != g_eventIdByName.end()) {
            return it->second;  // Already registered
        }

        int eventId = g_nextEventId++;
        RegisteredEvent evt;
        evt.id = eventId;
        evt.name = name;
        g_events[eventId] = evt;
        g_eventIdByName[name] = eventId;
        return eventId;
    }

    int Framework_Event_GetId(const char* eventName) {
        if (!eventName) return -1;
        auto it = g_eventIdByName.find(std::string(eventName));
        return it != g_eventIdByName.end() ? it->second : -1;
    }

    const char* Framework_Event_GetName(int eventId) {
        auto* evt = GetEvent(eventId);
        return evt ? evt->name.c_str() : nullptr;
    }

    bool Framework_Event_Exists(const char* eventName) {
        return eventName && g_eventIdByName.find(std::string(eventName)) != g_eventIdByName.end();
    }

    // Subscribe helpers
    int CreateSubscription(int eventId, SubscriptionType type, void* callback, void* userData, bool oneShot, int targetEntity) {
        if (!callback) return -1;
        auto* evt = GetEvent(eventId);
        if (!evt) return -1;

        int subId = g_nextSubscriptionId++;
        Subscription sub;
        sub.id = subId;
        sub.eventId = eventId;
        sub.type = type;
        sub.callback = callback;
        sub.userData = userData;
        sub.priority = 0;
        sub.enabled = true;
        sub.oneShot = oneShot;
        sub.targetEntity = targetEntity;
        g_subscriptions[subId] = sub;
        evt->subscriptionIds.push_back(subId);
        return subId;
    }

    int Framework_Event_Subscribe(int eventId, EventCallback callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_BASIC, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeInt(int eventId, EventCallbackInt callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_INT, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeFloat(int eventId, EventCallbackFloat callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_FLOAT, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeString(int eventId, EventCallbackString callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_STRING, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeVector2(int eventId, EventCallbackVector2 callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_VECTOR2, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeEntity(int eventId, EventCallbackEntity callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_ENTITY, (void*)callback, userData, false, -1);
    }

    int Framework_Event_SubscribeByName(const char* eventName, EventCallback callback, void* userData) {
        int eventId = Framework_Event_GetId(eventName);
        if (eventId < 0) eventId = Framework_Event_Register(eventName);
        return Framework_Event_Subscribe(eventId, callback, userData);
    }

    int Framework_Event_SubscribeOnce(int eventId, EventCallback callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_BASIC, (void*)callback, userData, true, -1);
    }

    int Framework_Event_SubscribeOnceInt(int eventId, EventCallbackInt callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_INT, (void*)callback, userData, true, -1);
    }

    // Unsubscribe
    void Framework_Event_Unsubscribe(int subscriptionId) {
        auto* sub = GetSubscription(subscriptionId);
        if (!sub) return;

        auto* evt = GetEvent(sub->eventId);
        if (evt) {
            auto& subs = evt->subscriptionIds;
            subs.erase(std::remove(subs.begin(), subs.end(), subscriptionId), subs.end());
        }
        g_subscriptions.erase(subscriptionId);
    }

    void Framework_Event_UnsubscribeAll(int eventId) {
        auto* evt = GetEvent(eventId);
        if (!evt) return;

        for (int subId : evt->subscriptionIds) {
            g_subscriptions.erase(subId);
        }
        evt->subscriptionIds.clear();
    }

    void Framework_Event_UnsubscribeCallback(int eventId, EventCallback callback) {
        auto* evt = GetEvent(eventId);
        if (!evt) return;

        std::vector<int> toRemove;
        for (int subId : evt->subscriptionIds) {
            auto* sub = GetSubscription(subId);
            if (sub && sub->callback == (void*)callback) {
                toRemove.push_back(subId);
            }
        }
        for (int subId : toRemove) {
            Framework_Event_Unsubscribe(subId);
        }
    }

    // Publish events (immediate dispatch)
    void DispatchEvent(int eventId, EventDataType dataType, int intVal, float floatVal,
                       const char* strVal, float x, float y, int targetEntity) {
        if (g_eventsPaused) return;

        auto* evt = GetEvent(eventId);
        if (!evt) return;

        std::vector<int> toRemove;

        // Copy subscription IDs to avoid iterator invalidation
        std::vector<int> subs = evt->subscriptionIds;

        for (int subId : subs) {
            auto* sub = GetSubscription(subId);
            if (!sub || !sub->enabled) continue;
            if (sub->targetEntity >= 0 && sub->targetEntity != targetEntity) continue;

            switch (sub->type) {
            case SUB_TYPE_BASIC:
                ((EventCallback)sub->callback)(eventId, sub->userData);
                break;
            case SUB_TYPE_INT:
                ((EventCallbackInt)sub->callback)(eventId, intVal, sub->userData);
                break;
            case SUB_TYPE_FLOAT:
                ((EventCallbackFloat)sub->callback)(eventId, floatVal, sub->userData);
                break;
            case SUB_TYPE_STRING:
                ((EventCallbackString)sub->callback)(eventId, strVal ? strVal : "", sub->userData);
                break;
            case SUB_TYPE_VECTOR2:
                ((EventCallbackVector2)sub->callback)(eventId, x, y, sub->userData);
                break;
            case SUB_TYPE_ENTITY:
                ((EventCallbackEntity)sub->callback)(eventId, targetEntity >= 0 ? targetEntity : intVal, sub->userData);
                break;
            }

            if (sub->oneShot) {
                toRemove.push_back(subId);
            }
        }

        // Remove one-shot subscriptions
        for (int subId : toRemove) {
            Framework_Event_Unsubscribe(subId);
        }
    }

    void Framework_Event_Publish(int eventId) {
        DispatchEvent(eventId, EVENT_DATA_NONE, 0, 0.0f, nullptr, 0, 0, -1);
    }

    void Framework_Event_PublishInt(int eventId, int value) {
        DispatchEvent(eventId, EVENT_DATA_INT, value, 0.0f, nullptr, 0, 0, -1);
    }

    void Framework_Event_PublishFloat(int eventId, float value) {
        DispatchEvent(eventId, EVENT_DATA_FLOAT, 0, value, nullptr, 0, 0, -1);
    }

    void Framework_Event_PublishString(int eventId, const char* value) {
        DispatchEvent(eventId, EVENT_DATA_STRING, 0, 0.0f, value, 0, 0, -1);
    }

    void Framework_Event_PublishVector2(int eventId, float x, float y) {
        DispatchEvent(eventId, EVENT_DATA_VECTOR2, 0, 0.0f, nullptr, x, y, -1);
    }

    void Framework_Event_PublishEntity(int eventId, int entity) {
        DispatchEvent(eventId, EVENT_DATA_ENTITY, entity, 0.0f, nullptr, 0, 0, -1);
    }

    void Framework_Event_PublishByName(const char* eventName) {
        int eventId = Framework_Event_GetId(eventName);
        if (eventId >= 0) Framework_Event_Publish(eventId);
    }

    void Framework_Event_PublishByNameInt(const char* eventName, int value) {
        int eventId = Framework_Event_GetId(eventName);
        if (eventId >= 0) Framework_Event_PublishInt(eventId, value);
    }

    // Queued/deferred events
    void Framework_Event_Queue(int eventId) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_NONE;
        qe.delay = 0;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    void Framework_Event_QueueInt(int eventId, int value) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_INT;
        qe.intValue = value;
        qe.delay = 0;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    void Framework_Event_QueueFloat(int eventId, float value) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_FLOAT;
        qe.floatValue = value;
        qe.delay = 0;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    void Framework_Event_QueueString(int eventId, const char* value) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_STRING;
        qe.stringValue = value ? value : "";
        qe.delay = 0;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    void Framework_Event_QueueDelayed(int eventId, float delay) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_NONE;
        qe.delay = delay;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    void Framework_Event_QueueDelayedInt(int eventId, int value, float delay) {
        QueuedEvent qe;
        qe.eventId = eventId;
        qe.dataType = EVENT_DATA_INT;
        qe.intValue = value;
        qe.delay = delay;
        qe.elapsed = 0;
        qe.targetEntity = -1;
        g_eventQueue.push_back(qe);
    }

    // Entity-specific events
    int Framework_Event_SubscribeToEntity(int entity, int eventId, EventCallbackEntity callback, void* userData) {
        return CreateSubscription(eventId, SUB_TYPE_ENTITY, (void*)callback, userData, false, entity);
    }

    void Framework_Event_PublishToEntity(int entity, int eventId) {
        DispatchEvent(eventId, EVENT_DATA_ENTITY, entity, 0.0f, nullptr, 0, 0, entity);
    }

    void Framework_Event_PublishToEntityInt(int entity, int eventId, int value) {
        DispatchEvent(eventId, EVENT_DATA_INT, value, 0.0f, nullptr, 0, 0, entity);
    }

    void Framework_Event_UnsubscribeFromEntity(int entity, int eventId) {
        auto* evt = GetEvent(eventId);
        if (!evt) return;

        std::vector<int> toRemove;
        for (int subId : evt->subscriptionIds) {
            auto* sub = GetSubscription(subId);
            if (sub && sub->targetEntity == entity) {
                toRemove.push_back(subId);
            }
        }
        for (int subId : toRemove) {
            Framework_Event_Unsubscribe(subId);
        }
    }

    void Framework_Event_UnsubscribeAllFromEntity(int entity) {
        std::vector<int> toRemove;
        for (auto& pair : g_subscriptions) {
            if (pair.second.targetEntity == entity) {
                toRemove.push_back(pair.first);
            }
        }
        for (int subId : toRemove) {
            Framework_Event_Unsubscribe(subId);
        }
    }

    // Priority control
    void Framework_Event_SetPriority(int subscriptionId, int priority) {
        auto* sub = GetSubscription(subscriptionId);
        if (!sub) return;
        sub->priority = priority;
        SortEventSubscriptions(sub->eventId);
    }

    int Framework_Event_GetPriority(int subscriptionId) {
        auto* sub = GetSubscription(subscriptionId);
        return sub ? sub->priority : 0;
    }

    // Event state and management
    void Framework_Event_SetEnabled(int subscriptionId, bool enabled) {
        auto* sub = GetSubscription(subscriptionId);
        if (sub) sub->enabled = enabled;
    }

    bool Framework_Event_IsEnabled(int subscriptionId) {
        auto* sub = GetSubscription(subscriptionId);
        return sub ? sub->enabled : false;
    }

    bool Framework_Event_IsSubscriptionValid(int subscriptionId) {
        return GetSubscription(subscriptionId) != nullptr;
    }

    int Framework_Event_GetSubscriberCount(int eventId) {
        auto* evt = GetEvent(eventId);
        return evt ? (int)evt->subscriptionIds.size() : 0;
    }

    // Queue processing
    void Framework_Event_ProcessQueue(float dt) {
        if (g_eventsPaused) return;

        std::vector<int> toFire;

        for (size_t i = 0; i < g_eventQueue.size(); i++) {
            QueuedEvent& qe = g_eventQueue[i];
            qe.elapsed += dt;
            if (qe.elapsed >= qe.delay) {
                toFire.push_back((int)i);
            }
        }

        // Fire events in order and remove from queue (reverse order to maintain indices)
        for (int i = (int)toFire.size() - 1; i >= 0; i--) {
            int idx = toFire[i];
            QueuedEvent& qe = g_eventQueue[idx];

            switch (qe.dataType) {
            case EVENT_DATA_NONE:
                if (qe.targetEntity >= 0)
                    Framework_Event_PublishToEntity(qe.targetEntity, qe.eventId);
                else
                    Framework_Event_Publish(qe.eventId);
                break;
            case EVENT_DATA_INT:
                if (qe.targetEntity >= 0)
                    Framework_Event_PublishToEntityInt(qe.targetEntity, qe.eventId, qe.intValue);
                else
                    Framework_Event_PublishInt(qe.eventId, qe.intValue);
                break;
            case EVENT_DATA_FLOAT:
                Framework_Event_PublishFloat(qe.eventId, qe.floatValue);
                break;
            case EVENT_DATA_STRING:
                Framework_Event_PublishString(qe.eventId, qe.stringValue.c_str());
                break;
            case EVENT_DATA_VECTOR2:
                Framework_Event_PublishVector2(qe.eventId, qe.x, qe.y);
                break;
            default:
                break;
            }

            g_eventQueue.erase(g_eventQueue.begin() + idx);
        }
    }

    void Framework_Event_ClearQueue() {
        g_eventQueue.clear();
    }

    int Framework_Event_GetQueuedCount() {
        return (int)g_eventQueue.size();
    }

    // Global event system management
    void Framework_Event_PauseAll() {
        g_eventsPaused = true;
    }

    void Framework_Event_ResumeAll() {
        g_eventsPaused = false;
    }

    bool Framework_Event_IsPaused() {
        return g_eventsPaused;
    }

    void Framework_Event_Clear() {
        g_events.clear();
        g_eventIdByName.clear();
        g_subscriptions.clear();
        g_eventQueue.clear();
        g_nextEventId = 1;
        g_nextSubscriptionId = 1;
        g_eventsPaused = false;
    }

    int Framework_Event_GetEventCount() {
        return (int)g_events.size();
    }

    int Framework_Event_GetTotalSubscriptions() {
        return (int)g_subscriptions.size();
    }

    // ========================================================================
    // TIMER SYSTEM - Delayed execution and scheduling
    // ========================================================================

    enum TimerType {
        TIMER_ONESHOT = 0,
        TIMER_REPEATING = 1,
        TIMER_FRAME_ONESHOT = 2,
        TIMER_FRAME_REPEATING = 3
    };

    enum TimerCallbackType {
        TIMER_CB_BASIC = 0,
        TIMER_CB_INT = 1,
        TIMER_CB_FLOAT = 2
    };

    struct Timer {
        int id;
        TimerType type;
        TimerCallbackType callbackType;
        TimerState state;
        void* callback;
        void* userData;
        int intValue;
        float floatValue;
        float delay;           // Initial delay before first fire
        float interval;        // Time between fires (for repeating)
        float elapsed;         // Time since timer started
        float timeScale;       // Per-timer time scale
        int repeatCount;       // -1 = infinite, 0+ = limited
        int currentRepeat;     // Current repeat iteration
        int targetEntity;      // -1 for global, >= 0 for entity-bound
        int frameDelay;        // For frame-based timers
        int frameInterval;     // For frame-based repeating
        int frameCounter;      // Current frame count
        bool hasInitialDelay;  // For AfterThenEvery pattern
        bool initialDelayDone; // Has initial delay passed
    };

    struct TimerSequenceEntry {
        float delay;
        TimerCallbackType callbackType;
        void* callback;
        void* userData;
        int intValue;
        bool fired;
    };

    struct TimerSequence {
        int id;
        std::vector<TimerSequenceEntry> entries;
        float elapsed;
        float duration;
        TimerState state;
        bool loop;
    };

    // Timer system globals
    static std::unordered_map<int, Timer> g_timers;
    static std::unordered_map<int, TimerSequence> g_timerSequences;
    static int g_nextTimerId = 1;
    static int g_nextTimerSeqId = 1;
    static bool g_timersPaused = false;
    static float g_globalTimerTimeScale = 1.0f;

    // Helper to get timer
    Timer* GetTimer(int timerId) {
        auto it = g_timers.find(timerId);
        return it != g_timers.end() ? &it->second : nullptr;
    }

    // Helper to get sequence
    TimerSequence* GetTimerSequence(int seqId) {
        auto it = g_timerSequences.find(seqId);
        return it != g_timerSequences.end() ? &it->second : nullptr;
    }

    // Internal timer creation
    int CreateTimer(TimerType type, TimerCallbackType cbType, void* callback, void* userData,
                    float delay, float interval, int repeatCount, int entity) {
        if (!callback) return -1;

        Timer t;
        t.id = g_nextTimerId++;
        t.type = type;
        t.callbackType = cbType;
        t.state = delay > 0 ? TIMER_STATE_PENDING : TIMER_STATE_RUNNING;
        t.callback = callback;
        t.userData = userData;
        t.intValue = 0;
        t.floatValue = 0.0f;
        t.delay = delay;
        t.interval = interval;
        t.elapsed = 0.0f;
        t.timeScale = 1.0f;
        t.repeatCount = repeatCount;
        t.currentRepeat = 0;
        t.targetEntity = entity;
        t.frameDelay = 0;
        t.frameInterval = 0;
        t.frameCounter = 0;
        t.hasInitialDelay = false;
        t.initialDelayDone = false;
        g_timers[t.id] = t;
        return t.id;
    }

    // Basic timers (one-shot)
    int Framework_Timer_After(float delay, TimerCallback callback, void* userData) {
        return CreateTimer(TIMER_ONESHOT, TIMER_CB_BASIC, (void*)callback, userData, delay, 0, 1, -1);
    }

    int Framework_Timer_AfterInt(float delay, TimerCallbackInt callback, int value, void* userData) {
        int id = CreateTimer(TIMER_ONESHOT, TIMER_CB_INT, (void*)callback, userData, delay, 0, 1, -1);
        if (auto* t = GetTimer(id)) t->intValue = value;
        return id;
    }

    int Framework_Timer_AfterFloat(float delay, TimerCallbackFloat callback, float value, void* userData) {
        int id = CreateTimer(TIMER_ONESHOT, TIMER_CB_FLOAT, (void*)callback, userData, delay, 0, 1, -1);
        if (auto* t = GetTimer(id)) t->floatValue = value;
        return id;
    }

    // Repeating timers
    int Framework_Timer_Every(float interval, TimerCallback callback, void* userData) {
        return CreateTimer(TIMER_REPEATING, TIMER_CB_BASIC, (void*)callback, userData, 0, interval, -1, -1);
    }

    int Framework_Timer_EveryInt(float interval, TimerCallbackInt callback, int value, void* userData) {
        int id = CreateTimer(TIMER_REPEATING, TIMER_CB_INT, (void*)callback, userData, 0, interval, -1, -1);
        if (auto* t = GetTimer(id)) t->intValue = value;
        return id;
    }

    int Framework_Timer_EveryLimit(float interval, int repeatCount, TimerCallback callback, void* userData) {
        return CreateTimer(TIMER_REPEATING, TIMER_CB_BASIC, (void*)callback, userData, 0, interval, repeatCount, -1);
    }

    int Framework_Timer_AfterThenEvery(float delay, float interval, TimerCallback callback, void* userData) {
        int id = CreateTimer(TIMER_REPEATING, TIMER_CB_BASIC, (void*)callback, userData, delay, interval, -1, -1);
        if (auto* t = GetTimer(id)) {
            t->hasInitialDelay = true;
            t->initialDelayDone = false;
        }
        return id;
    }

    // Timer control
    void Framework_Timer_Cancel(int timerId) {
        auto* t = GetTimer(timerId);
        if (t) t->state = TIMER_STATE_CANCELLED;
    }

    void Framework_Timer_Pause(int timerId) {
        auto* t = GetTimer(timerId);
        if (t && t->state == TIMER_STATE_RUNNING) t->state = TIMER_STATE_PAUSED;
    }

    void Framework_Timer_Resume(int timerId) {
        auto* t = GetTimer(timerId);
        if (t && t->state == TIMER_STATE_PAUSED) t->state = TIMER_STATE_RUNNING;
    }

    void Framework_Timer_Reset(int timerId) {
        auto* t = GetTimer(timerId);
        if (!t) return;
        t->elapsed = 0.0f;
        t->currentRepeat = 0;
        t->frameCounter = 0;
        t->initialDelayDone = false;
        t->state = t->delay > 0 ? TIMER_STATE_PENDING : TIMER_STATE_RUNNING;
    }

    // Timer state queries
    bool Framework_Timer_IsValid(int timerId) {
        return GetTimer(timerId) != nullptr;
    }

    bool Framework_Timer_IsRunning(int timerId) {
        auto* t = GetTimer(timerId);
        return t && t->state == TIMER_STATE_RUNNING;
    }

    bool Framework_Timer_IsPaused(int timerId) {
        auto* t = GetTimer(timerId);
        return t && t->state == TIMER_STATE_PAUSED;
    }

    int Framework_Timer_GetState(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->state : TIMER_STATE_CANCELLED;
    }

    float Framework_Timer_GetElapsed(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->elapsed : 0.0f;
    }

    float Framework_Timer_GetRemaining(int timerId) {
        auto* t = GetTimer(timerId);
        if (!t) return 0.0f;
        if (t->type == TIMER_ONESHOT) {
            return t->delay - t->elapsed;
        }
        else {
            float targetTime = t->hasInitialDelay && !t->initialDelayDone ? t->delay : t->interval;
            float cycleElapsed = t->hasInitialDelay && !t->initialDelayDone ? t->elapsed : fmodf(t->elapsed, t->interval);
            return targetTime - cycleElapsed;
        }
    }

    int Framework_Timer_GetRepeatCount(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->repeatCount : 0;
    }

    int Framework_Timer_GetCurrentRepeat(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->currentRepeat : 0;
    }

    // Timer configuration
    void Framework_Timer_SetTimeScale(int timerId, float scale) {
        auto* t = GetTimer(timerId);
        if (t) t->timeScale = scale;
    }

    float Framework_Timer_GetTimeScale(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->timeScale : 1.0f;
    }

    void Framework_Timer_SetInterval(int timerId, float interval) {
        auto* t = GetTimer(timerId);
        if (t) t->interval = interval;
    }

    float Framework_Timer_GetInterval(int timerId) {
        auto* t = GetTimer(timerId);
        return t ? t->interval : 0.0f;
    }

    // Entity-bound timers
    int Framework_Timer_AfterEntity(int entity, float delay, TimerCallback callback, void* userData) {
        return CreateTimer(TIMER_ONESHOT, TIMER_CB_BASIC, (void*)callback, userData, delay, 0, 1, entity);
    }

    int Framework_Timer_EveryEntity(int entity, float interval, TimerCallback callback, void* userData) {
        return CreateTimer(TIMER_REPEATING, TIMER_CB_BASIC, (void*)callback, userData, 0, interval, -1, entity);
    }

    void Framework_Timer_CancelAllForEntity(int entity) {
        for (auto& pair : g_timers) {
            if (pair.second.targetEntity == entity) {
                pair.second.state = TIMER_STATE_CANCELLED;
            }
        }
    }

    // Sequence building
    int Framework_Timer_CreateSequence() {
        TimerSequence seq;
        seq.id = g_nextTimerSeqId++;
        seq.elapsed = 0.0f;
        seq.duration = 0.0f;
        seq.state = TIMER_STATE_PENDING;
        seq.loop = false;
        g_timerSequences[seq.id] = seq;
        return seq.id;
    }

    void Framework_Timer_SequenceAppend(int seqId, float delay, TimerCallback callback, void* userData) {
        auto* seq = GetTimerSequence(seqId);
        if (!seq) return;

        TimerSequenceEntry entry;
        entry.delay = seq->duration + delay;
        entry.callbackType = TIMER_CB_BASIC;
        entry.callback = (void*)callback;
        entry.userData = userData;
        entry.intValue = 0;
        entry.fired = false;
        seq->entries.push_back(entry);
        seq->duration = entry.delay;
    }

    void Framework_Timer_SequenceAppendInt(int seqId, float delay, TimerCallbackInt callback, int value, void* userData) {
        auto* seq = GetTimerSequence(seqId);
        if (!seq) return;

        TimerSequenceEntry entry;
        entry.delay = seq->duration + delay;
        entry.callbackType = TIMER_CB_INT;
        entry.callback = (void*)callback;
        entry.userData = userData;
        entry.intValue = value;
        entry.fired = false;
        seq->entries.push_back(entry);
        seq->duration = entry.delay;
    }

    void Framework_Timer_SequenceStart(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        if (seq) {
            seq->state = TIMER_STATE_RUNNING;
            seq->elapsed = 0.0f;
            for (auto& e : seq->entries) e.fired = false;
        }
    }

    void Framework_Timer_SequencePause(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        if (seq && seq->state == TIMER_STATE_RUNNING) seq->state = TIMER_STATE_PAUSED;
    }

    void Framework_Timer_SequenceResume(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        if (seq && seq->state == TIMER_STATE_PAUSED) seq->state = TIMER_STATE_RUNNING;
    }

    void Framework_Timer_SequenceCancel(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        if (seq) seq->state = TIMER_STATE_CANCELLED;
    }

    void Framework_Timer_SequenceReset(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        if (seq) {
            seq->elapsed = 0.0f;
            seq->state = TIMER_STATE_PENDING;
            for (auto& e : seq->entries) e.fired = false;
        }
    }

    bool Framework_Timer_SequenceIsValid(int seqId) {
        return GetTimerSequence(seqId) != nullptr;
    }

    bool Framework_Timer_SequenceIsRunning(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        return seq && seq->state == TIMER_STATE_RUNNING;
    }

    float Framework_Timer_SequenceGetDuration(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        return seq ? seq->duration : 0.0f;
    }

    float Framework_Timer_SequenceGetElapsed(int seqId) {
        auto* seq = GetTimerSequence(seqId);
        return seq ? seq->elapsed : 0.0f;
    }

    void Framework_Timer_SequenceSetLoop(int seqId, bool loop) {
        auto* seq = GetTimerSequence(seqId);
        if (seq) seq->loop = loop;
    }

    // Fire timer callback
    void FireTimerCallback(Timer& t) {
        switch (t.callbackType) {
        case TIMER_CB_BASIC:
            ((TimerCallback)t.callback)(t.id, t.userData);
            break;
        case TIMER_CB_INT:
            ((TimerCallbackInt)t.callback)(t.id, t.intValue, t.userData);
            break;
        case TIMER_CB_FLOAT:
            ((TimerCallbackFloat)t.callback)(t.id, t.floatValue, t.userData);
            break;
        }
    }

    // Global timer management
    void Framework_Timer_Update(float dt) {
        if (g_timersPaused) return;

        float scaledDt = dt * g_globalTimerTimeScale;
        std::vector<int> toRemove;

        // Update all timers
        for (auto& pair : g_timers) {
            Timer& t = pair.second;

            // Skip inactive timers
            if (t.state != TIMER_STATE_RUNNING && t.state != TIMER_STATE_PENDING) continue;

            // Check entity-bound timers
            if (t.targetEntity >= 0 && !Framework_Ecs_IsAlive(t.targetEntity)) {
                t.state = TIMER_STATE_CANCELLED;
                continue;
            }

            float timerDt = scaledDt * t.timeScale;

            // Handle frame-based timers
            if (t.type == TIMER_FRAME_ONESHOT || t.type == TIMER_FRAME_REPEATING) {
                t.frameCounter++;

                if (t.type == TIMER_FRAME_ONESHOT) {
                    if (t.frameCounter >= t.frameDelay) {
                        FireTimerCallback(t);
                        t.state = TIMER_STATE_COMPLETED;
                    }
                }
                else {
                    if (t.frameCounter >= t.frameInterval) {
                        FireTimerCallback(t);
                        t.frameCounter = 0;
                        t.currentRepeat++;
                        if (t.repeatCount >= 0 && t.currentRepeat >= t.repeatCount) {
                            t.state = TIMER_STATE_COMPLETED;
                        }
                    }
                }
                continue;
            }

            // Time-based timers
            t.elapsed += timerDt;

            if (t.type == TIMER_ONESHOT) {
                if (t.elapsed >= t.delay) {
                    FireTimerCallback(t);
                    t.state = TIMER_STATE_COMPLETED;
                }
                else if (t.state == TIMER_STATE_PENDING) {
                    t.state = TIMER_STATE_RUNNING;
                }
            }
            else if (t.type == TIMER_REPEATING) {
                // Handle initial delay for AfterThenEvery
                if (t.hasInitialDelay && !t.initialDelayDone) {
                    if (t.elapsed >= t.delay) {
                        FireTimerCallback(t);
                        t.initialDelayDone = true;
                        t.elapsed = 0.0f;
                        t.currentRepeat++;
                    }
                }
                else {
                    // Regular repeating
                    while (t.elapsed >= t.interval && t.state == TIMER_STATE_RUNNING) {
                        FireTimerCallback(t);
                        t.elapsed -= t.interval;
                        t.currentRepeat++;

                        if (t.repeatCount >= 0 && t.currentRepeat >= t.repeatCount) {
                            t.state = TIMER_STATE_COMPLETED;
                            break;
                        }
                    }
                }

                if (t.state == TIMER_STATE_PENDING) {
                    t.state = TIMER_STATE_RUNNING;
                }
            }
        }

        // Update sequences
        for (auto& pair : g_timerSequences) {
            TimerSequence& seq = pair.second;
            if (seq.state != TIMER_STATE_RUNNING) continue;

            seq.elapsed += scaledDt;

            // Fire callbacks that are due
            for (auto& entry : seq.entries) {
                if (!entry.fired && seq.elapsed >= entry.delay) {
                    entry.fired = true;
                    switch (entry.callbackType) {
                    case TIMER_CB_BASIC:
                        ((TimerCallback)entry.callback)(seq.id, entry.userData);
                        break;
                    case TIMER_CB_INT:
                        ((TimerCallbackInt)entry.callback)(seq.id, entry.intValue, entry.userData);
                        break;
                    default:
                        break;
                    }
                }
            }

            // Check if sequence is complete
            if (seq.elapsed >= seq.duration) {
                if (seq.loop) {
                    seq.elapsed = 0.0f;
                    for (auto& e : seq.entries) e.fired = false;
                }
                else {
                    seq.state = TIMER_STATE_COMPLETED;
                }
            }
        }
    }

    void Framework_Timer_PauseAll() {
        g_timersPaused = true;
    }

    void Framework_Timer_ResumeAll() {
        g_timersPaused = false;
    }

    void Framework_Timer_CancelAll() {
        for (auto& pair : g_timers) {
            pair.second.state = TIMER_STATE_CANCELLED;
        }
        for (auto& pair : g_timerSequences) {
            pair.second.state = TIMER_STATE_CANCELLED;
        }
    }

    int Framework_Timer_GetActiveCount() {
        int count = 0;
        for (auto& pair : g_timers) {
            if (pair.second.state == TIMER_STATE_RUNNING || pair.second.state == TIMER_STATE_PENDING) {
                count++;
            }
        }
        return count;
    }

    void Framework_Timer_SetGlobalTimeScale(float scale) {
        g_globalTimerTimeScale = scale;
    }

    float Framework_Timer_GetGlobalTimeScale() {
        return g_globalTimerTimeScale;
    }

    // Frame-based timers
    int Framework_Timer_AfterFrames(int frames, TimerCallback callback, void* userData) {
        int id = CreateTimer(TIMER_FRAME_ONESHOT, TIMER_CB_BASIC, (void*)callback, userData, 0, 0, 1, -1);
        if (auto* t = GetTimer(id)) {
            t->frameDelay = frames;
        }
        return id;
    }

    int Framework_Timer_EveryFrames(int frames, TimerCallback callback, void* userData) {
        int id = CreateTimer(TIMER_FRAME_REPEATING, TIMER_CB_BASIC, (void*)callback, userData, 0, 0, -1, -1);
        if (auto* t = GetTimer(id)) {
            t->frameInterval = frames;
        }
        return id;
    }

    // Utility functions
    void Framework_Timer_ClearCompleted() {
        std::vector<int> toRemove;
        for (auto& pair : g_timers) {
            if (pair.second.state == TIMER_STATE_COMPLETED || pair.second.state == TIMER_STATE_CANCELLED) {
                toRemove.push_back(pair.first);
            }
        }
        for (int id : toRemove) {
            g_timers.erase(id);
        }

        std::vector<int> seqToRemove;
        for (auto& pair : g_timerSequences) {
            if (pair.second.state == TIMER_STATE_COMPLETED || pair.second.state == TIMER_STATE_CANCELLED) {
                seqToRemove.push_back(pair.first);
            }
        }
        for (int id : seqToRemove) {
            g_timerSequences.erase(id);
        }
    }

    // ========================================================================
    // OBJECT POOLING - Efficient object reuse
    // ========================================================================

    struct PoolObject {
        bool active;
        int entityId;  // For entity pools, -1 for generic pools
    };

    struct ObjectPool {
        int id;
        std::string name;
        std::vector<PoolObject> objects;
        std::vector<int> availableIndices;  // Stack of available object indices
        int maxCapacity;
        bool autoGrow;
        int growAmount;
        int prefabId;  // For entity pools, -1 for generic
        bool isEntityPool;

        // Callbacks
        PoolResetCallback resetCallback;
        void* resetUserData;
        PoolInitCallback initCallback;
        void* initUserData;

        // Statistics
        int totalAcquires;
        int totalReleases;
        int peakUsage;
    };

    // Pool system globals
    static std::unordered_map<int, ObjectPool> g_pools;
    static std::unordered_map<std::string, int> g_poolIdByName;
    static int g_nextPoolId = 1;

    // Helper to get pool
    ObjectPool* GetPool(int poolId) {
        auto it = g_pools.find(poolId);
        return it != g_pools.end() ? &it->second : nullptr;
    }

    // Pool creation and management
    int Framework_Pool_Create(const char* poolName, int initialCapacity, int maxCapacity) {
        if (!poolName || initialCapacity < 0) return -1;
        if (maxCapacity > 0 && initialCapacity > maxCapacity) initialCapacity = maxCapacity;

        std::string name(poolName);
        auto it = g_poolIdByName.find(name);
        if (it != g_poolIdByName.end()) {
            return it->second;  // Already exists
        }

        ObjectPool pool;
        pool.id = g_nextPoolId++;
        pool.name = name;
        pool.maxCapacity = maxCapacity > 0 ? maxCapacity : INT_MAX;
        pool.autoGrow = true;
        pool.growAmount = 10;
        pool.prefabId = -1;
        pool.isEntityPool = false;
        pool.resetCallback = nullptr;
        pool.resetUserData = nullptr;
        pool.initCallback = nullptr;
        pool.initUserData = nullptr;
        pool.totalAcquires = 0;
        pool.totalReleases = 0;
        pool.peakUsage = 0;

        // Initialize objects
        pool.objects.resize(initialCapacity);
        for (int i = 0; i < initialCapacity; i++) {
            pool.objects[i].active = false;
            pool.objects[i].entityId = -1;
            pool.availableIndices.push_back(i);
        }

        g_pools[pool.id] = pool;
        g_poolIdByName[name] = pool.id;
        return pool.id;
    }

    int Framework_Pool_GetByName(const char* poolName) {
        if (!poolName) return -1;
        auto it = g_poolIdByName.find(std::string(poolName));
        return it != g_poolIdByName.end() ? it->second : -1;
    }

    void Framework_Pool_Destroy(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return;

        // For entity pools, destroy all entities
        if (pool->isEntityPool) {
            for (auto& obj : pool->objects) {
                if (obj.entityId >= 0) {
                    Framework_Ecs_DestroyEntity(obj.entityId);
                }
            }
        }

        g_poolIdByName.erase(pool->name);
        g_pools.erase(poolId);
    }

    bool Framework_Pool_IsValid(int poolId) {
        return GetPool(poolId) != nullptr;
    }

    // Pool configuration
    void Framework_Pool_SetAutoGrow(int poolId, bool autoGrow) {
        auto* pool = GetPool(poolId);
        if (pool) pool->autoGrow = autoGrow;
    }

    bool Framework_Pool_GetAutoGrow(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->autoGrow : false;
    }

    void Framework_Pool_SetGrowAmount(int poolId, int amount) {
        auto* pool = GetPool(poolId);
        if (pool && amount > 0) pool->growAmount = amount;
    }

    int Framework_Pool_GetGrowAmount(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->growAmount : 0;
    }

    void Framework_Pool_SetResetCallback(int poolId, PoolResetCallback callback, void* userData) {
        auto* pool = GetPool(poolId);
        if (pool) {
            pool->resetCallback = callback;
            pool->resetUserData = userData;
        }
    }

    void Framework_Pool_SetInitCallback(int poolId, PoolInitCallback callback, void* userData) {
        auto* pool = GetPool(poolId);
        if (pool) {
            pool->initCallback = callback;
            pool->initUserData = userData;
        }
    }

    // Internal: grow pool
    void GrowPool(ObjectPool* pool, int amount) {
        if (!pool) return;
        int currentSize = (int)pool->objects.size();
        int newSize = currentSize + amount;
        if (newSize > pool->maxCapacity) newSize = pool->maxCapacity;
        if (newSize <= currentSize) return;

        pool->objects.resize(newSize);
        for (int i = currentSize; i < newSize; i++) {
            pool->objects[i].active = false;
            pool->objects[i].entityId = -1;
            pool->availableIndices.push_back(i);

            // For entity pools, create entities
            if (pool->isEntityPool && pool->prefabId >= 0) {
                int entity = Framework_Prefab_Instantiate(pool->prefabId, -1, 0, 0);
                pool->objects[i].entityId = entity;
                Framework_Ecs_SetEnabled(entity, false);
            }

            // Call init callback
            if (pool->initCallback) {
                pool->initCallback(pool->id, i, pool->initUserData);
            }
        }
    }

    // Acquire and release objects
    int Framework_Pool_Acquire(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return -1;

        // Check if we need to grow
        if (pool->availableIndices.empty()) {
            if (pool->autoGrow && (int)pool->objects.size() < pool->maxCapacity) {
                GrowPool(pool, pool->growAmount);
            }
            if (pool->availableIndices.empty()) {
                return -1;  // Still empty, can't grow more
            }
        }

        // Get from available stack
        int index = pool->availableIndices.back();
        pool->availableIndices.pop_back();
        pool->objects[index].active = true;

        // Update stats
        pool->totalAcquires++;
        int activeCount = (int)pool->objects.size() - (int)pool->availableIndices.size();
        if (activeCount > pool->peakUsage) pool->peakUsage = activeCount;

        return index;
    }

    void Framework_Pool_Release(int poolId, int objectIndex) {
        auto* pool = GetPool(poolId);
        if (!pool) return;
        if (objectIndex < 0 || objectIndex >= (int)pool->objects.size()) return;
        if (!pool->objects[objectIndex].active) return;  // Already released

        pool->objects[objectIndex].active = false;
        pool->availableIndices.push_back(objectIndex);
        pool->totalReleases++;

        // Call reset callback
        if (pool->resetCallback) {
            pool->resetCallback(pool->id, objectIndex, pool->resetUserData);
        }
    }

    void Framework_Pool_ReleaseAll(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return;

        pool->availableIndices.clear();
        for (int i = 0; i < (int)pool->objects.size(); i++) {
            if (pool->objects[i].active) {
                pool->objects[i].active = false;
                pool->totalReleases++;
                if (pool->resetCallback) {
                    pool->resetCallback(pool->id, i, pool->resetUserData);
                }
            }
            pool->availableIndices.push_back(i);
        }
    }

    // Pool state queries
    int Framework_Pool_GetCapacity(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? (int)pool->objects.size() : 0;
    }

    int Framework_Pool_GetActiveCount(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return 0;
        return (int)pool->objects.size() - (int)pool->availableIndices.size();
    }

    int Framework_Pool_GetAvailableCount(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? (int)pool->availableIndices.size() : 0;
    }

    bool Framework_Pool_IsEmpty(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->availableIndices.empty() : true;
    }

    bool Framework_Pool_IsFull(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return true;
        return pool->availableIndices.empty() && (int)pool->objects.size() >= pool->maxCapacity;
    }

    bool Framework_Pool_IsObjectActive(int poolId, int objectIndex) {
        auto* pool = GetPool(poolId);
        if (!pool || objectIndex < 0 || objectIndex >= (int)pool->objects.size()) return false;
        return pool->objects[objectIndex].active;
    }

    // Pool statistics
    int Framework_Pool_GetTotalAcquires(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->totalAcquires : 0;
    }

    int Framework_Pool_GetTotalReleases(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->totalReleases : 0;
    }

    int Framework_Pool_GetPeakUsage(int poolId) {
        auto* pool = GetPool(poolId);
        return pool ? pool->peakUsage : 0;
    }

    void Framework_Pool_ResetStats(int poolId) {
        auto* pool = GetPool(poolId);
        if (pool) {
            pool->totalAcquires = 0;
            pool->totalReleases = 0;
            pool->peakUsage = Framework_Pool_GetActiveCount(poolId);
        }
    }

    // Pre-warming
    void Framework_Pool_Warmup(int poolId, int count) {
        auto* pool = GetPool(poolId);
        if (!pool || count <= 0) return;

        int currentSize = (int)pool->objects.size();
        int targetSize = currentSize + count;
        if (targetSize > pool->maxCapacity) targetSize = pool->maxCapacity;

        GrowPool(pool, targetSize - currentSize);
    }

    void Framework_Pool_Shrink(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return;

        // Only shrink if we have inactive objects at the end
        // This is a simple shrink - just removes trailing inactive objects
        while (!pool->objects.empty() && !pool->objects.back().active) {
            int lastIndex = (int)pool->objects.size() - 1;

            // Remove from available indices
            auto it = std::find(pool->availableIndices.begin(), pool->availableIndices.end(), lastIndex);
            if (it != pool->availableIndices.end()) {
                pool->availableIndices.erase(it);
            }

            // For entity pools, destroy the entity
            if (pool->isEntityPool && pool->objects.back().entityId >= 0) {
                Framework_Ecs_DestroyEntity(pool->objects.back().entityId);
            }

            pool->objects.pop_back();
        }
    }

    // Entity pools
    int Framework_Pool_CreateEntityPool(const char* poolName, int prefabId, int initialCapacity, int maxCapacity) {
        int poolId = Framework_Pool_Create(poolName, 0, maxCapacity);  // Create empty first
        auto* pool = GetPool(poolId);
        if (!pool) return -1;

        pool->prefabId = prefabId;
        pool->isEntityPool = true;

        // Now add initial capacity with entities
        if (initialCapacity > 0) {
            GrowPool(pool, initialCapacity);
        }

        return poolId;
    }

    int Framework_Pool_AcquireEntity(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool || !pool->isEntityPool) return -1;

        int index = Framework_Pool_Acquire(poolId);
        if (index < 0) return -1;

        int entity = pool->objects[index].entityId;
        if (entity >= 0) {
            Framework_Ecs_SetEnabled(entity, true);
        }
        return entity;
    }

    void Framework_Pool_ReleaseEntity(int poolId, int entity) {
        auto* pool = GetPool(poolId);
        if (!pool || !pool->isEntityPool) return;

        // Find the object with this entity
        for (int i = 0; i < (int)pool->objects.size(); i++) {
            if (pool->objects[i].entityId == entity && pool->objects[i].active) {
                Framework_Ecs_SetEnabled(entity, false);
                Framework_Pool_Release(poolId, i);
                return;
            }
        }
    }

    // Iterate active objects
    int Framework_Pool_GetFirstActive(int poolId) {
        auto* pool = GetPool(poolId);
        if (!pool) return -1;

        for (int i = 0; i < (int)pool->objects.size(); i++) {
            if (pool->objects[i].active) return i;
        }
        return -1;
    }

    int Framework_Pool_GetNextActive(int poolId, int currentIndex) {
        auto* pool = GetPool(poolId);
        if (!pool) return -1;

        for (int i = currentIndex + 1; i < (int)pool->objects.size(); i++) {
            if (pool->objects[i].active) return i;
        }
        return -1;
    }

    // Bulk operations
    int Framework_Pool_AcquireMultiple(int poolId, int count, int* outIndices) {
        if (!outIndices || count <= 0) return 0;

        int acquired = 0;
        for (int i = 0; i < count; i++) {
            int index = Framework_Pool_Acquire(poolId);
            if (index < 0) break;
            outIndices[acquired++] = index;
        }
        return acquired;
    }

    void Framework_Pool_ReleaseMultiple(int poolId, int* indices, int count) {
        if (!indices || count <= 0) return;

        for (int i = 0; i < count; i++) {
            Framework_Pool_Release(poolId, indices[i]);
        }
    }

    // Global pool management
    int Framework_Pool_GetPoolCount() {
        return (int)g_pools.size();
    }

    void Framework_Pool_DestroyAll() {
        std::vector<int> poolIds;
        for (auto& pair : g_pools) {
            poolIds.push_back(pair.first);
        }
        for (int id : poolIds) {
            Framework_Pool_Destroy(id);
        }
    }

    void Framework_Pool_ReleaseAllPools() {
        for (auto& pair : g_pools) {
            Framework_Pool_ReleaseAll(pair.first);
        }
    }

    // ========================================================================
    // STATE MACHINE SYSTEM
    // ========================================================================

    struct FSMState {
        int id;
        std::string name;
        StateEnterCallback enterCallback = nullptr;
        void* enterUserData = nullptr;
        StateUpdateCallback updateCallback = nullptr;
        void* updateUserData = nullptr;
        StateExitCallback exitCallback = nullptr;
        void* exitUserData = nullptr;
    };

    struct FSMTransition {
        int id;
        int fromState;
        int toState;
        bool isAnyState;  // Can trigger from any state
        TransitionCondition condition = nullptr;
        void* conditionUserData = nullptr;
    };

    struct FSMTrigger {
        int id;
        std::string name;
        int fromState;  // -1 for any state
        int toState;
        void* lastData = nullptr;
    };

    struct StateMachine {
        int id;
        std::string name;
        int entity = -1;  // -1 if not bound to entity

        std::unordered_map<int, FSMState> states;
        std::unordered_map<std::string, int> stateIdByName;
        int nextStateId = 0;

        std::unordered_map<int, FSMTransition> transitions;
        int nextTransitionId = 0;

        std::unordered_map<int, FSMTrigger> triggers;
        std::unordered_map<std::string, std::vector<int>> triggerIdsByName;
        int nextTriggerId = 0;

        int initialState = -1;
        int currentState = -1;
        int previousState = -1;

        bool running = false;
        bool paused = false;

        float timeInState = 0.0f;
        int stateChangeCount = 0;

        std::vector<int> stateHistory;
        int maxHistorySize = 10;

        bool debugEnabled = false;
    };

    static std::unordered_map<int, StateMachine> g_fsms;
    static std::unordered_map<std::string, int> g_fsmIdByName;
    static std::unordered_map<int, int> g_fsmIdByEntity;
    static int g_nextFsmId = 1;
    static bool g_fsmGlobalPaused = false;

    static StateMachine* GetFSM(int fsmId) {
        auto it = g_fsms.find(fsmId);
        return (it != g_fsms.end()) ? &it->second : nullptr;
    }

    static FSMState* GetFSMState(StateMachine* fsm, int stateId) {
        if (!fsm) return nullptr;
        auto it = fsm->states.find(stateId);
        return (it != fsm->states.end()) ? &it->second : nullptr;
    }

    static void FSMPerformTransition(StateMachine* fsm, int newState) {
        if (!fsm || newState == fsm->currentState) return;

        FSMState* oldStateObj = GetFSMState(fsm, fsm->currentState);
        FSMState* newStateObj = GetFSMState(fsm, newState);

        if (!newStateObj) return;

        // Exit old state
        if (oldStateObj && oldStateObj->exitCallback) {
            oldStateObj->exitCallback(fsm->id, fsm->currentState, newState, oldStateObj->exitUserData);
        }

        // Update history
        if (fsm->currentState >= 0) {
            fsm->stateHistory.insert(fsm->stateHistory.begin(), fsm->currentState);
            while ((int)fsm->stateHistory.size() > fsm->maxHistorySize) {
                fsm->stateHistory.pop_back();
            }
        }

        int prevState = fsm->currentState;
        fsm->previousState = prevState;
        fsm->currentState = newState;
        fsm->timeInState = 0.0f;
        fsm->stateChangeCount++;

        if (fsm->debugEnabled) {
            const char* fromName = prevState >= 0 ? fsm->states[prevState].name.c_str() : "none";
            const char* toName = newStateObj->name.c_str();
            TraceLog(LOG_INFO, "FSM[%s]: %s -> %s", fsm->name.c_str(), fromName, toName);
        }

        // Enter new state
        if (newStateObj->enterCallback) {
            newStateObj->enterCallback(fsm->id, newState, prevState, newStateObj->enterUserData);
        }
    }

    // FSM creation and management
    int Framework_FSM_Create(const char* name) {
        StateMachine fsm;
        fsm.id = g_nextFsmId++;
        fsm.name = name ? name : "";

        g_fsms[fsm.id] = fsm;
        if (name && strlen(name) > 0) {
            g_fsmIdByName[name] = fsm.id;
        }

        return fsm.id;
    }

    int Framework_FSM_CreateForEntity(const char* name, int entity) {
        int fsmId = Framework_FSM_Create(name);
        auto* fsm = GetFSM(fsmId);
        if (fsm) {
            fsm->entity = entity;
            g_fsmIdByEntity[entity] = fsmId;
        }
        return fsmId;
    }

    void Framework_FSM_Destroy(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return;

        // Stop if running
        if (fsm->running) {
            Framework_FSM_Stop(fsmId);
        }

        // Remove from lookup maps
        if (!fsm->name.empty()) {
            g_fsmIdByName.erase(fsm->name);
        }
        if (fsm->entity >= 0) {
            g_fsmIdByEntity.erase(fsm->entity);
        }

        g_fsms.erase(fsmId);
    }

    int Framework_FSM_GetByName(const char* name) {
        if (!name) return -1;
        auto it = g_fsmIdByName.find(name);
        return (it != g_fsmIdByName.end()) ? it->second : -1;
    }

    int Framework_FSM_GetForEntity(int entity) {
        auto it = g_fsmIdByEntity.find(entity);
        return (it != g_fsmIdByEntity.end()) ? it->second : -1;
    }

    bool Framework_FSM_IsValid(int fsmId) {
        return GetFSM(fsmId) != nullptr;
    }

    // State registration
    int Framework_FSM_AddState(int fsmId, const char* stateName) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !stateName) return -1;

        // Check if state already exists
        auto it = fsm->stateIdByName.find(stateName);
        if (it != fsm->stateIdByName.end()) {
            return it->second;
        }

        FSMState state;
        state.id = fsm->nextStateId++;
        state.name = stateName;

        fsm->states[state.id] = state;
        fsm->stateIdByName[stateName] = state.id;

        return state.id;
    }

    int Framework_FSM_GetState(int fsmId, const char* stateName) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !stateName) return -1;

        auto it = fsm->stateIdByName.find(stateName);
        return (it != fsm->stateIdByName.end()) ? it->second : -1;
    }

    const char* Framework_FSM_GetStateName(int fsmId, int stateId) {
        auto* fsm = GetFSM(fsmId);
        auto* state = GetFSMState(fsm, stateId);
        return state ? state->name.c_str() : "";
    }

    void Framework_FSM_RemoveState(int fsmId, int stateId) {
        auto* fsm = GetFSM(fsmId);
        auto* state = GetFSMState(fsm, stateId);
        if (!state) return;

        // Can't remove current state while running
        if (fsm->running && fsm->currentState == stateId) return;

        fsm->stateIdByName.erase(state->name);
        fsm->states.erase(stateId);

        // Remove transitions involving this state
        std::vector<int> toRemove;
        for (auto& pair : fsm->transitions) {
            if (pair.second.fromState == stateId || pair.second.toState == stateId) {
                toRemove.push_back(pair.first);
            }
        }
        for (int id : toRemove) {
            fsm->transitions.erase(id);
        }
    }

    int Framework_FSM_GetStateCount(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? (int)fsm->states.size() : 0;
    }

    // State callbacks
    void Framework_FSM_SetStateEnter(int fsmId, int stateId, StateEnterCallback callback, void* userData) {
        auto* fsm = GetFSM(fsmId);
        auto* state = GetFSMState(fsm, stateId);
        if (state) {
            state->enterCallback = callback;
            state->enterUserData = userData;
        }
    }

    void Framework_FSM_SetStateUpdate(int fsmId, int stateId, StateUpdateCallback callback, void* userData) {
        auto* fsm = GetFSM(fsmId);
        auto* state = GetFSMState(fsm, stateId);
        if (state) {
            state->updateCallback = callback;
            state->updateUserData = userData;
        }
    }

    void Framework_FSM_SetStateExit(int fsmId, int stateId, StateExitCallback callback, void* userData) {
        auto* fsm = GetFSM(fsmId);
        auto* state = GetFSMState(fsm, stateId);
        if (state) {
            state->exitCallback = callback;
            state->exitUserData = userData;
        }
    }

    // Transitions
    int Framework_FSM_AddTransition(int fsmId, int fromState, int toState) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return -1;

        FSMTransition transition;
        transition.id = fsm->nextTransitionId++;
        transition.fromState = fromState;
        transition.toState = toState;
        transition.isAnyState = false;

        fsm->transitions[transition.id] = transition;
        return transition.id;
    }

    void Framework_FSM_SetTransitionCondition(int fsmId, int transitionId, TransitionCondition condition, void* userData) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return;

        auto it = fsm->transitions.find(transitionId);
        if (it != fsm->transitions.end()) {
            it->second.condition = condition;
            it->second.conditionUserData = userData;
        }
    }

    void Framework_FSM_RemoveTransition(int fsmId, int transitionId) {
        auto* fsm = GetFSM(fsmId);
        if (fsm) {
            fsm->transitions.erase(transitionId);
        }
    }

    bool Framework_FSM_CanTransition(int fsmId, int fromState, int toState) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return false;

        for (auto& pair : fsm->transitions) {
            auto& t = pair.second;
            if ((t.fromState == fromState || t.isAnyState) && t.toState == toState) {
                if (!t.condition) return true;
                return t.condition(fsmId, fromState, toState, t.conditionUserData);
            }
        }
        return false;
    }

    // Any-state transitions
    int Framework_FSM_AddAnyTransition(int fsmId, int toState) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return -1;

        FSMTransition transition;
        transition.id = fsm->nextTransitionId++;
        transition.fromState = -1;
        transition.toState = toState;
        transition.isAnyState = true;

        fsm->transitions[transition.id] = transition;
        return transition.id;
    }

    void Framework_FSM_SetAnyTransitionCondition(int fsmId, int transitionId, TransitionCondition condition, void* userData) {
        Framework_FSM_SetTransitionCondition(fsmId, transitionId, condition, userData);
    }

    // State machine control
    void Framework_FSM_SetInitialState(int fsmId, int stateId) {
        auto* fsm = GetFSM(fsmId);
        if (fsm) {
            fsm->initialState = stateId;
        }
    }

    void Framework_FSM_Start(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || fsm->running) return;

        fsm->running = true;
        fsm->paused = false;
        fsm->timeInState = 0.0f;
        fsm->stateChangeCount = 0;
        fsm->stateHistory.clear();
        fsm->previousState = -1;

        // Enter initial state
        if (fsm->initialState >= 0) {
            fsm->currentState = fsm->initialState;
            auto* state = GetFSMState(fsm, fsm->initialState);
            if (state && state->enterCallback) {
                state->enterCallback(fsm->id, fsm->initialState, -1, state->enterUserData);
            }
            if (fsm->debugEnabled) {
                TraceLog(LOG_INFO, "FSM[%s]: Started in state '%s'", fsm->name.c_str(), state ? state->name.c_str() : "unknown");
            }
        }
    }

    void Framework_FSM_Stop(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running) return;

        // Exit current state
        auto* state = GetFSMState(fsm, fsm->currentState);
        if (state && state->exitCallback) {
            state->exitCallback(fsm->id, fsm->currentState, -1, state->exitUserData);
        }

        fsm->running = false;
        fsm->paused = false;
        fsm->currentState = -1;

        if (fsm->debugEnabled) {
            TraceLog(LOG_INFO, "FSM[%s]: Stopped", fsm->name.c_str());
        }
    }

    void Framework_FSM_Pause(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (fsm && fsm->running) {
            fsm->paused = true;
        }
    }

    void Framework_FSM_Resume(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (fsm && fsm->running) {
            fsm->paused = false;
        }
    }

    bool Framework_FSM_IsRunning(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->running : false;
    }

    bool Framework_FSM_IsPaused(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->paused : false;
    }

    // State queries
    int Framework_FSM_GetCurrentState(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->currentState : -1;
    }

    int Framework_FSM_GetPreviousState(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->previousState : -1;
    }

    float Framework_FSM_GetTimeInState(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->timeInState : 0.0f;
    }

    int Framework_FSM_GetStateChangeCount(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->stateChangeCount : 0;
    }

    // Manual transitions
    bool Framework_FSM_TransitionTo(int fsmId, int stateId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running) return false;

        auto* state = GetFSMState(fsm, stateId);
        if (!state) return false;

        FSMPerformTransition(fsm, stateId);
        return true;
    }

    bool Framework_FSM_TransitionToByName(int fsmId, const char* stateName) {
        int stateId = Framework_FSM_GetState(fsmId, stateName);
        return Framework_FSM_TransitionTo(fsmId, stateId);
    }

    bool Framework_FSM_TryTransition(int fsmId, int toState) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running) return false;

        if (Framework_FSM_CanTransition(fsmId, fsm->currentState, toState)) {
            FSMPerformTransition(fsm, toState);
            return true;
        }
        return false;
    }

    void Framework_FSM_RevertToPrevious(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running || fsm->previousState < 0) return;

        FSMPerformTransition(fsm, fsm->previousState);
    }

    // State history
    void Framework_FSM_SetHistorySize(int fsmId, int size) {
        auto* fsm = GetFSM(fsmId);
        if (fsm && size >= 0) {
            fsm->maxHistorySize = size;
            while ((int)fsm->stateHistory.size() > size) {
                fsm->stateHistory.pop_back();
            }
        }
    }

    int Framework_FSM_GetHistoryState(int fsmId, int index) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || index < 0 || index >= (int)fsm->stateHistory.size()) return -1;
        return fsm->stateHistory[index];
    }

    int Framework_FSM_GetHistoryCount(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? (int)fsm->stateHistory.size() : 0;
    }

    // Triggers
    int Framework_FSM_AddTrigger(int fsmId, const char* triggerName, int fromState, int toState) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !triggerName) return -1;

        FSMTrigger trigger;
        trigger.id = fsm->nextTriggerId++;
        trigger.name = triggerName;
        trigger.fromState = fromState;
        trigger.toState = toState;

        fsm->triggers[trigger.id] = trigger;
        fsm->triggerIdsByName[triggerName].push_back(trigger.id);

        return trigger.id;
    }

    void Framework_FSM_FireTrigger(int fsmId, const char* triggerName) {
        Framework_FSM_FireTriggerWithData(fsmId, triggerName, nullptr);
    }

    void Framework_FSM_FireTriggerWithData(int fsmId, const char* triggerName, void* data) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running || !triggerName) return;

        auto it = fsm->triggerIdsByName.find(triggerName);
        if (it == fsm->triggerIdsByName.end()) return;

        for (int triggerId : it->second) {
            auto trigIt = fsm->triggers.find(triggerId);
            if (trigIt == fsm->triggers.end()) continue;

            auto& trigger = trigIt->second;
            trigger.lastData = data;

            // Check if trigger applies to current state
            if (trigger.fromState < 0 || trigger.fromState == fsm->currentState) {
                if (fsm->debugEnabled) {
                    TraceLog(LOG_INFO, "FSM[%s]: Trigger '%s' fired", fsm->name.c_str(), triggerName);
                }
                FSMPerformTransition(fsm, trigger.toState);
                return;  // Only first matching trigger
            }
        }
    }

    void Framework_FSM_RemoveTrigger(int fsmId, int triggerId) {
        auto* fsm = GetFSM(fsmId);
        if (!fsm) return;

        auto it = fsm->triggers.find(triggerId);
        if (it == fsm->triggers.end()) return;

        // Remove from name lookup
        auto& triggerList = fsm->triggerIdsByName[it->second.name];
        triggerList.erase(std::remove(triggerList.begin(), triggerList.end(), triggerId), triggerList.end());
        if (triggerList.empty()) {
            fsm->triggerIdsByName.erase(it->second.name);
        }

        fsm->triggers.erase(triggerId);
    }

    // Update
    void Framework_FSM_Update(int fsmId, float deltaTime) {
        if (g_fsmGlobalPaused) return;

        auto* fsm = GetFSM(fsmId);
        if (!fsm || !fsm->running || fsm->paused) return;

        fsm->timeInState += deltaTime;

        // Check auto-transitions
        for (auto& pair : fsm->transitions) {
            auto& t = pair.second;
            if ((t.fromState == fsm->currentState || t.isAnyState) && t.condition) {
                if (t.condition(fsmId, fsm->currentState, t.toState, t.conditionUserData)) {
                    FSMPerformTransition(fsm, t.toState);
                    break;  // Only one transition per frame
                }
            }
        }

        // Update current state
        auto* state = GetFSMState(fsm, fsm->currentState);
        if (state && state->updateCallback) {
            state->updateCallback(fsm->id, fsm->currentState, deltaTime, state->updateUserData);
        }
    }

    void Framework_FSM_UpdateAll(float deltaTime) {
        if (g_fsmGlobalPaused) return;

        for (auto& pair : g_fsms) {
            Framework_FSM_Update(pair.first, deltaTime);
        }
    }

    // Global FSM management
    int Framework_FSM_GetCount() {
        return (int)g_fsms.size();
    }

    void Framework_FSM_DestroyAll() {
        // Stop all first
        for (auto& pair : g_fsms) {
            if (pair.second.running) {
                pair.second.running = false;
            }
        }

        g_fsms.clear();
        g_fsmIdByName.clear();
        g_fsmIdByEntity.clear();
    }

    void Framework_FSM_PauseAll() {
        g_fsmGlobalPaused = true;
    }

    void Framework_FSM_ResumeAll() {
        g_fsmGlobalPaused = false;
    }

    // Debug
    void Framework_FSM_SetDebugEnabled(int fsmId, bool enabled) {
        auto* fsm = GetFSM(fsmId);
        if (fsm) {
            fsm->debugEnabled = enabled;
        }
    }

    bool Framework_FSM_GetDebugEnabled(int fsmId) {
        auto* fsm = GetFSM(fsmId);
        return fsm ? fsm->debugEnabled : false;
    }

    // ========================================================================
    // AI & PATHFINDING SYSTEM
    // ========================================================================

    struct NavCell {
        bool walkable = true;
        float cost = 1.0f;
    };

    struct NavGrid {
        int id;
        int width;
        int height;
        float cellSize;
        float originX = 0;
        float originY = 0;
        std::vector<NavCell> cells;
        bool diagonalEnabled = true;
        float diagonalCost = 1.414f;
        int heuristic = 1;  // 0=Manhattan, 1=Euclidean, 2=Chebyshev
    };

    struct PathWaypoint {
        float x, y;
    };

    struct NavPath {
        int id;
        std::vector<PathWaypoint> waypoints;
        float totalDistance = 0;
    };

    struct BehaviorConfig {
        bool enabled = false;
        float weight = 1.0f;
    };

    struct SteeringAgent {
        int id;
        int entity;
        float maxSpeed = 100.0f;
        float maxForce = 50.0f;
        float mass = 1.0f;
        float velocityX = 0;
        float velocityY = 0;
        float steeringX = 0;
        float steeringY = 0;

        // Target
        float targetX = 0;
        float targetY = 0;
        int targetEntity = -1;

        // Path following
        int pathId = -1;
        int currentWaypoint = 0;
        float pathOffset = 20.0f;
        bool reachedTarget = false;
        bool reachedPathEnd = false;

        // Arrive behavior
        float slowingRadius = 50.0f;

        // Wander behavior
        float wanderRadius = 30.0f;
        float wanderDistance = 50.0f;
        float wanderJitter = 20.0f;
        float wanderAngle = 0;

        // Flocking
        float neighborRadius = 100.0f;
        float separationRadius = 30.0f;

        // Obstacle avoidance
        float avoidanceRadius = 50.0f;
        float avoidanceForce = 100.0f;

        // Behaviors
        BehaviorConfig behaviors[12];

        bool debugEnabled = false;
    };

    static std::unordered_map<int, NavGrid> g_navGrids;
    static int g_nextNavGridId = 1;

    static std::unordered_map<int, NavPath> g_navPaths;
    static int g_nextPathId = 1;

    static std::unordered_map<int, SteeringAgent> g_steerAgents;
    static std::unordered_map<int, int> g_agentByEntity;
    static int g_nextAgentId = 1;

    static NavGrid* GetNavGrid(int gridId) {
        auto it = g_navGrids.find(gridId);
        return (it != g_navGrids.end()) ? &it->second : nullptr;
    }

    static NavPath* GetNavPath(int pathId) {
        auto it = g_navPaths.find(pathId);
        return (it != g_navPaths.end()) ? &it->second : nullptr;
    }

    static SteeringAgent* GetSteerAgent(int agentId) {
        auto it = g_steerAgents.find(agentId);
        return (it != g_steerAgents.end()) ? &it->second : nullptr;
    }

    // Navigation grid functions
    int Framework_NavGrid_Create(int width, int height, float cellSize) {
        NavGrid grid;
        grid.id = g_nextNavGridId++;
        grid.width = width;
        grid.height = height;
        grid.cellSize = cellSize;
        grid.cells.resize(width * height);

        g_navGrids[grid.id] = grid;
        return grid.id;
    }

    void Framework_NavGrid_Destroy(int gridId) {
        g_navGrids.erase(gridId);
    }

    bool Framework_NavGrid_IsValid(int gridId) {
        return GetNavGrid(gridId) != nullptr;
    }

    void Framework_NavGrid_SetOrigin(int gridId, float x, float y) {
        auto* grid = GetNavGrid(gridId);
        if (grid) {
            grid->originX = x;
            grid->originY = y;
        }
    }

    void Framework_NavGrid_GetOrigin(int gridId, float* outX, float* outY) {
        auto* grid = GetNavGrid(gridId);
        if (grid) {
            if (outX) *outX = grid->originX;
            if (outY) *outY = grid->originY;
        }
    }

    void Framework_NavGrid_SetWalkable(int gridId, int cellX, int cellY, bool walkable) {
        auto* grid = GetNavGrid(gridId);
        if (!grid || cellX < 0 || cellX >= grid->width || cellY < 0 || cellY >= grid->height) return;
        grid->cells[cellY * grid->width + cellX].walkable = walkable;
    }

    bool Framework_NavGrid_IsWalkable(int gridId, int cellX, int cellY) {
        auto* grid = GetNavGrid(gridId);
        if (!grid || cellX < 0 || cellX >= grid->width || cellY < 0 || cellY >= grid->height) return false;
        return grid->cells[cellY * grid->width + cellX].walkable;
    }

    void Framework_NavGrid_SetCost(int gridId, int cellX, int cellY, float cost) {
        auto* grid = GetNavGrid(gridId);
        if (!grid || cellX < 0 || cellX >= grid->width || cellY < 0 || cellY >= grid->height) return;
        grid->cells[cellY * grid->width + cellX].cost = cost;
    }

    float Framework_NavGrid_GetCost(int gridId, int cellX, int cellY) {
        auto* grid = GetNavGrid(gridId);
        if (!grid || cellX < 0 || cellX >= grid->width || cellY < 0 || cellY >= grid->height) return 1.0f;
        return grid->cells[cellY * grid->width + cellX].cost;
    }

    void Framework_NavGrid_SetAllWalkable(int gridId, bool walkable) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;
        for (auto& cell : grid->cells) cell.walkable = walkable;
    }

    void Framework_NavGrid_SetRect(int gridId, int x, int y, int w, int h, bool walkable) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;
        for (int cy = y; cy < y + h && cy < grid->height; cy++) {
            for (int cx = x; cx < x + w && cx < grid->width; cx++) {
                if (cx >= 0 && cy >= 0) {
                    grid->cells[cy * grid->width + cx].walkable = walkable;
                }
            }
        }
    }

    void Framework_NavGrid_SetCircle(int gridId, int centerX, int centerY, int radius, bool walkable) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;
        int r2 = radius * radius;
        for (int cy = centerY - radius; cy <= centerY + radius; cy++) {
            for (int cx = centerX - radius; cx <= centerX + radius; cx++) {
                if (cx >= 0 && cx < grid->width && cy >= 0 && cy < grid->height) {
                    int dx = cx - centerX;
                    int dy = cy - centerY;
                    if (dx * dx + dy * dy <= r2) {
                        grid->cells[cy * grid->width + cx].walkable = walkable;
                    }
                }
            }
        }
    }

    void Framework_NavGrid_WorldToCell(int gridId, float worldX, float worldY, int* outCellX, int* outCellY) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;
        if (outCellX) *outCellX = (int)((worldX - grid->originX) / grid->cellSize);
        if (outCellY) *outCellY = (int)((worldY - grid->originY) / grid->cellSize);
    }

    void Framework_NavGrid_CellToWorld(int gridId, int cellX, int cellY, float* outWorldX, float* outWorldY) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;
        if (outWorldX) *outWorldX = grid->originX + cellX * grid->cellSize + grid->cellSize * 0.5f;
        if (outWorldY) *outWorldY = grid->originY + cellY * grid->cellSize + grid->cellSize * 0.5f;
    }

    bool Framework_NavGrid_IsWorldPosWalkable(int gridId, float worldX, float worldY) {
        int cellX, cellY;
        Framework_NavGrid_WorldToCell(gridId, worldX, worldY, &cellX, &cellY);
        return Framework_NavGrid_IsWalkable(gridId, cellX, cellY);
    }

    // A* Pathfinding helper
    struct AStarNode {
        int x, y;
        float g, h, f;
        int parentX, parentY;
        bool operator>(const AStarNode& other) const { return f > other.f; }
    };

    static float Heuristic(int x1, int y1, int x2, int y2, int type) {
        float dx = (float)abs(x2 - x1);
        float dy = (float)abs(y2 - y1);
        switch (type) {
            case 0: return dx + dy;  // Manhattan
            case 1: return sqrtf(dx * dx + dy * dy);  // Euclidean
            case 2: return fmaxf(dx, dy);  // Chebyshev
            default: return sqrtf(dx * dx + dy * dy);
        }
    }

    int Framework_Path_FindCell(int gridId, int startCellX, int startCellY, int endCellX, int endCellY) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return -1;

        // Validate start and end
        if (!Framework_NavGrid_IsWalkable(gridId, startCellX, startCellY)) return -1;
        if (!Framework_NavGrid_IsWalkable(gridId, endCellX, endCellY)) return -1;

        // A* implementation
        std::priority_queue<AStarNode, std::vector<AStarNode>, std::greater<AStarNode>> openList;
        std::unordered_map<int, AStarNode> allNodes;
        std::unordered_set<int> closedSet;

        auto nodeKey = [&](int x, int y) { return y * grid->width + x; };

        AStarNode start;
        start.x = startCellX; start.y = startCellY;
        start.g = 0;
        start.h = Heuristic(startCellX, startCellY, endCellX, endCellY, grid->heuristic);
        start.f = start.g + start.h;
        start.parentX = -1; start.parentY = -1;

        openList.push(start);
        allNodes[nodeKey(startCellX, startCellY)] = start;

        int dx[] = { 0, 1, 0, -1, 1, 1, -1, -1 };
        int dy[] = { -1, 0, 1, 0, -1, 1, 1, -1 };
        int dirCount = grid->diagonalEnabled ? 8 : 4;

        while (!openList.empty()) {
            AStarNode current = openList.top();
            openList.pop();

            if (current.x == endCellX && current.y == endCellY) {
                // Reconstruct path
                NavPath path;
                path.id = g_nextPathId++;

                int cx = endCellX, cy = endCellY;
                while (cx != -1 && cy != -1) {
                    float wx, wy;
                    Framework_NavGrid_CellToWorld(gridId, cx, cy, &wx, &wy);
                    path.waypoints.insert(path.waypoints.begin(), PathWaypoint{ wx, wy });

                    int key = nodeKey(cx, cy);
                    auto it = allNodes.find(key);
                    if (it != allNodes.end()) {
                        cx = it->second.parentX;
                        cy = it->second.parentY;
                    } else {
                        break;
                    }
                }

                // Calculate total distance
                path.totalDistance = 0;
                for (size_t i = 1; i < path.waypoints.size(); i++) {
                    float dx = path.waypoints[i].x - path.waypoints[i - 1].x;
                    float dy = path.waypoints[i].y - path.waypoints[i - 1].y;
                    path.totalDistance += sqrtf(dx * dx + dy * dy);
                }

                g_navPaths[path.id] = path;
                return path.id;
            }

            closedSet.insert(nodeKey(current.x, current.y));

            for (int i = 0; i < dirCount; i++) {
                int nx = current.x + dx[i];
                int ny = current.y + dy[i];

                if (nx < 0 || nx >= grid->width || ny < 0 || ny >= grid->height) continue;
                if (!grid->cells[ny * grid->width + nx].walkable) continue;
                if (closedSet.count(nodeKey(nx, ny))) continue;

                // Check diagonal corners
                if (i >= 4) {
                    if (!Framework_NavGrid_IsWalkable(gridId, current.x + dx[i], current.y) ||
                        !Framework_NavGrid_IsWalkable(gridId, current.x, current.y + dy[i])) continue;
                }

                float moveCost = (i >= 4) ? grid->diagonalCost : 1.0f;
                float newG = current.g + moveCost * grid->cells[ny * grid->width + nx].cost;

                int nKey = nodeKey(nx, ny);
                auto it = allNodes.find(nKey);
                if (it == allNodes.end() || newG < it->second.g) {
                    AStarNode neighbor;
                    neighbor.x = nx; neighbor.y = ny;
                    neighbor.g = newG;
                    neighbor.h = Heuristic(nx, ny, endCellX, endCellY, grid->heuristic);
                    neighbor.f = neighbor.g + neighbor.h;
                    neighbor.parentX = current.x;
                    neighbor.parentY = current.y;

                    allNodes[nKey] = neighbor;
                    openList.push(neighbor);
                }
            }
        }

        return -1;  // No path found
    }

    int Framework_Path_Find(int gridId, float startX, float startY, float endX, float endY) {
        int startCellX, startCellY, endCellX, endCellY;
        Framework_NavGrid_WorldToCell(gridId, startX, startY, &startCellX, &startCellY);
        Framework_NavGrid_WorldToCell(gridId, endX, endY, &endCellX, &endCellY);
        return Framework_Path_FindCell(gridId, startCellX, startCellY, endCellX, endCellY);
    }

    void Framework_Path_Destroy(int pathId) {
        g_navPaths.erase(pathId);
    }

    bool Framework_Path_IsValid(int pathId) {
        return GetNavPath(pathId) != nullptr;
    }

    int Framework_Path_GetLength(int pathId) {
        auto* path = GetNavPath(pathId);
        return path ? (int)path->waypoints.size() : 0;
    }

    void Framework_Path_GetWaypoint(int pathId, int index, float* outX, float* outY) {
        auto* path = GetNavPath(pathId);
        if (!path || index < 0 || index >= (int)path->waypoints.size()) return;
        if (outX) *outX = path->waypoints[index].x;
        if (outY) *outY = path->waypoints[index].y;
    }

    float Framework_Path_GetTotalDistance(int pathId) {
        auto* path = GetNavPath(pathId);
        return path ? path->totalDistance : 0;
    }

    void Framework_Path_Smooth(int pathId) {
        auto* path = GetNavPath(pathId);
        if (!path || path->waypoints.size() < 3) return;

        std::vector<PathWaypoint> smoothed;
        smoothed.push_back(path->waypoints[0]);

        for (size_t i = 1; i < path->waypoints.size() - 1; i++) {
            // Simple averaging
            PathWaypoint wp;
            wp.x = (path->waypoints[i - 1].x + path->waypoints[i].x + path->waypoints[i + 1].x) / 3.0f;
            wp.y = (path->waypoints[i - 1].y + path->waypoints[i].y + path->waypoints[i + 1].y) / 3.0f;
            smoothed.push_back(wp);
        }

        smoothed.push_back(path->waypoints.back());
        path->waypoints = smoothed;

        // Recalculate distance
        path->totalDistance = 0;
        for (size_t i = 1; i < path->waypoints.size(); i++) {
            float dx = path->waypoints[i].x - path->waypoints[i - 1].x;
            float dy = path->waypoints[i].y - path->waypoints[i - 1].y;
            path->totalDistance += sqrtf(dx * dx + dy * dy);
        }
    }

    void Framework_Path_SimplifyRDP(int pathId, float epsilon) {
        auto* path = GetNavPath(pathId);
        if (!path || path->waypoints.size() < 3) return;

        std::function<void(int, int, std::vector<bool>&)> rdp = [&](int start, int end, std::vector<bool>& keep) {
            float maxDist = 0;
            int maxIdx = start;

            float x1 = path->waypoints[start].x, y1 = path->waypoints[start].y;
            float x2 = path->waypoints[end].x, y2 = path->waypoints[end].y;
            float dx = x2 - x1, dy = y2 - y1;
            float len = sqrtf(dx * dx + dy * dy);

            for (int i = start + 1; i < end; i++) {
                float dist;
                if (len < 0.0001f) {
                    dist = sqrtf((path->waypoints[i].x - x1) * (path->waypoints[i].x - x1) +
                                 (path->waypoints[i].y - y1) * (path->waypoints[i].y - y1));
                } else {
                    float t = ((path->waypoints[i].x - x1) * dx + (path->waypoints[i].y - y1) * dy) / (len * len);
                    t = fmaxf(0, fminf(1, t));
                    float projX = x1 + t * dx, projY = y1 + t * dy;
                    dist = sqrtf((path->waypoints[i].x - projX) * (path->waypoints[i].x - projX) +
                                 (path->waypoints[i].y - projY) * (path->waypoints[i].y - projY));
                }
                if (dist > maxDist) {
                    maxDist = dist;
                    maxIdx = i;
                }
            }

            if (maxDist > epsilon) {
                keep[maxIdx] = true;
                rdp(start, maxIdx, keep);
                rdp(maxIdx, end, keep);
            }
        };

        std::vector<bool> keep(path->waypoints.size(), false);
        keep[0] = true;
        keep[path->waypoints.size() - 1] = true;
        rdp(0, (int)path->waypoints.size() - 1, keep);

        std::vector<PathWaypoint> simplified;
        for (size_t i = 0; i < path->waypoints.size(); i++) {
            if (keep[i]) simplified.push_back(path->waypoints[i]);
        }
        path->waypoints = simplified;

        // Recalculate distance
        path->totalDistance = 0;
        for (size_t i = 1; i < path->waypoints.size(); i++) {
            float dx = path->waypoints[i].x - path->waypoints[i - 1].x;
            float dy = path->waypoints[i].y - path->waypoints[i - 1].y;
            path->totalDistance += sqrtf(dx * dx + dy * dy);
        }
    }

    void Framework_Path_SetDiagonalEnabled(int gridId, bool enabled) {
        auto* grid = GetNavGrid(gridId);
        if (grid) grid->diagonalEnabled = enabled;
    }

    void Framework_Path_SetDiagonalCost(int gridId, float cost) {
        auto* grid = GetNavGrid(gridId);
        if (grid) grid->diagonalCost = cost;
    }

    void Framework_Path_SetHeuristic(int gridId, int heuristic) {
        auto* grid = GetNavGrid(gridId);
        if (grid) grid->heuristic = heuristic;
    }

    // Steering agent functions
    int Framework_Steer_CreateAgent(int entity) {
        SteeringAgent agent;
        agent.id = g_nextAgentId++;
        agent.entity = entity;

        g_steerAgents[agent.id] = agent;
        g_agentByEntity[entity] = agent.id;
        return agent.id;
    }

    void Framework_Steer_DestroyAgent(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            g_agentByEntity.erase(agent->entity);
            g_steerAgents.erase(agentId);
        }
    }

    int Framework_Steer_GetAgentForEntity(int entity) {
        auto it = g_agentByEntity.find(entity);
        return (it != g_agentByEntity.end()) ? it->second : -1;
    }

    bool Framework_Steer_IsAgentValid(int agentId) {
        return GetSteerAgent(agentId) != nullptr;
    }

    void Framework_Steer_SetMaxSpeed(int agentId, float maxSpeed) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->maxSpeed = maxSpeed;
    }

    float Framework_Steer_GetMaxSpeed(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->maxSpeed : 0;
    }

    void Framework_Steer_SetMaxForce(int agentId, float maxForce) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->maxForce = maxForce;
    }

    float Framework_Steer_GetMaxForce(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->maxForce : 0;
    }

    void Framework_Steer_SetMass(int agentId, float mass) {
        auto* agent = GetSteerAgent(agentId);
        if (agent && mass > 0) agent->mass = mass;
    }

    float Framework_Steer_GetMass(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->mass : 1.0f;
    }

    void Framework_Steer_SetSlowingRadius(int agentId, float radius) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->slowingRadius = radius;
    }

    void Framework_Steer_SetWanderRadius(int agentId, float radius) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->wanderRadius = radius;
    }

    void Framework_Steer_SetWanderDistance(int agentId, float distance) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->wanderDistance = distance;
    }

    void Framework_Steer_SetWanderJitter(int agentId, float jitter) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->wanderJitter = jitter;
    }

    void Framework_Steer_GetVelocity(int agentId, float* outX, float* outY) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            if (outX) *outX = agent->velocityX;
            if (outY) *outY = agent->velocityY;
        }
    }

    void Framework_Steer_SetVelocity(int agentId, float x, float y) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            agent->velocityX = x;
            agent->velocityY = y;
        }
    }

    void Framework_Steer_EnableBehavior(int agentId, int behavior, bool enabled) {
        auto* agent = GetSteerAgent(agentId);
        if (agent && behavior >= 0 && behavior < 12) {
            agent->behaviors[behavior].enabled = enabled;
        }
    }

    bool Framework_Steer_IsBehaviorEnabled(int agentId, int behavior) {
        auto* agent = GetSteerAgent(agentId);
        if (agent && behavior >= 0 && behavior < 12) {
            return agent->behaviors[behavior].enabled;
        }
        return false;
    }

    void Framework_Steer_SetBehaviorWeight(int agentId, int behavior, float weight) {
        auto* agent = GetSteerAgent(agentId);
        if (agent && behavior >= 0 && behavior < 12) {
            agent->behaviors[behavior].weight = weight;
        }
    }

    float Framework_Steer_GetBehaviorWeight(int agentId, int behavior) {
        auto* agent = GetSteerAgent(agentId);
        if (agent && behavior >= 0 && behavior < 12) {
            return agent->behaviors[behavior].weight;
        }
        return 1.0f;
    }

    void Framework_Steer_SetTargetPosition(int agentId, float x, float y) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            agent->targetX = x;
            agent->targetY = y;
            agent->targetEntity = -1;
        }
    }

    void Framework_Steer_SetTargetEntity(int agentId, int targetEntity) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            agent->targetEntity = targetEntity;
        }
    }

    void Framework_Steer_SetPath(int agentId, int pathId) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            agent->pathId = pathId;
            agent->currentWaypoint = 0;
            agent->reachedPathEnd = false;
        }
    }

    void Framework_Steer_SetPathOffset(int agentId, float offset) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->pathOffset = offset;
    }

    void Framework_Steer_SetNeighborRadius(int agentId, float radius) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->neighborRadius = radius;
    }

    void Framework_Steer_SetSeparationRadius(int agentId, float radius) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->separationRadius = radius;
    }

    void Framework_Steer_SetAvoidanceRadius(int agentId, float radius) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->avoidanceRadius = radius;
    }

    void Framework_Steer_SetAvoidanceForce(int agentId, float force) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->avoidanceForce = force;
    }

    static Vector2 Truncate(Vector2 v, float max) {
        float len = sqrtf(v.x * v.x + v.y * v.y);
        if (len > max && len > 0) {
            v.x = v.x / len * max;
            v.y = v.y / len * max;
        }
        return v;
    }

    static Vector2 Normalize(Vector2 v) {
        float len = sqrtf(v.x * v.x + v.y * v.y);
        if (len > 0) {
            v.x /= len;
            v.y /= len;
        }
        return v;
    }

    void Framework_Steer_Update(int agentId, float deltaTime) {
        auto* agent = GetSteerAgent(agentId);
        if (!agent) return;

        // Get agent position from entity
        Vector2 pos = GetWorldPositionInternal(agent->entity);
        Vector2 steering = { 0, 0 };

        // Get target position
        Vector2 target = { agent->targetX, agent->targetY };
        if (agent->targetEntity >= 0) {
            target = GetWorldPositionInternal(agent->targetEntity);
            agent->targetX = target.x;
            agent->targetY = target.y;
        }

        // Seek
        if (agent->behaviors[STEER_SEEK].enabled) {
            Vector2 desired = { target.x - pos.x, target.y - pos.y };
            desired = Normalize(desired);
            desired.x *= agent->maxSpeed;
            desired.y *= agent->maxSpeed;
            Vector2 force = { desired.x - agent->velocityX, desired.y - agent->velocityY };
            force.x *= agent->behaviors[STEER_SEEK].weight;
            force.y *= agent->behaviors[STEER_SEEK].weight;
            steering.x += force.x;
            steering.y += force.y;
        }

        // Flee
        if (agent->behaviors[STEER_FLEE].enabled) {
            Vector2 desired = { pos.x - target.x, pos.y - target.y };
            desired = Normalize(desired);
            desired.x *= agent->maxSpeed;
            desired.y *= agent->maxSpeed;
            Vector2 force = { desired.x - agent->velocityX, desired.y - agent->velocityY };
            force.x *= agent->behaviors[STEER_FLEE].weight;
            force.y *= agent->behaviors[STEER_FLEE].weight;
            steering.x += force.x;
            steering.y += force.y;
        }

        // Arrive
        if (agent->behaviors[STEER_ARRIVE].enabled) {
            Vector2 toTarget = { target.x - pos.x, target.y - pos.y };
            float dist = sqrtf(toTarget.x * toTarget.x + toTarget.y * toTarget.y);

            if (dist > 0.1f) {
                float speed = agent->maxSpeed;
                if (dist < agent->slowingRadius) {
                    speed = agent->maxSpeed * (dist / agent->slowingRadius);
                }
                Vector2 desired = Normalize(toTarget);
                desired.x *= speed;
                desired.y *= speed;
                Vector2 force = { desired.x - agent->velocityX, desired.y - agent->velocityY };
                force.x *= agent->behaviors[STEER_ARRIVE].weight;
                force.y *= agent->behaviors[STEER_ARRIVE].weight;
                steering.x += force.x;
                steering.y += force.y;
            }

            agent->reachedTarget = dist < 5.0f;
        }

        // Wander
        if (agent->behaviors[STEER_WANDER].enabled) {
            agent->wanderAngle += ((float)rand() / RAND_MAX - 0.5f) * agent->wanderJitter;
            float circleX = agent->velocityX;
            float circleY = agent->velocityY;
            Vector2 circleDir = Normalize(Vector2{ circleX, circleY });
            if (circleDir.x == 0 && circleDir.y == 0) {
                circleDir.x = 1;
            }
            float cx = pos.x + circleDir.x * agent->wanderDistance;
            float cy = pos.y + circleDir.y * agent->wanderDistance;
            float tx = cx + cosf(agent->wanderAngle) * agent->wanderRadius;
            float ty = cy + sinf(agent->wanderAngle) * agent->wanderRadius;

            Vector2 desired = { tx - pos.x, ty - pos.y };
            desired = Normalize(desired);
            desired.x *= agent->maxSpeed;
            desired.y *= agent->maxSpeed;
            Vector2 force = { desired.x - agent->velocityX, desired.y - agent->velocityY };
            force.x *= agent->behaviors[STEER_WANDER].weight;
            force.y *= agent->behaviors[STEER_WANDER].weight;
            steering.x += force.x;
            steering.y += force.y;
        }

        // Path follow
        if (agent->behaviors[STEER_PATH_FOLLOW].enabled && agent->pathId >= 0) {
            auto* path = GetNavPath(agent->pathId);
            if (path && !path->waypoints.empty() && agent->currentWaypoint < (int)path->waypoints.size()) {
                Vector2 wp = { path->waypoints[agent->currentWaypoint].x, path->waypoints[agent->currentWaypoint].y };
                float dx = wp.x - pos.x;
                float dy = wp.y - pos.y;
                float dist = sqrtf(dx * dx + dy * dy);

                if (dist < agent->pathOffset) {
                    agent->currentWaypoint++;
                    if (agent->currentWaypoint >= (int)path->waypoints.size()) {
                        agent->reachedPathEnd = true;
                    }
                }

                if (!agent->reachedPathEnd) {
                    Vector2 desired = { wp.x - pos.x, wp.y - pos.y };
                    desired = Normalize(desired);
                    desired.x *= agent->maxSpeed;
                    desired.y *= agent->maxSpeed;
                    Vector2 force = { desired.x - agent->velocityX, desired.y - agent->velocityY };
                    force.x *= agent->behaviors[STEER_PATH_FOLLOW].weight;
                    force.y *= agent->behaviors[STEER_PATH_FOLLOW].weight;
                    steering.x += force.x;
                    steering.y += force.y;
                }
            }
        }

        // Truncate and apply steering
        steering = Truncate(steering, agent->maxForce);
        steering.x /= agent->mass;
        steering.y /= agent->mass;

        agent->steeringX = steering.x;
        agent->steeringY = steering.y;

        // Update velocity
        agent->velocityX += steering.x * deltaTime;
        agent->velocityY += steering.y * deltaTime;
        Vector2 vel = Truncate(Vector2{ agent->velocityX, agent->velocityY }, agent->maxSpeed);
        agent->velocityX = vel.x;
        agent->velocityY = vel.y;

        // Update entity position
        auto it = g_transform2D.find(agent->entity);
        if (it != g_transform2D.end()) {
            it->second.position.x += agent->velocityX * deltaTime;
            it->second.position.y += agent->velocityY * deltaTime;
        }
    }

    void Framework_Steer_UpdateAll(float deltaTime) {
        for (auto& pair : g_steerAgents) {
            Framework_Steer_Update(pair.first, deltaTime);
        }
    }

    void Framework_Steer_GetSteeringForce(int agentId, float* outX, float* outY) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            if (outX) *outX = agent->steeringX;
            if (outY) *outY = agent->steeringY;
        }
    }

    int Framework_Steer_GetCurrentWaypoint(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->currentWaypoint : 0;
    }

    bool Framework_Steer_HasReachedTarget(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->reachedTarget : false;
    }

    bool Framework_Steer_HasReachedPathEnd(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        return agent ? agent->reachedPathEnd : false;
    }

    void Framework_Steer_ResetPath(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) {
            agent->currentWaypoint = 0;
            agent->reachedPathEnd = false;
        }
    }

    // Debug visualization
    void Framework_NavGrid_DrawDebug(int gridId) {
        auto* grid = GetNavGrid(gridId);
        if (!grid) return;

        for (int y = 0; y < grid->height; y++) {
            for (int x = 0; x < grid->width; x++) {
                float wx = grid->originX + x * grid->cellSize;
                float wy = grid->originY + y * grid->cellSize;
                Color col = grid->cells[y * grid->width + x].walkable ? Color{ 0, 100, 0, 50 } : Color{ 100, 0, 0, 100 };
                DrawRectangle((int)wx, (int)wy, (int)grid->cellSize - 1, (int)grid->cellSize - 1, col);
            }
        }
    }

    void Framework_Path_DrawDebug(int pathId, unsigned char r, unsigned char g, unsigned char b) {
        auto* path = GetNavPath(pathId);
        if (!path || path->waypoints.size() < 2) return;

        Color col = { r, g, b, 255 };
        for (size_t i = 0; i < path->waypoints.size() - 1; i++) {
            DrawLineV(Vector2{ path->waypoints[i].x, path->waypoints[i].y },
                      Vector2{ path->waypoints[i + 1].x, path->waypoints[i + 1].y }, col);
        }

        for (auto& wp : path->waypoints) {
            DrawCircle((int)wp.x, (int)wp.y, 3, col);
        }
    }

    void Framework_Steer_DrawDebug(int agentId) {
        auto* agent = GetSteerAgent(agentId);
        if (!agent || !agent->debugEnabled) return;

        Vector2 pos = GetWorldPositionInternal(agent->entity);

        // Draw velocity
        DrawLineV(pos, Vector2{ pos.x + agent->velocityX * 0.5f, pos.y + agent->velocityY * 0.5f }, GREEN);

        // Draw steering
        DrawLineV(pos, Vector2{ pos.x + agent->steeringX * 0.5f, pos.y + agent->steeringY * 0.5f }, RED);

        // Draw target
        DrawCircle((int)agent->targetX, (int)agent->targetY, 5, YELLOW);
    }

    void Framework_Steer_SetDebugEnabled(int agentId, bool enabled) {
        auto* agent = GetSteerAgent(agentId);
        if (agent) agent->debugEnabled = enabled;
    }

    // Global management
    void Framework_NavGrid_DestroyAll() {
        g_navGrids.clear();
    }

    void Framework_Path_DestroyAll() {
        g_navPaths.clear();
    }

    void Framework_Steer_DestroyAllAgents() {
        g_steerAgents.clear();
        g_agentByEntity.clear();
    }

    // ========================================================================
    // DIALOGUE SYSTEM
    // ========================================================================

    struct DialogueChoice {
        std::string text;
        int targetNodeId = -1;
        std::string condition;
    };

    struct DialogueNode {
        int id = 0;
        std::string tag;
        std::string speaker;
        std::string text;
        int portrait = -1;
        int nextNodeId = -1;
        std::string condition;
        std::string eventName;
        std::vector<DialogueChoice> choices;
    };

    struct Dialogue {
        int id = 0;
        std::string name;
        int startNodeId = -1;
        std::unordered_map<int, DialogueNode> nodes;
        int nextNodeId = 0;
    };

    struct Speaker {
        std::string id;
        std::string displayName;
        int portrait = -1;
    };

    struct DialogueHistoryEntry {
        std::string speaker;
        std::string text;
    };

    // Dialogue variable union for storage
    struct DialogueVar {
        enum Type { INT, FLOAT, BOOL, STRING } type = INT;
        int intVal = 0;
        float floatVal = 0.0f;
        bool boolVal = false;
        std::string strVal;
    };

    // Global dialogue state
    namespace {
        std::unordered_map<int, Dialogue> g_dialogues;
        std::unordered_map<std::string, int> g_dialogueByName;
        int g_nextDialogueId = 1;

        std::unordered_map<std::string, Speaker> g_speakers;
        std::unordered_map<std::string, DialogueVar> g_dialogueVars;

        // Active playback state
        int g_activeDialogueId = -1;
        int g_activeNodeId = -1;

        // Typewriter state
        bool g_typewriterEnabled = true;
        float g_typewriterSpeed = 30.0f;  // chars per second
        float g_typewriterProgress = 0.0f;
        bool g_typewriterComplete = false;
        std::string g_visibleText;

        // Callbacks
        DialogueCallback g_onDialogueStart = nullptr;
        DialogueCallback g_onDialogueEnd = nullptr;
        DialogueCallback g_onNodeEnter = nullptr;
        DialogueCallback g_onNodeExit = nullptr;
        DialogueChoiceCallback g_onChoice = nullptr;
        DialogueConditionCallback g_conditionHandler = nullptr;
        void* g_dialogueStartUserData = nullptr;
        void* g_dialogueEndUserData = nullptr;
        void* g_nodeEnterUserData = nullptr;
        void* g_nodeExitUserData = nullptr;
        void* g_choiceUserData = nullptr;
        void* g_conditionUserData = nullptr;

        // History
        bool g_historyEnabled = false;
        std::vector<DialogueHistoryEntry> g_dialogueHistory;

        // Static buffers for string returns
        static char s_dialogueSpeakerBuf[256] = {0};
        static char s_dialogueTextBuf[2048] = {0};
        static char s_dialogueChoiceBuf[512] = {0};
        static char s_dialogueVarBuf[512] = {0};
        static char s_dialogueVisibleBuf[2048] = {0};
    }

    Dialogue* GetDialogue(int id) {
        auto it = g_dialogues.find(id);
        return it != g_dialogues.end() ? &it->second : nullptr;
    }

    DialogueNode* GetDialogueNode(int dialogueId, int nodeId) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg) return nullptr;
        auto it = dlg->nodes.find(nodeId);
        return it != dlg->nodes.end() ? &it->second : nullptr;
    }

    // Dialogue creation and management
    int Framework_Dialogue_Create(const char* name) {
        Dialogue dlg;
        dlg.id = g_nextDialogueId++;
        dlg.name = name ? name : "";
        g_dialogues[dlg.id] = dlg;
        if (name && strlen(name) > 0) {
            g_dialogueByName[name] = dlg.id;
        }
        return dlg.id;
    }

    void Framework_Dialogue_Destroy(int dialogueId) {
        auto* dlg = GetDialogue(dialogueId);
        if (dlg) {
            if (!dlg->name.empty()) {
                g_dialogueByName.erase(dlg->name);
            }
            g_dialogues.erase(dialogueId);
            if (g_activeDialogueId == dialogueId) {
                g_activeDialogueId = -1;
                g_activeNodeId = -1;
            }
        }
    }

    int Framework_Dialogue_GetByName(const char* name) {
        if (!name) return -1;
        auto it = g_dialogueByName.find(name);
        return it != g_dialogueByName.end() ? it->second : -1;
    }

    bool Framework_Dialogue_IsValid(int dialogueId) {
        return GetDialogue(dialogueId) != nullptr;
    }

    void Framework_Dialogue_Clear(int dialogueId) {
        auto* dlg = GetDialogue(dialogueId);
        if (dlg) {
            dlg->nodes.clear();
            dlg->startNodeId = -1;
            dlg->nextNodeId = 0;
        }
    }

    // Node creation
    int Framework_Dialogue_AddNode(int dialogueId, const char* nodeTag) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg) return -1;

        DialogueNode node;
        node.id = dlg->nextNodeId++;
        node.tag = nodeTag ? nodeTag : "";
        dlg->nodes[node.id] = node;

        if (dlg->startNodeId < 0) {
            dlg->startNodeId = node.id;
        }

        return node.id;
    }

    void Framework_Dialogue_RemoveNode(int dialogueId, int nodeId) {
        auto* dlg = GetDialogue(dialogueId);
        if (dlg) {
            dlg->nodes.erase(nodeId);
        }
    }

    int Framework_Dialogue_GetNodeByTag(int dialogueId, const char* tag) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg || !tag) return -1;

        for (auto& kv : dlg->nodes) {
            if (kv.second.tag == tag) return kv.first;
        }
        return -1;
    }

    int Framework_Dialogue_GetNodeCount(int dialogueId) {
        auto* dlg = GetDialogue(dialogueId);
        return dlg ? (int)dlg->nodes.size() : 0;
    }

    // Node content
    void Framework_Dialogue_SetNodeSpeaker(int dialogueId, int nodeId, const char* speaker) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->speaker = speaker ? speaker : "";
    }

    const char* Framework_Dialogue_GetNodeSpeaker(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && !node->speaker.empty()) {
            strncpy(s_dialogueSpeakerBuf, node->speaker.c_str(), sizeof(s_dialogueSpeakerBuf) - 1);
            return s_dialogueSpeakerBuf;
        }
        return "";
    }

    void Framework_Dialogue_SetNodeText(int dialogueId, int nodeId, const char* text) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->text = text ? text : "";
    }

    const char* Framework_Dialogue_GetNodeText(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && !node->text.empty()) {
            strncpy(s_dialogueTextBuf, node->text.c_str(), sizeof(s_dialogueTextBuf) - 1);
            return s_dialogueTextBuf;
        }
        return "";
    }

    void Framework_Dialogue_SetNodePortrait(int dialogueId, int nodeId, int textureHandle) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->portrait = textureHandle;
    }

    int Framework_Dialogue_GetNodePortrait(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        return node ? node->portrait : -1;
    }

    // Node connections
    void Framework_Dialogue_SetNextNode(int dialogueId, int nodeId, int nextNodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->nextNodeId = nextNodeId;
    }

    int Framework_Dialogue_GetNextNode(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        return node ? node->nextNodeId : -1;
    }

    void Framework_Dialogue_SetStartNode(int dialogueId, int nodeId) {
        auto* dlg = GetDialogue(dialogueId);
        if (dlg) dlg->startNodeId = nodeId;
    }

    int Framework_Dialogue_GetStartNode(int dialogueId) {
        auto* dlg = GetDialogue(dialogueId);
        return dlg ? dlg->startNodeId : -1;
    }

    // Choices
    int Framework_Dialogue_AddChoice(int dialogueId, int nodeId, const char* choiceText, int targetNodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (!node) return -1;

        DialogueChoice choice;
        choice.text = choiceText ? choiceText : "";
        choice.targetNodeId = targetNodeId;
        node->choices.push_back(choice);
        return (int)node->choices.size() - 1;
    }

    void Framework_Dialogue_RemoveChoice(int dialogueId, int nodeId, int choiceIndex) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            node->choices.erase(node->choices.begin() + choiceIndex);
        }
    }

    int Framework_Dialogue_GetChoiceCount(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        return node ? (int)node->choices.size() : 0;
    }

    const char* Framework_Dialogue_GetChoiceText(int dialogueId, int nodeId, int choiceIndex) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            strncpy(s_dialogueChoiceBuf, node->choices[choiceIndex].text.c_str(), sizeof(s_dialogueChoiceBuf) - 1);
            return s_dialogueChoiceBuf;
        }
        return "";
    }

    int Framework_Dialogue_GetChoiceTarget(int dialogueId, int nodeId, int choiceIndex) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            return node->choices[choiceIndex].targetNodeId;
        }
        return -1;
    }

    void Framework_Dialogue_SetChoiceCondition(int dialogueId, int nodeId, int choiceIndex, const char* condition) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            node->choices[choiceIndex].condition = condition ? condition : "";
        }
    }

    const char* Framework_Dialogue_GetChoiceCondition(int dialogueId, int nodeId, int choiceIndex) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            strncpy(s_dialogueChoiceBuf, node->choices[choiceIndex].condition.c_str(), sizeof(s_dialogueChoiceBuf) - 1);
            return s_dialogueChoiceBuf;
        }
        return "";
    }

    // Conditional nodes
    void Framework_Dialogue_SetNodeCondition(int dialogueId, int nodeId, const char* condition) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->condition = condition ? condition : "";
    }

    const char* Framework_Dialogue_GetNodeCondition(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && !node->condition.empty()) {
            strncpy(s_dialogueTextBuf, node->condition.c_str(), sizeof(s_dialogueTextBuf) - 1);
            return s_dialogueTextBuf;
        }
        return "";
    }

    // Node events
    void Framework_Dialogue_SetNodeEvent(int dialogueId, int nodeId, const char* eventName) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) node->eventName = eventName ? eventName : "";
    }

    const char* Framework_Dialogue_GetNodeEvent(int dialogueId, int nodeId) {
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node && !node->eventName.empty()) {
            strncpy(s_dialogueTextBuf, node->eventName.c_str(), sizeof(s_dialogueTextBuf) - 1);
            return s_dialogueTextBuf;
        }
        return "";
    }

    // Variables
    void Framework_Dialogue_SetVarInt(const char* varName, int value) {
        if (!varName) return;
        DialogueVar& v = g_dialogueVars[varName];
        v.type = DialogueVar::INT;
        v.intVal = value;
    }

    int Framework_Dialogue_GetVarInt(const char* varName) {
        if (!varName) return 0;
        auto it = g_dialogueVars.find(varName);
        if (it != g_dialogueVars.end() && it->second.type == DialogueVar::INT) {
            return it->second.intVal;
        }
        return 0;
    }

    void Framework_Dialogue_SetVarFloat(const char* varName, float value) {
        if (!varName) return;
        DialogueVar& v = g_dialogueVars[varName];
        v.type = DialogueVar::FLOAT;
        v.floatVal = value;
    }

    float Framework_Dialogue_GetVarFloat(const char* varName) {
        if (!varName) return 0.0f;
        auto it = g_dialogueVars.find(varName);
        if (it != g_dialogueVars.end() && it->second.type == DialogueVar::FLOAT) {
            return it->second.floatVal;
        }
        return 0.0f;
    }

    void Framework_Dialogue_SetVarBool(const char* varName, bool value) {
        if (!varName) return;
        DialogueVar& v = g_dialogueVars[varName];
        v.type = DialogueVar::BOOL;
        v.boolVal = value;
    }

    bool Framework_Dialogue_GetVarBool(const char* varName) {
        if (!varName) return false;
        auto it = g_dialogueVars.find(varName);
        if (it != g_dialogueVars.end() && it->second.type == DialogueVar::BOOL) {
            return it->second.boolVal;
        }
        return false;
    }

    void Framework_Dialogue_SetVarString(const char* varName, const char* value) {
        if (!varName) return;
        DialogueVar& v = g_dialogueVars[varName];
        v.type = DialogueVar::STRING;
        v.strVal = value ? value : "";
    }

    const char* Framework_Dialogue_GetVarString(const char* varName) {
        if (!varName) return "";
        auto it = g_dialogueVars.find(varName);
        if (it != g_dialogueVars.end() && it->second.type == DialogueVar::STRING) {
            strncpy(s_dialogueVarBuf, it->second.strVal.c_str(), sizeof(s_dialogueVarBuf) - 1);
            return s_dialogueVarBuf;
        }
        return "";
    }

    void Framework_Dialogue_ClearVar(const char* varName) {
        if (varName) g_dialogueVars.erase(varName);
    }

    void Framework_Dialogue_ClearAllVars() {
        g_dialogueVars.clear();
    }

    // Internal helper to enter a node
    void EnterDialogueNode(int dialogueId, int nodeId) {
        g_activeDialogueId = dialogueId;
        g_activeNodeId = nodeId;
        g_typewriterProgress = 0.0f;
        g_typewriterComplete = !g_typewriterEnabled;
        g_visibleText = "";

        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (node) {
            if (g_typewriterComplete) {
                g_visibleText = node->text;
            }

            // Add to history
            if (g_historyEnabled && !node->text.empty()) {
                DialogueHistoryEntry entry;
                entry.speaker = node->speaker;
                entry.text = node->text;
                g_dialogueHistory.push_back(entry);
            }

            // Trigger node enter callback
            if (g_onNodeEnter) {
                g_onNodeEnter(dialogueId, nodeId, g_nodeEnterUserData);
            }
        }
    }

    // Playback
    void Framework_Dialogue_Start(int dialogueId) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg || dlg->startNodeId < 0) return;

        if (g_onDialogueStart) {
            g_onDialogueStart(dialogueId, dlg->startNodeId, g_dialogueStartUserData);
        }

        EnterDialogueNode(dialogueId, dlg->startNodeId);
    }

    void Framework_Dialogue_StartAtNode(int dialogueId, int nodeId) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg) return;
        auto* node = GetDialogueNode(dialogueId, nodeId);
        if (!node) return;

        if (g_onDialogueStart) {
            g_onDialogueStart(dialogueId, nodeId, g_dialogueStartUserData);
        }

        EnterDialogueNode(dialogueId, nodeId);
    }

    void Framework_Dialogue_Stop() {
        if (g_activeDialogueId >= 0) {
            if (g_onDialogueEnd) {
                g_onDialogueEnd(g_activeDialogueId, g_activeNodeId, g_dialogueEndUserData);
            }
        }
        g_activeDialogueId = -1;
        g_activeNodeId = -1;
        g_typewriterProgress = 0.0f;
        g_typewriterComplete = false;
        g_visibleText = "";
    }

    bool Framework_Dialogue_IsActive() {
        return g_activeDialogueId >= 0;
    }

    int Framework_Dialogue_GetActiveDialogue() {
        return g_activeDialogueId;
    }

    int Framework_Dialogue_GetCurrentNode() {
        return g_activeNodeId;
    }

    bool Framework_Dialogue_Continue() {
        if (g_activeDialogueId < 0 || g_activeNodeId < 0) return false;

        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (!node) return false;

        // If has choices, cannot continue automatically
        if (!node->choices.empty()) return false;

        // Trigger node exit
        if (g_onNodeExit) {
            g_onNodeExit(g_activeDialogueId, g_activeNodeId, g_nodeExitUserData);
        }

        int nextId = node->nextNodeId;
        if (nextId < 0) {
            Framework_Dialogue_Stop();
            return false;
        }

        EnterDialogueNode(g_activeDialogueId, nextId);
        return true;
    }

    bool Framework_Dialogue_SelectChoice(int choiceIndex) {
        if (g_activeDialogueId < 0 || g_activeNodeId < 0) return false;

        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (!node || choiceIndex < 0 || choiceIndex >= (int)node->choices.size()) return false;

        // Check condition
        auto& choice = node->choices[choiceIndex];
        if (!choice.condition.empty() && g_conditionHandler) {
            if (!g_conditionHandler(g_activeDialogueId, choice.condition.c_str(), g_conditionUserData)) {
                return false;
            }
        }

        // Trigger choice callback
        if (g_onChoice) {
            g_onChoice(g_activeDialogueId, g_activeNodeId, choiceIndex, g_choiceUserData);
        }

        // Trigger node exit
        if (g_onNodeExit) {
            g_onNodeExit(g_activeDialogueId, g_activeNodeId, g_nodeExitUserData);
        }

        int targetId = choice.targetNodeId;
        if (targetId < 0) {
            Framework_Dialogue_Stop();
            return true;
        }

        EnterDialogueNode(g_activeDialogueId, targetId);
        return true;
    }

    // Current node queries
    const char* Framework_Dialogue_GetCurrentSpeaker() {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (node && !node->speaker.empty()) {
            // Check if speaker is registered
            auto sit = g_speakers.find(node->speaker);
            if (sit != g_speakers.end()) {
                strncpy(s_dialogueSpeakerBuf, sit->second.displayName.c_str(), sizeof(s_dialogueSpeakerBuf) - 1);
            } else {
                strncpy(s_dialogueSpeakerBuf, node->speaker.c_str(), sizeof(s_dialogueSpeakerBuf) - 1);
            }
            return s_dialogueSpeakerBuf;
        }
        return "";
    }

    const char* Framework_Dialogue_GetCurrentText() {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (node) {
            strncpy(s_dialogueTextBuf, node->text.c_str(), sizeof(s_dialogueTextBuf) - 1);
            return s_dialogueTextBuf;
        }
        return "";
    }

    int Framework_Dialogue_GetCurrentPortrait() {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (node) {
            if (node->portrait >= 0) return node->portrait;
            // Check speaker default portrait
            auto sit = g_speakers.find(node->speaker);
            if (sit != g_speakers.end()) {
                return sit->second.portrait;
            }
        }
        return -1;
    }

    int Framework_Dialogue_GetCurrentChoiceCount() {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        return node ? (int)node->choices.size() : 0;
    }

    const char* Framework_Dialogue_GetCurrentChoiceText(int choiceIndex) {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (node && choiceIndex >= 0 && choiceIndex < (int)node->choices.size()) {
            strncpy(s_dialogueChoiceBuf, node->choices[choiceIndex].text.c_str(), sizeof(s_dialogueChoiceBuf) - 1);
            return s_dialogueChoiceBuf;
        }
        return "";
    }

    bool Framework_Dialogue_IsCurrentChoiceAvailable(int choiceIndex) {
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (!node || choiceIndex < 0 || choiceIndex >= (int)node->choices.size()) return false;

        auto& choice = node->choices[choiceIndex];
        if (choice.condition.empty()) return true;

        if (g_conditionHandler) {
            return g_conditionHandler(g_activeDialogueId, choice.condition.c_str(), g_conditionUserData);
        }
        return true;
    }

    // Typewriter effect
    void Framework_Dialogue_SetTypewriterEnabled(bool enabled) {
        g_typewriterEnabled = enabled;
    }

    bool Framework_Dialogue_IsTypewriterEnabled() {
        return g_typewriterEnabled;
    }

    void Framework_Dialogue_SetTypewriterSpeed(float charsPerSecond) {
        g_typewriterSpeed = charsPerSecond > 0 ? charsPerSecond : 1.0f;
    }

    float Framework_Dialogue_GetTypewriterSpeed() {
        return g_typewriterSpeed;
    }

    void Framework_Dialogue_SkipTypewriter() {
        g_typewriterComplete = true;
        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (node) {
            g_visibleText = node->text;
            g_typewriterProgress = (float)node->text.length();
        }
    }

    bool Framework_Dialogue_IsTypewriterComplete() {
        return g_typewriterComplete;
    }

    const char* Framework_Dialogue_GetVisibleText() {
        strncpy(s_dialogueVisibleBuf, g_visibleText.c_str(), sizeof(s_dialogueVisibleBuf) - 1);
        return s_dialogueVisibleBuf;
    }

    int Framework_Dialogue_GetVisibleCharCount() {
        return (int)g_visibleText.length();
    }

    // Callbacks
    void Framework_Dialogue_SetOnStartCallback(DialogueCallback callback, void* userData) {
        g_onDialogueStart = callback;
        g_dialogueStartUserData = userData;
    }

    void Framework_Dialogue_SetOnEndCallback(DialogueCallback callback, void* userData) {
        g_onDialogueEnd = callback;
        g_dialogueEndUserData = userData;
    }

    void Framework_Dialogue_SetOnNodeEnterCallback(DialogueCallback callback, void* userData) {
        g_onNodeEnter = callback;
        g_nodeEnterUserData = userData;
    }

    void Framework_Dialogue_SetOnNodeExitCallback(DialogueCallback callback, void* userData) {
        g_onNodeExit = callback;
        g_nodeExitUserData = userData;
    }

    void Framework_Dialogue_SetOnChoiceCallback(DialogueChoiceCallback callback, void* userData) {
        g_onChoice = callback;
        g_choiceUserData = userData;
    }

    void Framework_Dialogue_SetConditionHandler(DialogueConditionCallback callback, void* userData) {
        g_conditionHandler = callback;
        g_conditionUserData = userData;
    }

    // Dialogue update
    void Framework_Dialogue_Update(float dt) {
        if (!g_typewriterEnabled || g_typewriterComplete) return;
        if (g_activeDialogueId < 0 || g_activeNodeId < 0) return;

        auto* node = GetDialogueNode(g_activeDialogueId, g_activeNodeId);
        if (!node) return;

        g_typewriterProgress += g_typewriterSpeed * dt;
        int charCount = (int)g_typewriterProgress;
        int textLen = (int)node->text.length();

        if (charCount >= textLen) {
            g_visibleText = node->text;
            g_typewriterComplete = true;
        } else {
            g_visibleText = node->text.substr(0, charCount);
        }
    }

    // Speaker management
    void Framework_Dialogue_RegisterSpeaker(const char* speakerId, const char* displayName, int defaultPortrait) {
        if (!speakerId) return;
        Speaker spk;
        spk.id = speakerId;
        spk.displayName = displayName ? displayName : speakerId;
        spk.portrait = defaultPortrait;
        g_speakers[speakerId] = spk;
    }

    void Framework_Dialogue_UnregisterSpeaker(const char* speakerId) {
        if (speakerId) g_speakers.erase(speakerId);
    }

    const char* Framework_Dialogue_GetSpeakerDisplayName(const char* speakerId) {
        if (!speakerId) return "";
        auto it = g_speakers.find(speakerId);
        if (it != g_speakers.end()) {
            strncpy(s_dialogueSpeakerBuf, it->second.displayName.c_str(), sizeof(s_dialogueSpeakerBuf) - 1);
            return s_dialogueSpeakerBuf;
        }
        return "";
    }

    int Framework_Dialogue_GetSpeakerPortrait(const char* speakerId) {
        if (!speakerId) return -1;
        auto it = g_speakers.find(speakerId);
        return it != g_speakers.end() ? it->second.portrait : -1;
    }

    void Framework_Dialogue_SetSpeakerPortrait(const char* speakerId, int textureHandle) {
        if (!speakerId) return;
        auto it = g_speakers.find(speakerId);
        if (it != g_speakers.end()) {
            it->second.portrait = textureHandle;
        }
    }

    // History
    void Framework_Dialogue_SetHistoryEnabled(bool enabled) {
        g_historyEnabled = enabled;
    }

    bool Framework_Dialogue_IsHistoryEnabled() {
        return g_historyEnabled;
    }

    int Framework_Dialogue_GetHistoryCount() {
        return (int)g_dialogueHistory.size();
    }

    const char* Framework_Dialogue_GetHistorySpeaker(int index) {
        if (index >= 0 && index < (int)g_dialogueHistory.size()) {
            strncpy(s_dialogueSpeakerBuf, g_dialogueHistory[index].speaker.c_str(), sizeof(s_dialogueSpeakerBuf) - 1);
            return s_dialogueSpeakerBuf;
        }
        return "";
    }

    const char* Framework_Dialogue_GetHistoryText(int index) {
        if (index >= 0 && index < (int)g_dialogueHistory.size()) {
            strncpy(s_dialogueTextBuf, g_dialogueHistory[index].text.c_str(), sizeof(s_dialogueTextBuf) - 1);
            return s_dialogueTextBuf;
        }
        return "";
    }

    void Framework_Dialogue_ClearHistory() {
        g_dialogueHistory.clear();
    }

    // Save/Load
    bool Framework_Dialogue_SaveToFile(int dialogueId, const char* filename) {
        auto* dlg = GetDialogue(dialogueId);
        if (!dlg || !filename) return false;

        std::ofstream file(filename);
        if (!file.is_open()) return false;

        file << "DIALOGUE " << dlg->name << "\n";
        file << "START " << dlg->startNodeId << "\n";

        for (auto& kv : dlg->nodes) {
            auto& node = kv.second;
            file << "NODE " << node.id << " " << node.tag << "\n";
            file << "SPEAKER " << node.speaker << "\n";
            file << "TEXT " << node.text << "\n";
            file << "NEXT " << node.nextNodeId << "\n";
            file << "PORTRAIT " << node.portrait << "\n";

            for (auto& choice : node.choices) {
                file << "CHOICE " << choice.targetNodeId << " " << choice.text << "\n";
            }
            file << "ENDNODE\n";
        }

        file << "ENDDIALOGUE\n";
        return true;
    }

    int Framework_Dialogue_LoadFromFile(const char* filename) {
        // Simple load - a full implementation would parse the file properly
        // For now just return -1 as placeholder
        return -1;
    }

    // Global management
    void Framework_Dialogue_DestroyAll() {
        g_dialogues.clear();
        g_dialogueByName.clear();
        g_activeDialogueId = -1;
        g_activeNodeId = -1;
    }

    int Framework_Dialogue_GetCount() {
        return (int)g_dialogues.size();
    }

    // ========================================================================
    // INVENTORY SYSTEM
    // ========================================================================

    // Item definition structure
    struct ItemDefinition {
        int id = 0;
        std::string name;         // Internal name
        std::string displayName;  // Display name
        std::string description;
        int iconTexture = -1;
        Rectangle iconRect = { 0, 0, 0, 0 };
        bool stackable = true;
        int maxStack = 99;
        std::string category;
        int rarity = ITEM_RARITY_COMMON;
        int equipSlot = EQUIP_SLOT_NONE;
        std::unordered_map<std::string, int> statsInt;
        std::unordered_map<std::string, float> statsFloat;
        int value = 0;
        float weight = 0.0f;
        bool usable = false;
        bool consumable = false;
    };

    // Inventory slot
    struct InventorySlot {
        int itemDefId = -1;
        int quantity = 0;
    };

    // Inventory container
    struct Inventory {
        int id = 0;
        std::string name;
        int slotCount = 20;
        float maxWeight = 0.0f; // 0 = unlimited
        std::vector<InventorySlot> slots;

        // Callbacks
        InventoryCallback onAddCallback = nullptr;
        InventoryCallback onRemoveCallback = nullptr;
        InventoryCallback onChangeCallback = nullptr;
        ItemUseCallback onUseCallback = nullptr;
        ItemDropCallback onDropCallback = nullptr;
        void* addUserData = nullptr;
        void* removeUserData = nullptr;
        void* changeUserData = nullptr;
        void* useUserData = nullptr;
        void* dropUserData = nullptr;
    };

    // Equipment
    struct Equipment {
        int id = 0;
        std::string name;
        std::unordered_map<int, int> slots; // slot -> itemDefId
    };

    // Loot entry
    struct LootEntry {
        int itemDefId = 0;
        float weight = 1.0f;
        int minQuantity = 1;
        int maxQuantity = 1;
    };

    // Loot table
    struct LootTable {
        int id = 0;
        std::string name;
        std::vector<LootEntry> entries;
    };

    // Global state
    static std::unordered_map<int, ItemDefinition> g_itemDefs;
    static std::unordered_map<std::string, int> g_itemDefByName;
    static int g_nextItemDefId = 1;

    static std::unordered_map<int, Inventory> g_inventories;
    static std::unordered_map<std::string, int> g_inventoryByName;
    static int g_nextInventoryId = 1;

    static std::unordered_map<int, Equipment> g_equipments;
    static std::unordered_map<std::string, int> g_equipmentByName;
    static int g_nextEquipmentId = 1;

    static std::unordered_map<int, LootTable> g_lootTables;
    static std::unordered_map<std::string, int> g_lootTableByName;
    static int g_nextLootTableId = 1;

    // String buffers
    static char s_itemNameBuf[256];
    static char s_itemDescBuf[1024];
    static char s_categoryBuf[256];

    // Helpers
    static ItemDefinition* GetItemDef(int id) {
        auto it = g_itemDefs.find(id);
        return (it != g_itemDefs.end()) ? &it->second : nullptr;
    }

    static Inventory* GetInventory(int id) {
        auto it = g_inventories.find(id);
        return (it != g_inventories.end()) ? &it->second : nullptr;
    }

    static Equipment* GetEquipment(int id) {
        auto it = g_equipments.find(id);
        return (it != g_equipments.end()) ? &it->second : nullptr;
    }

    static LootTable* GetLootTable(int id) {
        auto it = g_lootTables.find(id);
        return (it != g_lootTables.end()) ? &it->second : nullptr;
    }

    static float GetInventoryWeight(Inventory* inv) {
        if (!inv) return 0.0f;
        float weight = 0.0f;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId >= 0) {
                auto* item = GetItemDef(slot.itemDefId);
                if (item) weight += item->weight * slot.quantity;
            }
        }
        return weight;
    }

    // ---- Item Definition API ----

    int Framework_Item_Define(const char* itemName) {
        ItemDefinition item;
        item.id = g_nextItemDefId++;
        item.name = itemName ? itemName : "";
        item.displayName = item.name;

        g_itemDefs[item.id] = item;
        if (itemName) g_itemDefByName[itemName] = item.id;

        return item.id;
    }

    void Framework_Item_Undefine(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        if (!item) return;
        g_itemDefByName.erase(item->name);
        g_itemDefs.erase(itemDefId);
    }

    int Framework_Item_GetDefByName(const char* itemName) {
        if (!itemName) return -1;
        auto it = g_itemDefByName.find(itemName);
        return (it != g_itemDefByName.end()) ? it->second : -1;
    }

    bool Framework_Item_IsDefValid(int itemDefId) {
        return GetItemDef(itemDefId) != nullptr;
    }

    void Framework_Item_SetDisplayName(int itemDefId, const char* displayName) {
        auto* item = GetItemDef(itemDefId);
        if (item && displayName) item->displayName = displayName;
    }

    const char* Framework_Item_GetDisplayName(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        if (!item) return "";
        strncpy(s_itemNameBuf, item->displayName.c_str(), sizeof(s_itemNameBuf) - 1);
        s_itemNameBuf[sizeof(s_itemNameBuf) - 1] = '\0';
        return s_itemNameBuf;
    }

    void Framework_Item_SetDescription(int itemDefId, const char* description) {
        auto* item = GetItemDef(itemDefId);
        if (item && description) item->description = description;
    }

    const char* Framework_Item_GetDescription(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        if (!item) return "";
        strncpy(s_itemDescBuf, item->description.c_str(), sizeof(s_itemDescBuf) - 1);
        s_itemDescBuf[sizeof(s_itemDescBuf) - 1] = '\0';
        return s_itemDescBuf;
    }

    void Framework_Item_SetIcon(int itemDefId, int textureHandle) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->iconTexture = textureHandle;
    }

    int Framework_Item_GetIcon(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->iconTexture : -1;
    }

    void Framework_Item_SetIconRect(int itemDefId, float x, float y, float w, float h) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->iconRect = { x, y, w, h };
    }

    void Framework_Item_SetStackable(int itemDefId, bool stackable) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->stackable = stackable;
    }

    bool Framework_Item_IsStackable(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->stackable : false;
    }

    void Framework_Item_SetMaxStack(int itemDefId, int maxStack) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->maxStack = maxStack;
    }

    int Framework_Item_GetMaxStack(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->maxStack : 1;
    }

    void Framework_Item_SetCategory(int itemDefId, const char* category) {
        auto* item = GetItemDef(itemDefId);
        if (item && category) item->category = category;
    }

    const char* Framework_Item_GetCategory(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        if (!item) return "";
        strncpy(s_categoryBuf, item->category.c_str(), sizeof(s_categoryBuf) - 1);
        s_categoryBuf[sizeof(s_categoryBuf) - 1] = '\0';
        return s_categoryBuf;
    }

    void Framework_Item_SetRarity(int itemDefId, int rarity) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->rarity = rarity;
    }

    int Framework_Item_GetRarity(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->rarity : ITEM_RARITY_COMMON;
    }

    void Framework_Item_SetEquipSlot(int itemDefId, int equipSlot) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->equipSlot = equipSlot;
    }

    int Framework_Item_GetEquipSlot(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->equipSlot : EQUIP_SLOT_NONE;
    }

    void Framework_Item_SetUsable(int itemDefId, bool usable) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->usable = usable;
    }

    bool Framework_Item_IsUsable(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->usable : false;
    }

    void Framework_Item_SetConsumable(int itemDefId, bool consumable) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->consumable = consumable;
    }

    bool Framework_Item_IsConsumable(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->consumable : false;
    }

    void Framework_Item_SetStatInt(int itemDefId, const char* statName, int value) {
        auto* item = GetItemDef(itemDefId);
        if (item && statName) item->statsInt[statName] = value;
    }

    int Framework_Item_GetStatInt(int itemDefId, const char* statName) {
        auto* item = GetItemDef(itemDefId);
        if (!item || !statName) return 0;
        auto it = item->statsInt.find(statName);
        return (it != item->statsInt.end()) ? it->second : 0;
    }

    void Framework_Item_SetStatFloat(int itemDefId, const char* statName, float value) {
        auto* item = GetItemDef(itemDefId);
        if (item && statName) item->statsFloat[statName] = value;
    }

    float Framework_Item_GetStatFloat(int itemDefId, const char* statName) {
        auto* item = GetItemDef(itemDefId);
        if (!item || !statName) return 0.0f;
        auto it = item->statsFloat.find(statName);
        return (it != item->statsFloat.end()) ? it->second : 0.0f;
    }

    void Framework_Item_SetValue(int itemDefId, int value) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->value = value;
    }

    int Framework_Item_GetValue(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->value : 0;
    }

    void Framework_Item_SetWeight(int itemDefId, float weight) {
        auto* item = GetItemDef(itemDefId);
        if (item) item->weight = weight;
    }

    float Framework_Item_GetWeight(int itemDefId) {
        auto* item = GetItemDef(itemDefId);
        return item ? item->weight : 0.0f;
    }

    // ---- Inventory Container API ----

    int Framework_Inventory_Create(const char* name, int slotCount) {
        Inventory inv;
        inv.id = g_nextInventoryId++;
        inv.name = name ? name : "";
        inv.slotCount = slotCount > 0 ? slotCount : 20;
        inv.slots.resize(inv.slotCount);

        g_inventories[inv.id] = inv;
        if (name) g_inventoryByName[name] = inv.id;

        return inv.id;
    }

    void Framework_Inventory_Destroy(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;
        g_inventoryByName.erase(inv->name);
        g_inventories.erase(inventoryId);
    }

    int Framework_Inventory_GetByName(const char* name) {
        if (!name) return -1;
        auto it = g_inventoryByName.find(name);
        return (it != g_inventoryByName.end()) ? it->second : -1;
    }

    bool Framework_Inventory_IsValid(int inventoryId) {
        return GetInventory(inventoryId) != nullptr;
    }

    void Framework_Inventory_SetSlotCount(int inventoryId, int slotCount) {
        auto* inv = GetInventory(inventoryId);
        if (!inv || slotCount <= 0) return;
        inv->slotCount = slotCount;
        inv->slots.resize(slotCount);
    }

    int Framework_Inventory_GetSlotCount(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        return inv ? inv->slotCount : 0;
    }

    void Framework_Inventory_SetMaxWeight(int inventoryId, float maxWeight) {
        auto* inv = GetInventory(inventoryId);
        if (inv) inv->maxWeight = maxWeight;
    }

    float Framework_Inventory_GetMaxWeight(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        return inv ? inv->maxWeight : 0.0f;
    }

    float Framework_Inventory_GetCurrentWeight(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        return inv ? GetInventoryWeight(inv) : 0.0f;
    }

    bool Framework_Inventory_IsWeightLimited(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        return inv ? (inv->maxWeight > 0.0f) : false;
    }

    // Adding items
    bool Framework_Inventory_AddItem(int inventoryId, int itemDefId, int quantity) {
        auto* inv = GetInventory(inventoryId);
        auto* item = GetItemDef(itemDefId);
        if (!inv || !item || quantity <= 0) return false;

        int remaining = quantity;

        // Try to stack with existing items first
        if (item->stackable) {
            for (int i = 0; i < inv->slotCount && remaining > 0; i++) {
                auto& slot = inv->slots[i];
                if (slot.itemDefId == itemDefId) {
                    int spaceInSlot = item->maxStack - slot.quantity;
                    int toAdd = (remaining < spaceInSlot) ? remaining : spaceInSlot;
                    if (toAdd > 0) {
                        slot.quantity += toAdd;
                        remaining -= toAdd;
                        if (inv->onChangeCallback) {
                            inv->onChangeCallback(inventoryId, i, itemDefId, inv->changeUserData);
                        }
                    }
                }
            }
        }

        // Add to empty slots
        for (int i = 0; i < inv->slotCount && remaining > 0; i++) {
            auto& slot = inv->slots[i];
            if (slot.itemDefId < 0) {
                int toAdd = item->stackable ? ((remaining < item->maxStack) ? remaining : item->maxStack) : 1;
                slot.itemDefId = itemDefId;
                slot.quantity = toAdd;
                remaining -= toAdd;

                if (inv->onAddCallback) {
                    inv->onAddCallback(inventoryId, i, itemDefId, inv->addUserData);
                }
            }
        }

        return remaining == 0;
    }

    bool Framework_Inventory_AddItemToSlot(int inventoryId, int slotIndex, int itemDefId, int quantity) {
        auto* inv = GetInventory(inventoryId);
        auto* item = GetItemDef(itemDefId);
        if (!inv || !item || quantity <= 0) return false;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return false;

        auto& slot = inv->slots[slotIndex];
        if (slot.itemDefId >= 0 && slot.itemDefId != itemDefId) return false;

        int currentQty = (slot.itemDefId == itemDefId) ? slot.quantity : 0;
        int maxAdd = item->stackable ? (item->maxStack - currentQty) : (currentQty == 0 ? 1 : 0);
        if (quantity > maxAdd) return false;

        bool wasEmpty = slot.itemDefId < 0;
        slot.itemDefId = itemDefId;
        slot.quantity += quantity;

        if (wasEmpty && inv->onAddCallback) {
            inv->onAddCallback(inventoryId, slotIndex, itemDefId, inv->addUserData);
        } else if (inv->onChangeCallback) {
            inv->onChangeCallback(inventoryId, slotIndex, itemDefId, inv->changeUserData);
        }

        return true;
    }

    int Framework_Inventory_AddItemGetRemaining(int inventoryId, int itemDefId, int quantity) {
        auto* inv = GetInventory(inventoryId);
        auto* item = GetItemDef(itemDefId);
        if (!inv || !item || quantity <= 0) return quantity;

        int remaining = quantity;

        if (item->stackable) {
            for (int i = 0; i < inv->slotCount && remaining > 0; i++) {
                auto& slot = inv->slots[i];
                if (slot.itemDefId == itemDefId) {
                    int spaceInSlot = item->maxStack - slot.quantity;
                    int toAdd = (remaining < spaceInSlot) ? remaining : spaceInSlot;
                    if (toAdd > 0) {
                        slot.quantity += toAdd;
                        remaining -= toAdd;
                    }
                }
            }
        }

        for (int i = 0; i < inv->slotCount && remaining > 0; i++) {
            auto& slot = inv->slots[i];
            if (slot.itemDefId < 0) {
                int toAdd = item->stackable ? ((remaining < item->maxStack) ? remaining : item->maxStack) : 1;
                slot.itemDefId = itemDefId;
                slot.quantity = toAdd;
                remaining -= toAdd;
            }
        }

        return remaining;
    }

    // Removing items
    bool Framework_Inventory_RemoveItem(int inventoryId, int itemDefId, int quantity) {
        auto* inv = GetInventory(inventoryId);
        if (!inv || quantity <= 0) return false;

        int total = Framework_Inventory_CountItem(inventoryId, itemDefId);
        if (total < quantity) return false;

        int remaining = quantity;
        for (int i = inv->slotCount - 1; i >= 0 && remaining > 0; i--) {
            auto& slot = inv->slots[i];
            if (slot.itemDefId == itemDefId) {
                int toRemove = (remaining < slot.quantity) ? remaining : slot.quantity;
                slot.quantity -= toRemove;
                remaining -= toRemove;

                if (slot.quantity <= 0) {
                    int oldId = slot.itemDefId;
                    slot.itemDefId = -1;
                    slot.quantity = 0;
                    if (inv->onRemoveCallback) {
                        inv->onRemoveCallback(inventoryId, i, oldId, inv->removeUserData);
                    }
                }
            }
        }

        return true;
    }

    bool Framework_Inventory_RemoveItemFromSlot(int inventoryId, int slotIndex, int quantity) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return false;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return false;

        auto& slot = inv->slots[slotIndex];
        if (slot.itemDefId < 0 || slot.quantity < quantity) return false;

        slot.quantity -= quantity;
        if (slot.quantity <= 0) {
            int oldId = slot.itemDefId;
            slot.itemDefId = -1;
            slot.quantity = 0;
            if (inv->onRemoveCallback) {
                inv->onRemoveCallback(inventoryId, slotIndex, oldId, inv->removeUserData);
            }
        }

        return true;
    }

    void Framework_Inventory_ClearSlot(int inventoryId, int slotIndex) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return;

        auto& slot = inv->slots[slotIndex];
        if (slot.itemDefId >= 0) {
            int oldId = slot.itemDefId;
            slot.itemDefId = -1;
            slot.quantity = 0;
            if (inv->onRemoveCallback) {
                inv->onRemoveCallback(inventoryId, slotIndex, oldId, inv->removeUserData);
            }
        }
    }

    void Framework_Inventory_Clear(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;

        for (int i = 0; i < inv->slotCount; i++) {
            auto& slot = inv->slots[i];
            if (slot.itemDefId >= 0) {
                int oldId = slot.itemDefId;
                slot.itemDefId = -1;
                slot.quantity = 0;
                if (inv->onRemoveCallback) {
                    inv->onRemoveCallback(inventoryId, i, oldId, inv->removeUserData);
                }
            }
        }
    }

    // Slot queries
    int Framework_Inventory_GetItemAt(int inventoryId, int slotIndex) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return -1;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return -1;
        return inv->slots[slotIndex].itemDefId;
    }

    int Framework_Inventory_GetQuantityAt(int inventoryId, int slotIndex) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return 0;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return 0;
        return inv->slots[slotIndex].quantity;
    }

    bool Framework_Inventory_IsSlotEmpty(int inventoryId, int slotIndex) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return true;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return true;
        return inv->slots[slotIndex].itemDefId < 0;
    }

    int Framework_Inventory_GetFirstEmptySlot(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return -1;
        for (int i = 0; i < inv->slotCount; i++) {
            if (inv->slots[i].itemDefId < 0) return i;
        }
        return -1;
    }

    int Framework_Inventory_GetEmptySlotCount(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return 0;

        int count = 0;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId < 0) count++;
        }
        return count;
    }

    // Item queries
    bool Framework_Inventory_HasItem(int inventoryId, int itemDefId) {
        return Framework_Inventory_FindItem(inventoryId, itemDefId) >= 0;
    }

    int Framework_Inventory_CountItem(int inventoryId, int itemDefId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return 0;

        int count = 0;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId == itemDefId) {
                count += slot.quantity;
            }
        }
        return count;
    }

    int Framework_Inventory_FindItem(int inventoryId, int itemDefId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return -1;

        for (int i = 0; i < inv->slotCount; i++) {
            if (inv->slots[i].itemDefId == itemDefId) {
                return i;
            }
        }
        return -1;
    }

    int Framework_Inventory_FindItemByCategory(int inventoryId, const char* category) {
        auto* inv = GetInventory(inventoryId);
        if (!inv || !category) return -1;

        for (int i = 0; i < inv->slotCount; i++) {
            auto* item = GetItemDef(inv->slots[i].itemDefId);
            if (item && item->category == category) {
                return i;
            }
        }
        return -1;
    }

    // Moving/swapping items
    bool Framework_Inventory_MoveItem(int inventoryId, int fromSlot, int toSlot) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return false;
        if (fromSlot < 0 || fromSlot >= inv->slotCount) return false;
        if (toSlot < 0 || toSlot >= inv->slotCount) return false;
        if (fromSlot == toSlot) return true;

        auto& from = inv->slots[fromSlot];
        auto& to = inv->slots[toSlot];

        if (from.itemDefId < 0) return false;
        if (to.itemDefId >= 0) return false;

        to = from;
        from.itemDefId = -1;
        from.quantity = 0;
        return true;
    }

    bool Framework_Inventory_SwapSlots(int inventoryId, int slotA, int slotB) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return false;
        if (slotA < 0 || slotA >= inv->slotCount) return false;
        if (slotB < 0 || slotB >= inv->slotCount) return false;
        if (slotA == slotB) return true;

        std::swap(inv->slots[slotA], inv->slots[slotB]);
        return true;
    }

    bool Framework_Inventory_TransferItem(int fromInvId, int fromSlot, int toInvId, int toSlot, int quantity) {
        auto* fromInv = GetInventory(fromInvId);
        auto* toInv = GetInventory(toInvId);
        if (!fromInv || !toInv) return false;
        if (fromSlot < 0 || fromSlot >= fromInv->slotCount) return false;
        if (toSlot < 0 || toSlot >= toInv->slotCount) return false;

        auto& from = fromInv->slots[fromSlot];
        if (from.itemDefId < 0 || from.quantity < quantity) return false;

        if (!Framework_Inventory_AddItemToSlot(toInvId, toSlot, from.itemDefId, quantity)) {
            return false;
        }

        Framework_Inventory_RemoveItemFromSlot(fromInvId, fromSlot, quantity);
        return true;
    }

    bool Framework_Inventory_SplitStack(int inventoryId, int slotIndex, int quantity, int targetSlot) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return false;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return false;
        if (targetSlot < 0 || targetSlot >= inv->slotCount) return false;
        if (slotIndex == targetSlot) return false;

        auto& from = inv->slots[slotIndex];
        auto& to = inv->slots[targetSlot];

        if (from.itemDefId < 0 || from.quantity <= quantity) return false;
        if (to.itemDefId >= 0) return false;

        to.itemDefId = from.itemDefId;
        to.quantity = quantity;
        from.quantity -= quantity;
        return true;
    }

    // Sorting
    void Framework_Inventory_Sort(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;

        // Collect non-empty slots
        std::vector<InventorySlot> items;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId >= 0) {
                items.push_back(slot);
            }
        }

        // Sort by category then name
        std::sort(items.begin(), items.end(), [](const InventorySlot& a, const InventorySlot& b) {
            auto* itemA = GetItemDef(a.itemDefId);
            auto* itemB = GetItemDef(b.itemDefId);
            if (!itemA || !itemB) return false;
            if (itemA->category != itemB->category) return itemA->category < itemB->category;
            return itemA->name < itemB->name;
        });

        // Clear slots and refill
        int itemIdx = 0;
        for (auto& slot : inv->slots) {
            if (itemIdx < (int)items.size()) {
                slot = items[itemIdx++];
            } else {
                slot.itemDefId = -1;
                slot.quantity = 0;
            }
        }
    }

    void Framework_Inventory_SortByRarity(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;

        std::vector<InventorySlot> items;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId >= 0) {
                items.push_back(slot);
            }
        }

        std::sort(items.begin(), items.end(), [](const InventorySlot& a, const InventorySlot& b) {
            auto* itemA = GetItemDef(a.itemDefId);
            auto* itemB = GetItemDef(b.itemDefId);
            if (!itemA || !itemB) return false;
            return itemA->rarity > itemB->rarity;
        });

        int itemIdx = 0;
        for (auto& slot : inv->slots) {
            if (itemIdx < (int)items.size()) {
                slot = items[itemIdx++];
            } else {
                slot.itemDefId = -1;
                slot.quantity = 0;
            }
        }
    }

    void Framework_Inventory_Compact(int inventoryId) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return;

        std::vector<InventorySlot> items;
        for (auto& slot : inv->slots) {
            if (slot.itemDefId >= 0) {
                items.push_back(slot);
            }
        }

        int itemIdx = 0;
        for (auto& slot : inv->slots) {
            if (itemIdx < (int)items.size()) {
                slot = items[itemIdx++];
            } else {
                slot.itemDefId = -1;
                slot.quantity = 0;
            }
        }
    }

    // Using items
    bool Framework_Inventory_UseItem(int inventoryId, int slotIndex) {
        auto* inv = GetInventory(inventoryId);
        if (!inv) return false;
        if (slotIndex < 0 || slotIndex >= inv->slotCount) return false;

        auto& slot = inv->slots[slotIndex];
        if (slot.itemDefId < 0) return false;

        auto* item = GetItemDef(slot.itemDefId);
        if (!item || !item->usable) return false;

        if (inv->onUseCallback) {
            inv->onUseCallback(inventoryId, slotIndex, slot.itemDefId, slot.quantity, inv->useUserData);
        }

        if (item->consumable) {
            Framework_Inventory_RemoveItemFromSlot(inventoryId, slotIndex, 1);
        }

        return true;
    }

    void Framework_Inventory_SetUseCallback(int inventoryId, ItemUseCallback callback, void* userData) {
        auto* inv = GetInventory(inventoryId);
        if (inv) {
            inv->onUseCallback = callback;
            inv->useUserData = userData;
        }
    }

    // ---- Equipment System ----

    int Framework_Equipment_Create(const char* name) {
        Equipment equip;
        equip.id = g_nextEquipmentId++;
        equip.name = name ? name : "";

        g_equipments[equip.id] = equip;
        if (name) g_equipmentByName[name] = equip.id;

        return equip.id;
    }

    void Framework_Equipment_Destroy(int equipId) {
        auto* equip = GetEquipment(equipId);
        if (!equip) return;
        g_equipmentByName.erase(equip->name);
        g_equipments.erase(equipId);
    }

    int Framework_Equipment_GetByName(const char* name) {
        if (!name) return -1;
        auto it = g_equipmentByName.find(name);
        return (it != g_equipmentByName.end()) ? it->second : -1;
    }

    bool Framework_Equipment_IsValid(int equipId) {
        return GetEquipment(equipId) != nullptr;
    }

    bool Framework_Equipment_Equip(int equipId, int itemDefId, int slot) {
        auto* equip = GetEquipment(equipId);
        auto* item = GetItemDef(itemDefId);
        if (!equip || !item) return false;

        equip->slots[slot] = itemDefId;
        return true;
    }

    bool Framework_Equipment_EquipFromInventory(int equipId, int inventoryId, int invSlot, int equipSlot) {
        auto* equip = GetEquipment(equipId);
        auto* inv = GetInventory(inventoryId);
        if (!equip || !inv) return false;
        if (invSlot < 0 || invSlot >= inv->slotCount) return false;

        auto& slot = inv->slots[invSlot];
        if (slot.itemDefId < 0) return false;

        int itemId = slot.itemDefId;
        Framework_Inventory_RemoveItemFromSlot(inventoryId, invSlot, 1);
        equip->slots[equipSlot] = itemId;
        return true;
    }

    int Framework_Equipment_Unequip(int equipId, int slot) {
        auto* equip = GetEquipment(equipId);
        if (!equip) return -1;

        auto it = equip->slots.find(slot);
        if (it == equip->slots.end() || it->second < 0) return -1;

        int itemId = it->second;
        equip->slots.erase(it);

        return itemId;
    }

    bool Framework_Equipment_UnequipToInventory(int equipId, int slot, int inventoryId) {
        auto* equip = GetEquipment(equipId);
        auto* inv = GetInventory(inventoryId);
        if (!equip || !inv) return false;

        int itemId = Framework_Equipment_Unequip(equipId, slot);
        if (itemId < 0) return false;

        return Framework_Inventory_AddItem(inventoryId, itemId, 1);
    }

    void Framework_Equipment_UnequipAll(int equipId) {
        auto* equip = GetEquipment(equipId);
        if (equip) equip->slots.clear();
    }

    int Framework_Equipment_GetItemAt(int equipId, int slot) {
        auto* equip = GetEquipment(equipId);
        if (!equip) return -1;

        auto it = equip->slots.find(slot);
        return (it != equip->slots.end()) ? it->second : -1;
    }

    bool Framework_Equipment_IsSlotEmpty(int equipId, int slot) {
        return Framework_Equipment_GetItemAt(equipId, slot) < 0;
    }

    bool Framework_Equipment_CanEquip(int equipId, int itemDefId, int slot) {
        auto* equip = GetEquipment(equipId);
        auto* item = GetItemDef(itemDefId);
        if (!equip || !item) return false;
        return item->equipSlot == slot || item->equipSlot == EQUIP_SLOT_NONE;
    }

    int Framework_Equipment_GetTotalStatInt(int equipId, const char* statName) {
        auto* equip = GetEquipment(equipId);
        if (!equip || !statName) return 0;

        int total = 0;
        for (auto& kv : equip->slots) {
            if (kv.second >= 0) {
                auto* item = GetItemDef(kv.second);
                if (item) {
                    auto it = item->statsInt.find(statName);
                    if (it != item->statsInt.end()) {
                        total += it->second;
                    }
                }
            }
        }
        return total;
    }

    float Framework_Equipment_GetTotalStatFloat(int equipId, const char* statName) {
        auto* equip = GetEquipment(equipId);
        if (!equip || !statName) return 0.0f;

        float total = 0.0f;
        for (auto& kv : equip->slots) {
            if (kv.second >= 0) {
                auto* item = GetItemDef(kv.second);
                if (item) {
                    auto it = item->statsFloat.find(statName);
                    if (it != item->statsFloat.end()) {
                        total += it->second;
                    }
                }
            }
        }
        return total;
    }

    // ---- Inventory Callbacks ----

    void Framework_Inventory_SetOnAddCallback(int inventoryId, InventoryCallback callback, void* userData) {
        auto* inv = GetInventory(inventoryId);
        if (inv) {
            inv->onAddCallback = callback;
            inv->addUserData = userData;
        }
    }

    void Framework_Inventory_SetOnRemoveCallback(int inventoryId, InventoryCallback callback, void* userData) {
        auto* inv = GetInventory(inventoryId);
        if (inv) {
            inv->onRemoveCallback = callback;
            inv->removeUserData = userData;
        }
    }

    void Framework_Inventory_SetOnChangeCallback(int inventoryId, InventoryCallback callback, void* userData) {
        auto* inv = GetInventory(inventoryId);
        if (inv) {
            inv->onChangeCallback = callback;
            inv->changeUserData = userData;
        }
    }

    void Framework_Inventory_SetDropCallback(int inventoryId, ItemDropCallback callback, void* userData) {
        auto* inv = GetInventory(inventoryId);
        if (inv) {
            inv->onDropCallback = callback;
            inv->dropUserData = userData;
        }
    }

    // ---- Loot Tables ----

    int Framework_LootTable_Create(const char* name) {
        LootTable table;
        table.id = g_nextLootTableId++;
        table.name = name ? name : "";

        g_lootTables[table.id] = table;
        if (name) g_lootTableByName[name] = table.id;

        return table.id;
    }

    void Framework_LootTable_Destroy(int tableId) {
        auto* table = GetLootTable(tableId);
        if (!table) return;
        g_lootTableByName.erase(table->name);
        g_lootTables.erase(tableId);
    }

    void Framework_LootTable_AddEntry(int tableId, int itemDefId, float weight, int minQty, int maxQty) {
        auto* table = GetLootTable(tableId);
        if (!table) return;

        LootEntry entry;
        entry.itemDefId = itemDefId;
        entry.weight = weight > 0.0f ? weight : 1.0f;
        entry.minQuantity = minQty > 0 ? minQty : 1;
        entry.maxQuantity = maxQty > minQty ? maxQty : minQty;

        table->entries.push_back(entry);
    }

    void Framework_LootTable_RemoveEntry(int tableId, int itemDefId) {
        auto* table = GetLootTable(tableId);
        if (!table) return;

        table->entries.erase(
            std::remove_if(table->entries.begin(), table->entries.end(),
                [itemDefId](const LootEntry& e) { return e.itemDefId == itemDefId; }),
            table->entries.end());
    }

    int Framework_LootTable_Roll(int tableId, int* outQuantity) {
        auto* table = GetLootTable(tableId);
        if (!table || table->entries.empty()) {
            if (outQuantity) *outQuantity = 0;
            return -1;
        }

        float totalWeight = 0.0f;
        for (auto& entry : table->entries) {
            totalWeight += entry.weight;
        }

        float roll = (float)GetRandomValue(0, 10000) / 10000.0f * totalWeight;
        float cumulative = 0.0f;

        for (auto& entry : table->entries) {
            cumulative += entry.weight;
            if (roll <= cumulative) {
                int qty = entry.minQuantity;
                if (entry.maxQuantity > entry.minQuantity) {
                    qty += GetRandomValue(0, entry.maxQuantity - entry.minQuantity);
                }
                if (outQuantity) *outQuantity = qty;
                return entry.itemDefId;
            }
        }

        if (outQuantity) *outQuantity = 0;
        return -1;
    }

    void Framework_LootTable_RollMultiple(int tableId, int rolls, int* outItems, int* outQuantities, int bufferSize) {
        if (!outItems || !outQuantities || bufferSize <= 0) return;

        for (int i = 0; i < rolls && i < bufferSize; i++) {
            outItems[i] = Framework_LootTable_Roll(tableId, &outQuantities[i]);
        }
    }

    // ---- Save/Load ----

    bool Framework_Inventory_SaveToSlot(int inventoryId, int saveSlot, const char* key) {
        // Simplified implementation - would use save system
        return true;
    }

    bool Framework_Inventory_LoadFromSlot(int inventoryId, int saveSlot, const char* key) {
        return true;
    }

    bool Framework_Equipment_SaveToSlot(int equipId, int saveSlot, const char* key) {
        return true;
    }

    bool Framework_Equipment_LoadFromSlot(int equipId, int saveSlot, const char* key) {
        return true;
    }

    // ---- Global Management ----

    void Framework_Item_UndefineAll() {
        g_itemDefs.clear();
        g_itemDefByName.clear();
    }

    void Framework_Inventory_DestroyAll() {
        g_inventories.clear();
        g_inventoryByName.clear();
    }

    void Framework_Equipment_DestroyAll() {
        g_equipments.clear();
        g_equipmentByName.clear();
    }

    void Framework_LootTable_DestroyAll() {
        g_lootTables.clear();
        g_lootTableByName.clear();
    }

    int Framework_Item_GetDefCount() {
        return (int)g_itemDefs.size();
    }

    int Framework_Inventory_GetCount() {
        return (int)g_inventories.size();
    }

    int Framework_Equipment_GetCount() {
        return (int)g_equipments.size();
    }

    // ========================================================================
    // QUEST SYSTEM
    // ========================================================================
    // Note: Quest states and objective types are defined in framework.h as macros

    struct QuestObjective {
        int type = OBJECTIVE_TYPE_CUSTOM;
        std::string description;
        int requiredCount = 1;
        int currentProgress = 0;
        std::string targetId;       // For kill, talk, interact
        float locationX = 0;        // For reach/explore
        float locationY = 0;
        float locationRadius = 50;
        bool optional = false;
        bool hidden = false;
        bool completed = false;
    };

    struct QuestReward {
        std::vector<std::pair<int, int>> items;  // itemDefId, quantity
        int experience = 0;
        std::unordered_map<int, int> currency;   // currencyType -> amount
        std::vector<std::string> unlocks;
    };

    struct Quest {
        int handle = 0;
        std::string stringId;
        std::string name;
        std::string description;
        std::string category;
        int level = 1;
        int state = QUEST_STATE_NOT_STARTED;
        bool repeatable = false;
        bool autoComplete = true;
        bool hidden = false;
        float timeLimit = 0;        // 0 = no limit
        float timeElapsed = 0;
        int minLevel = 0;
        std::vector<std::string> prerequisites;
        std::vector<QuestObjective> objectives;
        QuestReward rewards;
        bool tracked = false;
    };

    struct QuestChain {
        int handle = 0;
        std::string stringId;
        std::vector<int> questHandles;
        int currentIndex = 0;
    };

    // Quest system state
    static std::unordered_map<int, Quest> g_quests;
    static std::unordered_map<std::string, int> g_questByStringId;
    static std::unordered_map<int, QuestChain> g_questChains;
    static std::unordered_map<std::string, int> g_chainByStringId;
    static int g_nextQuestHandle = 1;
    static int g_nextChainHandle = 1;
    static int g_maxTracked = 3;
    static QuestStateCallback g_questStateCallback = nullptr;
    static ObjectiveUpdateCallback g_objectiveUpdateCallback = nullptr;

    // Static buffers for string returns
    static char g_questNameBuf[256];
    static char g_questDescBuf[1024];
    static char g_questCatBuf[128];
    static char g_questIdBuf[128];
    static char g_objDescBuf[512];

    // Helper: Check if all required objectives are complete
    static bool AreRequiredObjectivesComplete(const Quest& q) {
        for (const auto& obj : q.objectives) {
            if (!obj.optional && !obj.completed) return false;
        }
        return true;
    }

    // Helper: Update objective completion status
    static void UpdateObjectiveCompletion(Quest& q, int objIndex) {
        if (objIndex < 0 || objIndex >= (int)q.objectives.size()) return;
        auto& obj = q.objectives[objIndex];
        bool wasComplete = obj.completed;
        obj.completed = (obj.currentProgress >= obj.requiredCount);

        // Fire callback if progress changed
        if (g_objectiveUpdateCallback) {
            g_objectiveUpdateCallback(q.handle, objIndex, obj.currentProgress, obj.requiredCount);
        }

        // Check for auto-complete
        if (!wasComplete && obj.completed && q.autoComplete && q.state == QUEST_STATE_IN_PROGRESS) {
            if (AreRequiredObjectivesComplete(q)) {
                q.state = QUEST_STATE_COMPLETED;
                if (g_questStateCallback) {
                    g_questStateCallback(q.handle, QUEST_STATE_COMPLETED);
                }
            }
        }
    }

    // ---- Quest Definition ----
    int Framework_Quest_Define(const char* questId) {
        if (!questId) return -1;

        // Check if already defined
        auto it = g_questByStringId.find(questId);
        if (it != g_questByStringId.end()) {
            return it->second;
        }

        Quest q;
        q.handle = g_nextQuestHandle++;
        q.stringId = questId;
        q.name = questId;
        g_quests[q.handle] = q;
        g_questByStringId[questId] = q.handle;
        return q.handle;
    }

    void Framework_Quest_SetName(int questHandle, const char* name) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end() && name) {
            it->second.name = name;
        }
    }

    void Framework_Quest_SetDescription(int questHandle, const char* description) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end() && description) {
            it->second.description = description;
        }
    }

    void Framework_Quest_SetCategory(int questHandle, const char* category) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end() && category) {
            it->second.category = category;
        }
    }

    void Framework_Quest_SetLevel(int questHandle, int level) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.level = level;
        }
    }

    void Framework_Quest_SetRepeatable(int questHandle, bool repeatable) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.repeatable = repeatable;
        }
    }

    void Framework_Quest_SetAutoComplete(int questHandle, bool autoComplete) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.autoComplete = autoComplete;
        }
    }

    void Framework_Quest_SetHidden(int questHandle, bool hidden) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.hidden = hidden;
        }
    }

    void Framework_Quest_SetTimeLimit(int questHandle, float seconds) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.timeLimit = seconds;
        }
    }

    // ---- Quest Prerequisites ----
    void Framework_Quest_AddPrerequisite(int questHandle, const char* requiredQuestId) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end() && requiredQuestId) {
            it->second.prerequisites.push_back(requiredQuestId);
        }
    }

    void Framework_Quest_SetMinLevel(int questHandle, int minLevel) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.minLevel = minLevel;
        }
    }

    bool Framework_Quest_CheckPrerequisites(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;

        const Quest& q = it->second;
        for (const auto& prereqId : q.prerequisites) {
            auto prereqIt = g_questByStringId.find(prereqId);
            if (prereqIt == g_questByStringId.end()) return false;
            auto questIt = g_quests.find(prereqIt->second);
            if (questIt == g_quests.end()) return false;
            if (questIt->second.state != QUEST_STATE_COMPLETED) return false;
        }
        return true;
    }

    // ---- Objectives ----
    int Framework_Quest_AddObjective(int questHandle, int objectiveType, const char* description, int requiredCount) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return -1;

        QuestObjective obj;
        obj.type = objectiveType;
        obj.description = description ? description : "";
        obj.requiredCount = requiredCount > 0 ? requiredCount : 1;
        it->second.objectives.push_back(obj);
        return (int)it->second.objectives.size() - 1;
    }

    void Framework_Quest_SetObjectiveTarget(int questHandle, int objectiveIndex, const char* targetId) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;
        if (targetId) {
            it->second.objectives[objectiveIndex].targetId = targetId;
        }
    }

    void Framework_Quest_SetObjectiveLocation(int questHandle, int objectiveIndex, float x, float y, float radius) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;
        it->second.objectives[objectiveIndex].locationX = x;
        it->second.objectives[objectiveIndex].locationY = y;
        it->second.objectives[objectiveIndex].locationRadius = radius;
    }

    void Framework_Quest_SetObjectiveOptional(int questHandle, int objectiveIndex, bool optional) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;
        it->second.objectives[objectiveIndex].optional = optional;
    }

    void Framework_Quest_SetObjectiveHidden(int questHandle, int objectiveIndex, bool hidden) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;
        it->second.objectives[objectiveIndex].hidden = hidden;
    }

    int Framework_Quest_GetObjectiveCount(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        return (int)it->second.objectives.size();
    }

    const char* Framework_Quest_GetObjectiveDescription(int questHandle, int objectiveIndex) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return "";
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return "";
        strncpy(g_objDescBuf, it->second.objectives[objectiveIndex].description.c_str(), sizeof(g_objDescBuf) - 1);
        g_objDescBuf[sizeof(g_objDescBuf) - 1] = '\0';
        return g_objDescBuf;
    }

    int Framework_Quest_GetObjectiveType(int questHandle, int objectiveIndex) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return -1;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return -1;
        return it->second.objectives[objectiveIndex].type;
    }

    int Framework_Quest_GetObjectiveProgress(int questHandle, int objectiveIndex) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return 0;
        return it->second.objectives[objectiveIndex].currentProgress;
    }

    int Framework_Quest_GetObjectiveRequired(int questHandle, int objectiveIndex) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return 0;
        return it->second.objectives[objectiveIndex].requiredCount;
    }

    bool Framework_Quest_IsObjectiveComplete(int questHandle, int objectiveIndex) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return false;
        return it->second.objectives[objectiveIndex].completed;
    }

    // ---- Rewards ----
    void Framework_Quest_AddRewardItem(int questHandle, int itemDefId, int quantity) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.rewards.items.push_back({ itemDefId, quantity });
        }
    }

    void Framework_Quest_SetRewardExperience(int questHandle, int experience) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.rewards.experience = experience;
        }
    }

    void Framework_Quest_SetRewardCurrency(int questHandle, int currencyType, int amount) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end()) {
            it->second.rewards.currency[currencyType] = amount;
        }
    }

    void Framework_Quest_AddRewardUnlock(int questHandle, const char* unlockId) {
        auto it = g_quests.find(questHandle);
        if (it != g_quests.end() && unlockId) {
            it->second.rewards.unlocks.push_back(unlockId);
        }
    }

    // ---- Quest State Management ----
    bool Framework_Quest_Start(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;

        Quest& q = it->second;
        if (q.state == QUEST_STATE_IN_PROGRESS) return true;  // Already started
        if (q.state == QUEST_STATE_COMPLETED && !q.repeatable) return false;
        if (!Framework_Quest_CheckPrerequisites(questHandle)) return false;

        q.state = QUEST_STATE_IN_PROGRESS;
        q.timeElapsed = 0;

        // Reset progress for repeatable quests
        if (q.repeatable) {
            for (auto& obj : q.objectives) {
                obj.currentProgress = 0;
                obj.completed = false;
            }
        }

        if (g_questStateCallback) {
            g_questStateCallback(questHandle, QUEST_STATE_IN_PROGRESS);
        }
        return true;
    }

    bool Framework_Quest_Complete(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        if (it->second.state != QUEST_STATE_IN_PROGRESS) return false;

        it->second.state = QUEST_STATE_COMPLETED;
        it->second.tracked = false;

        if (g_questStateCallback) {
            g_questStateCallback(questHandle, QUEST_STATE_COMPLETED);
        }
        return true;
    }

    bool Framework_Quest_Fail(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        if (it->second.state != QUEST_STATE_IN_PROGRESS) return false;

        it->second.state = QUEST_STATE_FAILED;
        it->second.tracked = false;

        if (g_questStateCallback) {
            g_questStateCallback(questHandle, QUEST_STATE_FAILED);
        }
        return true;
    }

    bool Framework_Quest_Abandon(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        if (it->second.state != QUEST_STATE_IN_PROGRESS) return false;

        it->second.state = QUEST_STATE_NOT_STARTED;
        it->second.tracked = false;
        it->second.timeElapsed = 0;

        // Reset progress
        for (auto& obj : it->second.objectives) {
            obj.currentProgress = 0;
            obj.completed = false;
        }

        if (g_questStateCallback) {
            g_questStateCallback(questHandle, QUEST_STATE_NOT_STARTED);
        }
        return true;
    }

    bool Framework_Quest_Reset(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;

        it->second.state = QUEST_STATE_NOT_STARTED;
        it->second.tracked = false;
        it->second.timeElapsed = 0;

        for (auto& obj : it->second.objectives) {
            obj.currentProgress = 0;
            obj.completed = false;
        }
        return true;
    }

    int Framework_Quest_GetState(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return -1;
        return it->second.state;
    }

    bool Framework_Quest_IsActive(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        return it->second.state == QUEST_STATE_IN_PROGRESS;
    }

    bool Framework_Quest_IsCompleted(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        return it->second.state == QUEST_STATE_COMPLETED;
    }

    bool Framework_Quest_CanStart(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;

        const Quest& q = it->second;
        if (q.state == QUEST_STATE_IN_PROGRESS) return false;
        if (q.state == QUEST_STATE_COMPLETED && !q.repeatable) return false;
        return Framework_Quest_CheckPrerequisites(questHandle);
    }

    // ---- Progress Tracking ----
    void Framework_Quest_SetObjectiveProgress(int questHandle, int objectiveIndex, int progress) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;

        it->second.objectives[objectiveIndex].currentProgress = progress;
        UpdateObjectiveCompletion(it->second, objectiveIndex);
    }

    void Framework_Quest_AddObjectiveProgress(int questHandle, int objectiveIndex, int amount) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;
        if (objectiveIndex < 0 || objectiveIndex >= (int)it->second.objectives.size()) return;

        it->second.objectives[objectiveIndex].currentProgress += amount;
        UpdateObjectiveCompletion(it->second, objectiveIndex);
    }

    float Framework_Quest_GetCompletionPercent(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0.0f;

        const Quest& q = it->second;
        if (q.objectives.empty()) return q.state == QUEST_STATE_COMPLETED ? 100.0f : 0.0f;

        int totalRequired = 0;
        int totalProgress = 0;
        for (const auto& obj : q.objectives) {
            if (!obj.optional) {
                totalRequired += obj.requiredCount;
                totalProgress += (obj.currentProgress < obj.requiredCount) ? obj.currentProgress : obj.requiredCount;
            }
        }
        if (totalRequired == 0) return 100.0f;
        return (float)totalProgress / (float)totalRequired * 100.0f;
    }

    // ---- Auto-Progress Reporting ----
    void Framework_Quest_ReportKill(const char* targetType, int count) {
        if (!targetType) return;
        std::string target = targetType;

        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if (obj.type == OBJECTIVE_TYPE_KILL && obj.targetId == target && !obj.completed) {
                    obj.currentProgress += count;
                    UpdateObjectiveCompletion(q, i);
                }
            }
        }
    }

    void Framework_Quest_ReportCollect(int itemDefId, int count) {
        std::string target = std::to_string(itemDefId);

        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if (obj.type == OBJECTIVE_TYPE_COLLECT && obj.targetId == target && !obj.completed) {
                    obj.currentProgress += count;
                    UpdateObjectiveCompletion(q, i);
                }
            }
        }
    }

    void Framework_Quest_ReportTalk(const char* npcId) {
        if (!npcId) return;
        std::string target = npcId;

        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if (obj.type == OBJECTIVE_TYPE_TALK && obj.targetId == target && !obj.completed) {
                    obj.currentProgress = obj.requiredCount;
                    UpdateObjectiveCompletion(q, i);
                }
            }
        }
    }

    void Framework_Quest_ReportLocation(float x, float y) {
        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if ((obj.type == OBJECTIVE_TYPE_REACH || obj.type == OBJECTIVE_TYPE_EXPLORE) && !obj.completed) {
                    float dx = x - obj.locationX;
                    float dy = y - obj.locationY;
                    float dist = sqrtf(dx * dx + dy * dy);
                    if (dist <= obj.locationRadius) {
                        obj.currentProgress = obj.requiredCount;
                        UpdateObjectiveCompletion(q, i);
                    }
                }
            }
        }
    }

    void Framework_Quest_ReportInteract(const char* objectId) {
        if (!objectId) return;
        std::string target = objectId;

        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if (obj.type == OBJECTIVE_TYPE_INTERACT && obj.targetId == target && !obj.completed) {
                    obj.currentProgress++;
                    UpdateObjectiveCompletion(q, i);
                }
            }
        }
    }

    void Framework_Quest_ReportCustom(const char* eventType, const char* eventData) {
        if (!eventType) return;
        std::string target = eventType;
        std::string data = eventData ? eventData : "";

        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            for (int i = 0; i < (int)q.objectives.size(); i++) {
                auto& obj = q.objectives[i];
                if (obj.type == OBJECTIVE_TYPE_CUSTOM && obj.targetId == target && !obj.completed) {
                    obj.currentProgress++;
                    UpdateObjectiveCompletion(q, i);
                }
            }
        }
    }

    // ---- Quest Queries ----
    int Framework_Quest_GetByStringId(const char* questId) {
        if (!questId) return -1;
        auto it = g_questByStringId.find(questId);
        if (it == g_questByStringId.end()) return -1;
        return it->second;
    }

    const char* Framework_Quest_GetName(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return "";
        strncpy(g_questNameBuf, it->second.name.c_str(), sizeof(g_questNameBuf) - 1);
        g_questNameBuf[sizeof(g_questNameBuf) - 1] = '\0';
        return g_questNameBuf;
    }

    const char* Framework_Quest_GetDescription(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return "";
        strncpy(g_questDescBuf, it->second.description.c_str(), sizeof(g_questDescBuf) - 1);
        g_questDescBuf[sizeof(g_questDescBuf) - 1] = '\0';
        return g_questDescBuf;
    }

    const char* Framework_Quest_GetCategory(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return "";
        strncpy(g_questCatBuf, it->second.category.c_str(), sizeof(g_questCatBuf) - 1);
        g_questCatBuf[sizeof(g_questCatBuf) - 1] = '\0';
        return g_questCatBuf;
    }

    const char* Framework_Quest_GetStringId(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return "";
        strncpy(g_questIdBuf, it->second.stringId.c_str(), sizeof(g_questIdBuf) - 1);
        g_questIdBuf[sizeof(g_questIdBuf) - 1] = '\0';
        return g_questIdBuf;
    }

    int Framework_Quest_GetLevel(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        return it->second.level;
    }

    float Framework_Quest_GetTimeRemaining(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        if (it->second.timeLimit <= 0) return -1;  // No limit
        float remaining = it->second.timeLimit - it->second.timeElapsed;
        return remaining > 0 ? remaining : 0;
    }

    float Framework_Quest_GetTimeElapsed(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return 0;
        return it->second.timeElapsed;
    }

    // ---- Active Quest List ----
    int Framework_Quest_GetActiveCount() {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_IN_PROGRESS && !kv.second.hidden) count++;
        }
        return count;
    }

    int Framework_Quest_GetActiveAt(int index) {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_IN_PROGRESS && !kv.second.hidden) {
                if (count == index) return kv.first;
                count++;
            }
        }
        return -1;
    }

    int Framework_Quest_GetCompletedCount() {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_COMPLETED) count++;
        }
        return count;
    }

    int Framework_Quest_GetCompletedAt(int index) {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_COMPLETED) {
                if (count == index) return kv.first;
                count++;
            }
        }
        return -1;
    }

    int Framework_Quest_GetAvailableCount() {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_NOT_STARTED && !kv.second.hidden) {
                if (Framework_Quest_CheckPrerequisites(kv.first)) count++;
            }
        }
        return count;
    }

    int Framework_Quest_GetAvailableAt(int index) {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.state == QUEST_STATE_NOT_STARTED && !kv.second.hidden) {
                if (Framework_Quest_CheckPrerequisites(kv.first)) {
                    if (count == index) return kv.first;
                    count++;
                }
            }
        }
        return -1;
    }

    // ---- Quest Tracking (HUD) ----
    void Framework_Quest_SetTracked(int questHandle, bool tracked) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return;

        if (tracked) {
            // Check max tracked limit
            int currentTracked = 0;
            for (const auto& kv : g_quests) {
                if (kv.second.tracked) currentTracked++;
            }
            if (currentTracked >= g_maxTracked && !it->second.tracked) {
                return;  // Can't track more
            }
        }
        it->second.tracked = tracked;
    }

    bool Framework_Quest_IsTracked(int questHandle) {
        auto it = g_quests.find(questHandle);
        if (it == g_quests.end()) return false;
        return it->second.tracked;
    }

    int Framework_Quest_GetTrackedCount() {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.tracked) count++;
        }
        return count;
    }

    int Framework_Quest_GetTrackedAt(int index) {
        int count = 0;
        for (const auto& kv : g_quests) {
            if (kv.second.tracked) {
                if (count == index) return kv.first;
                count++;
            }
        }
        return -1;
    }

    void Framework_Quest_SetMaxTracked(int maxTracked) {
        g_maxTracked = maxTracked > 0 ? maxTracked : 1;
    }

    // ---- Callbacks ----
    void Framework_Quest_SetOnStateChange(QuestStateCallback callback) {
        g_questStateCallback = callback;
    }

    void Framework_Quest_SetOnObjectiveUpdate(ObjectiveUpdateCallback callback) {
        g_objectiveUpdateCallback = callback;
    }

    // ---- Quest Chains ----
    int Framework_QuestChain_Create(const char* chainId) {
        if (!chainId) return -1;

        auto it = g_chainByStringId.find(chainId);
        if (it != g_chainByStringId.end()) {
            return it->second;
        }

        QuestChain chain;
        chain.handle = g_nextChainHandle++;
        chain.stringId = chainId;
        g_questChains[chain.handle] = chain;
        g_chainByStringId[chainId] = chain.handle;
        return chain.handle;
    }

    void Framework_QuestChain_AddQuest(int chainHandle, int questHandle) {
        auto it = g_questChains.find(chainHandle);
        if (it == g_questChains.end()) return;
        if (g_quests.find(questHandle) == g_quests.end()) return;
        it->second.questHandles.push_back(questHandle);
    }

    int Framework_QuestChain_GetCurrentQuest(int chainHandle) {
        auto it = g_questChains.find(chainHandle);
        if (it == g_questChains.end()) return -1;

        const QuestChain& chain = it->second;
        if (chain.questHandles.empty()) return -1;

        // Find first incomplete quest in chain
        for (int h : chain.questHandles) {
            auto qIt = g_quests.find(h);
            if (qIt != g_quests.end() && qIt->second.state != QUEST_STATE_COMPLETED) {
                return h;
            }
        }
        return -1;  // All complete
    }

    int Framework_QuestChain_GetProgress(int chainHandle) {
        auto it = g_questChains.find(chainHandle);
        if (it == g_questChains.end()) return 0;

        int completed = 0;
        for (int h : it->second.questHandles) {
            auto qIt = g_quests.find(h);
            if (qIt != g_quests.end() && qIt->second.state == QUEST_STATE_COMPLETED) {
                completed++;
            }
        }
        return completed;
    }

    int Framework_QuestChain_GetLength(int chainHandle) {
        auto it = g_questChains.find(chainHandle);
        if (it == g_questChains.end()) return 0;
        return (int)it->second.questHandles.size();
    }

    bool Framework_QuestChain_IsComplete(int chainHandle) {
        auto it = g_questChains.find(chainHandle);
        if (it == g_questChains.end()) return false;

        for (int h : it->second.questHandles) {
            auto qIt = g_quests.find(h);
            if (qIt == g_quests.end() || qIt->second.state != QUEST_STATE_COMPLETED) {
                return false;
            }
        }
        return !it->second.questHandles.empty();
    }

    // ---- Save/Load ----
    bool Framework_Quest_SaveProgress(int saveSlot, const char* key) {
        if (!Framework_Save_BeginSave(saveSlot)) return false;

        // Build save data string: questId:state:obj0progress:obj1progress...;
        std::string data;
        for (const auto& kv : g_quests) {
            const Quest& q = kv.second;
            data += q.stringId + ":" + std::to_string(q.state);
            for (const auto& obj : q.objectives) {
                data += ":" + std::to_string(obj.currentProgress);
            }
            data += ";";
        }
        Framework_Save_WriteString(key, data.c_str());
        return Framework_Save_EndSave();
    }

    bool Framework_Quest_LoadProgress(int saveSlot, const char* key) {
        if (!Framework_Save_BeginLoad(saveSlot)) return false;

        const char* data = Framework_Save_ReadString(key, "");
        if (!data || strlen(data) == 0) {
            Framework_Save_EndLoad();
            return false;
        }

        std::string str = data;
        size_t pos = 0;
        while ((pos = str.find(';')) != std::string::npos) {
            std::string entry = str.substr(0, pos);
            str.erase(0, pos + 1);

            // Parse questId:state:obj0:obj1:...
            std::vector<std::string> parts;
            size_t colonPos;
            while ((colonPos = entry.find(':')) != std::string::npos) {
                parts.push_back(entry.substr(0, colonPos));
                entry.erase(0, colonPos + 1);
            }
            parts.push_back(entry);

            if (parts.size() >= 2) {
                std::string questId = parts[0];
                int state = std::stoi(parts[1]);

                auto idIt = g_questByStringId.find(questId);
                if (idIt != g_questByStringId.end()) {
                    auto qIt = g_quests.find(idIt->second);
                    if (qIt != g_quests.end()) {
                        qIt->second.state = state;
                        for (size_t i = 2; i < parts.size() && (i - 2) < qIt->second.objectives.size(); i++) {
                            qIt->second.objectives[i - 2].currentProgress = std::stoi(parts[i]);
                            qIt->second.objectives[i - 2].completed =
                                qIt->second.objectives[i - 2].currentProgress >= qIt->second.objectives[i - 2].requiredCount;
                        }
                    }
                }
            }
        }
        Framework_Save_EndLoad();
        return true;
    }

    // ---- Global Management ----
    void Framework_Quest_Update(float deltaTime) {
        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            if (q.state != QUEST_STATE_IN_PROGRESS) continue;

            // Update time
            q.timeElapsed += deltaTime;

            // Check time limit
            if (q.timeLimit > 0 && q.timeElapsed >= q.timeLimit) {
                q.state = QUEST_STATE_FAILED;
                q.tracked = false;
                if (g_questStateCallback) {
                    g_questStateCallback(q.handle, QUEST_STATE_FAILED);
                }
            }
        }
    }

    void Framework_Quest_UndefineAll() {
        g_quests.clear();
        g_questByStringId.clear();
        g_questChains.clear();
        g_chainByStringId.clear();
        g_nextQuestHandle = 1;
        g_nextChainHandle = 1;
    }

    void Framework_Quest_ResetAllProgress() {
        for (auto& kv : g_quests) {
            Quest& q = kv.second;
            q.state = QUEST_STATE_NOT_STARTED;
            q.tracked = false;
            q.timeElapsed = 0;
            for (auto& obj : q.objectives) {
                obj.currentProgress = 0;
                obj.completed = false;
            }
        }
    }

    int Framework_Quest_GetDefinedCount() {
        return (int)g_quests.size();
    }

    // ========================================================================
    // 2D LIGHTING SYSTEM
    // ========================================================================

    struct Light2D {
        int id = 0;
        int type = LIGHT_TYPE_POINT;
        float x = 0, y = 0;
        float radius = 100.0f;
        unsigned char r = 255, g = 255, b = 255;
        float intensity = 1.0f;
        float falloff = 1.0f;
        bool enabled = true;
        int layer = 0;

        // Spot light properties
        float direction = 0;      // Angle in degrees
        float coneAngle = 45.0f;  // Half-angle of cone
        float softEdge = 0.1f;    // Soft edge factor

        // Effects
        float flickerAmount = 0;
        float flickerSpeed = 0;
        float flickerPhase = 0;
        float pulseMin = 1.0f, pulseMax = 1.0f;
        float pulseSpeed = 0;
        float pulsePhase = 0;

        // Attachment
        int attachedEntity = -1;
        float offsetX = 0, offsetY = 0;
    };

    struct ShadowOccluder {
        int id = 0;
        int type = 0;  // 0=box, 1=circle, 2=polygon
        float x = 0, y = 0;
        float rotation = 0;
        float width = 0, height = 0;
        float radius = 0;
        std::vector<float> points;  // For polygon
        bool enabled = true;

        // Attachment
        int attachedEntity = -1;
        float offsetX = 0, offsetY = 0;
    };

    struct LightingState {
        bool initialized = false;
        bool enabled = true;
        int width = 800, height = 600;

        // Render targets
        RenderTexture2D lightMap;
        RenderTexture2D sceneBuffer;
        bool hasRenderTargets = false;

        // Ambient
        unsigned char ambientR = 50, ambientG = 50, ambientB = 70;
        float ambientIntensity = 0.3f;

        // Directional light
        bool directionalEnabled = false;
        float directionalAngle = -45.0f;
        unsigned char dirR = 255, dirG = 255, dirB = 200;
        float dirIntensity = 0.5f;

        // Shadows
        int shadowQuality = SHADOW_QUALITY_HARD;
        float shadowBlur = 2.0f;
        unsigned char shadowR = 0, shadowG = 0, shadowB = 0, shadowA = 200;

        // Day/Night cycle
        bool dayNightEnabled = false;
        float timeOfDay = 12.0f;  // 0-24
        float dayNightSpeed = 1.0f;
        float sunriseTime = 6.0f;
        float sunsetTime = 18.0f;
        unsigned char dayAmbientR = 200, dayAmbientG = 200, dayAmbientB = 220;
        float dayAmbientIntensity = 0.8f;
        unsigned char nightAmbientR = 20, nightAmbientG = 20, nightAmbientB = 50;
        float nightAmbientIntensity = 0.1f;
    };

    static LightingState g_lighting;
    static std::unordered_map<int, Light2D> g_lights;
    static std::unordered_map<int, ShadowOccluder> g_occluders;
    static int g_nextLightId = 1;
    static int g_nextOccluderId = 1;

    // Helper: Draw a single light to the light map
    static void DrawLight2D(const Light2D& light, float effectiveIntensity) {
        if (!light.enabled || effectiveIntensity <= 0) return;

        Color lightColor = { light.r, light.g, light.b, (unsigned char)(255 * effectiveIntensity) };

        if (light.type == LIGHT_TYPE_POINT) {
            // Draw radial gradient
            for (float r = light.radius; r > 0; r -= 2.0f) {
                float t = r / light.radius;
                float falloffFactor = powf(1.0f - t, light.falloff);
                unsigned char alpha = (unsigned char)(255 * effectiveIntensity * falloffFactor);
                Color c = { light.r, light.g, light.b, alpha };
                DrawCircle((int)light.x, (int)light.y, r, c);
            }
        }
        else if (light.type == LIGHT_TYPE_SPOT) {
            // Draw cone shape
            float dirRad = light.direction * DEG2RAD;
            float coneRad = light.coneAngle * DEG2RAD;

            int segments = 32;
            for (float r = light.radius; r > 0; r -= 3.0f) {
                float t = r / light.radius;
                float falloffFactor = powf(1.0f - t, light.falloff);
                unsigned char alpha = (unsigned char)(255 * effectiveIntensity * falloffFactor);
                Color c = { light.r, light.g, light.b, alpha };

                // Draw arc segments
                for (int i = 0; i < segments; i++) {
                    float a1 = dirRad - coneRad + (2.0f * coneRad * i / segments);
                    float a2 = dirRad - coneRad + (2.0f * coneRad * (i + 1) / segments);

                    Vector2 p1 = { light.x + cosf(a1) * r, light.y + sinf(a1) * r };
                    Vector2 p2 = { light.x + cosf(a2) * r, light.y + sinf(a2) * r };
                    Vector2 center = { light.x, light.y };

                    DrawTriangle(center, p1, p2, c);
                }
            }
        }
    }

    // ---- Lighting System Control ----
    void Framework_Lighting_Initialize(int width, int height) {
        g_lighting.width = width;
        g_lighting.height = height;

        if (g_lighting.hasRenderTargets) {
            UnloadRenderTexture(g_lighting.lightMap);
            UnloadRenderTexture(g_lighting.sceneBuffer);
        }

        g_lighting.lightMap = LoadRenderTexture(width, height);
        g_lighting.sceneBuffer = LoadRenderTexture(width, height);
        g_lighting.hasRenderTargets = true;
        g_lighting.initialized = true;
    }

    void Framework_Lighting_Shutdown() {
        if (g_lighting.hasRenderTargets) {
            UnloadRenderTexture(g_lighting.lightMap);
            UnloadRenderTexture(g_lighting.sceneBuffer);
            g_lighting.hasRenderTargets = false;
        }
        g_lights.clear();
        g_occluders.clear();
        g_lighting.initialized = false;
    }

    void Framework_Lighting_SetEnabled(bool enabled) {
        g_lighting.enabled = enabled;
    }

    bool Framework_Lighting_IsEnabled() {
        return g_lighting.enabled;
    }

    void Framework_Lighting_SetResolution(int width, int height) {
        if (g_lighting.initialized && (width != g_lighting.width || height != g_lighting.height)) {
            Framework_Lighting_Initialize(width, height);
        }
    }

    // ---- Ambient Light ----
    void Framework_Lighting_SetAmbientColor(unsigned char r, unsigned char g, unsigned char b) {
        g_lighting.ambientR = r;
        g_lighting.ambientG = g;
        g_lighting.ambientB = b;
    }

    void Framework_Lighting_SetAmbientIntensity(float intensity) {
        g_lighting.ambientIntensity = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);
    }

    float Framework_Lighting_GetAmbientIntensity() {
        return g_lighting.ambientIntensity;
    }

    // ---- Point Lights ----
    int Framework_Light_CreatePoint(float x, float y, float radius) {
        Light2D light;
        light.id = g_nextLightId++;
        light.type = LIGHT_TYPE_POINT;
        light.x = x;
        light.y = y;
        light.radius = radius;
        g_lights[light.id] = light;
        return light.id;
    }

    void Framework_Light_Destroy(int lightId) {
        g_lights.erase(lightId);
    }

    void Framework_Light_SetPosition(int lightId, float x, float y) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.x = x;
            it->second.y = y;
        }
    }

    void Framework_Light_GetPosition(int lightId, float* x, float* y) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            if (x) *x = it->second.x;
            if (y) *y = it->second.y;
        }
    }

    void Framework_Light_SetColor(int lightId, unsigned char r, unsigned char g, unsigned char b) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.r = r;
            it->second.g = g;
            it->second.b = b;
        }
    }

    void Framework_Light_SetIntensity(int lightId, float intensity) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.intensity = intensity < 0 ? 0 : intensity;
        }
    }

    float Framework_Light_GetIntensity(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.intensity;
        }
        return 0;
    }

    void Framework_Light_SetRadius(int lightId, float radius) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.radius = radius > 0 ? radius : 1;
        }
    }

    float Framework_Light_GetRadius(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.radius;
        }
        return 0;
    }

    void Framework_Light_SetEnabled(int lightId, bool enabled) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.enabled = enabled;
        }
    }

    bool Framework_Light_IsEnabled(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.enabled;
        }
        return false;
    }

    // ---- Spot Lights ----
    int Framework_Light_CreateSpot(float x, float y, float radius, float angle, float coneAngle) {
        Light2D light;
        light.id = g_nextLightId++;
        light.type = LIGHT_TYPE_SPOT;
        light.x = x;
        light.y = y;
        light.radius = radius;
        light.direction = angle;
        light.coneAngle = coneAngle;
        g_lights[light.id] = light;
        return light.id;
    }

    void Framework_Light_SetDirection(int lightId, float angle) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.direction = angle;
        }
    }

    float Framework_Light_GetDirection(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.direction;
        }
        return 0;
    }

    void Framework_Light_SetConeAngle(int lightId, float angle) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.coneAngle = angle > 0 ? angle : 1;
        }
    }

    float Framework_Light_GetConeAngle(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.coneAngle;
        }
        return 0;
    }

    void Framework_Light_SetSoftEdge(int lightId, float softness) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.softEdge = softness < 0 ? 0 : (softness > 1 ? 1 : softness);
        }
    }

    // ---- Directional Light (Global) ----
    void Framework_Lighting_SetDirectionalAngle(float angle) {
        g_lighting.directionalAngle = angle;
    }

    void Framework_Lighting_SetDirectionalColor(unsigned char r, unsigned char g, unsigned char b) {
        g_lighting.dirR = r;
        g_lighting.dirG = g;
        g_lighting.dirB = b;
    }

    void Framework_Lighting_SetDirectionalIntensity(float intensity) {
        g_lighting.dirIntensity = intensity < 0 ? 0 : intensity;
    }

    void Framework_Lighting_SetDirectionalEnabled(bool enabled) {
        g_lighting.directionalEnabled = enabled;
    }

    // ---- Light Properties ----
    void Framework_Light_SetFalloff(int lightId, float falloff) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.falloff = falloff > 0 ? falloff : 0.1f;
        }
    }

    float Framework_Light_GetFalloff(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.falloff;
        }
        return 1.0f;
    }

    void Framework_Light_SetFlicker(int lightId, float amount, float speed) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.flickerAmount = amount;
            it->second.flickerSpeed = speed;
        }
    }

    void Framework_Light_SetPulse(int lightId, float minIntensity, float maxIntensity, float speed) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.pulseMin = minIntensity;
            it->second.pulseMax = maxIntensity;
            it->second.pulseSpeed = speed;
        }
    }

    void Framework_Light_SetLayer(int lightId, int layer) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.layer = layer;
        }
    }

    int Framework_Light_GetLayer(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.layer;
        }
        return 0;
    }

    // ---- Light Attachment ----
    void Framework_Light_AttachToEntity(int lightId, int entityId, float offsetX, float offsetY) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.attachedEntity = entityId;
            it->second.offsetX = offsetX;
            it->second.offsetY = offsetY;
        }
    }

    void Framework_Light_Detach(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            it->second.attachedEntity = -1;
        }
    }

    // ---- Shadow Occluders ----
    int Framework_Shadow_CreateBox(float x, float y, float width, float height) {
        ShadowOccluder occ;
        occ.id = g_nextOccluderId++;
        occ.type = 0;
        occ.x = x;
        occ.y = y;
        occ.width = width;
        occ.height = height;
        g_occluders[occ.id] = occ;
        return occ.id;
    }

    int Framework_Shadow_CreateCircle(float x, float y, float radius) {
        ShadowOccluder occ;
        occ.id = g_nextOccluderId++;
        occ.type = 1;
        occ.x = x;
        occ.y = y;
        occ.radius = radius;
        g_occluders[occ.id] = occ;
        return occ.id;
    }

    int Framework_Shadow_CreatePolygon(const float* points, int pointCount) {
        ShadowOccluder occ;
        occ.id = g_nextOccluderId++;
        occ.type = 2;
        if (points && pointCount > 0) {
            occ.points.assign(points, points + pointCount * 2);
        }
        g_occluders[occ.id] = occ;
        return occ.id;
    }

    void Framework_Shadow_Destroy(int occluderId) {
        g_occluders.erase(occluderId);
    }

    void Framework_Shadow_SetPosition(int occluderId, float x, float y) {
        auto it = g_occluders.find(occluderId);
        if (it != g_occluders.end()) {
            it->second.x = x;
            it->second.y = y;
        }
    }

    void Framework_Shadow_SetRotation(int occluderId, float angle) {
        auto it = g_occluders.find(occluderId);
        if (it != g_occluders.end()) {
            it->second.rotation = angle;
        }
    }

    void Framework_Shadow_SetEnabled(int occluderId, bool enabled) {
        auto it = g_occluders.find(occluderId);
        if (it != g_occluders.end()) {
            it->second.enabled = enabled;
        }
    }

    void Framework_Shadow_AttachToEntity(int occluderId, int entityId, float offsetX, float offsetY) {
        auto it = g_occluders.find(occluderId);
        if (it != g_occluders.end()) {
            it->second.attachedEntity = entityId;
            it->second.offsetX = offsetX;
            it->second.offsetY = offsetY;
        }
    }

    void Framework_Shadow_Detach(int occluderId) {
        auto it = g_occluders.find(occluderId);
        if (it != g_occluders.end()) {
            it->second.attachedEntity = -1;
        }
    }

    // ---- Shadow Settings ----
    void Framework_Lighting_SetShadowQuality(int quality) {
        g_lighting.shadowQuality = quality;
    }

    int Framework_Lighting_GetShadowQuality() {
        return g_lighting.shadowQuality;
    }

    void Framework_Lighting_SetShadowBlur(float blur) {
        g_lighting.shadowBlur = blur > 0 ? blur : 0;
    }

    void Framework_Lighting_SetShadowColor(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        g_lighting.shadowR = r;
        g_lighting.shadowG = g;
        g_lighting.shadowB = b;
        g_lighting.shadowA = a;
    }

    // ---- Day/Night Cycle ----
    void Framework_Lighting_SetTimeOfDay(float time) {
        while (time < 0) time += 24.0f;
        while (time >= 24.0f) time -= 24.0f;
        g_lighting.timeOfDay = time;
    }

    float Framework_Lighting_GetTimeOfDay() {
        return g_lighting.timeOfDay;
    }

    void Framework_Lighting_SetDayNightSpeed(float speed) {
        g_lighting.dayNightSpeed = speed;
    }

    void Framework_Lighting_SetDayNightEnabled(bool enabled) {
        g_lighting.dayNightEnabled = enabled;
    }

    void Framework_Lighting_SetSunriseTime(float hour) {
        g_lighting.sunriseTime = hour;
    }

    void Framework_Lighting_SetSunsetTime(float hour) {
        g_lighting.sunsetTime = hour;
    }

    void Framework_Lighting_SetDayAmbient(unsigned char r, unsigned char g, unsigned char b, float intensity) {
        g_lighting.dayAmbientR = r;
        g_lighting.dayAmbientG = g;
        g_lighting.dayAmbientB = b;
        g_lighting.dayAmbientIntensity = intensity;
    }

    void Framework_Lighting_SetNightAmbient(unsigned char r, unsigned char g, unsigned char b, float intensity) {
        g_lighting.nightAmbientR = r;
        g_lighting.nightAmbientG = g;
        g_lighting.nightAmbientB = b;
        g_lighting.nightAmbientIntensity = intensity;
    }

    // ---- Rendering ----
    void Framework_Lighting_BeginLightPass() {
        if (!g_lighting.initialized || !g_lighting.hasRenderTargets) return;

        BeginTextureMode(g_lighting.sceneBuffer);
        ClearBackground(BLACK);
    }

    void Framework_Lighting_EndLightPass() {
        if (!g_lighting.initialized || !g_lighting.hasRenderTargets) return;
        EndTextureMode();
    }

    void Framework_Lighting_RenderToScreen() {
        if (!g_lighting.initialized || !g_lighting.hasRenderTargets || !g_lighting.enabled) return;

        // Render light map
        BeginTextureMode(g_lighting.lightMap);

        // Start with ambient color
        unsigned char ambR = g_lighting.ambientR;
        unsigned char ambG = g_lighting.ambientG;
        unsigned char ambB = g_lighting.ambientB;
        float ambInt = g_lighting.ambientIntensity;

        // Apply day/night cycle
        if (g_lighting.dayNightEnabled) {
            float t = g_lighting.timeOfDay;
            float dayFactor = 0;

            if (t >= g_lighting.sunriseTime && t < g_lighting.sunriseTime + 1) {
                dayFactor = (t - g_lighting.sunriseTime);
            }
            else if (t >= g_lighting.sunriseTime + 1 && t < g_lighting.sunsetTime) {
                dayFactor = 1.0f;
            }
            else if (t >= g_lighting.sunsetTime && t < g_lighting.sunsetTime + 1) {
                dayFactor = 1.0f - (t - g_lighting.sunsetTime);
            }

            ambR = (unsigned char)(g_lighting.nightAmbientR + dayFactor * (g_lighting.dayAmbientR - g_lighting.nightAmbientR));
            ambG = (unsigned char)(g_lighting.nightAmbientG + dayFactor * (g_lighting.dayAmbientG - g_lighting.nightAmbientG));
            ambB = (unsigned char)(g_lighting.nightAmbientB + dayFactor * (g_lighting.dayAmbientB - g_lighting.nightAmbientB));
            ambInt = g_lighting.nightAmbientIntensity + dayFactor * (g_lighting.dayAmbientIntensity - g_lighting.nightAmbientIntensity);
        }

        ClearBackground({ (unsigned char)(ambR * ambInt), (unsigned char)(ambG * ambInt), (unsigned char)(ambB * ambInt), 255 });

        // Draw all lights with additive blending
        BeginBlendMode(BLEND_ADDITIVE);

        for (auto& kv : g_lights) {
            Light2D& light = kv.second;
            if (!light.enabled) continue;

            // Calculate effective intensity with flicker/pulse
            float effectiveIntensity = light.intensity;

            if (light.flickerAmount > 0 && light.flickerSpeed > 0) {
                float flicker = sinf(light.flickerPhase) * light.flickerAmount;
                effectiveIntensity *= (1.0f + flicker);
            }

            if (light.pulseSpeed > 0) {
                float pulse = (sinf(light.pulsePhase) + 1.0f) * 0.5f;
                effectiveIntensity *= light.pulseMin + pulse * (light.pulseMax - light.pulseMin);
            }

            DrawLight2D(light, effectiveIntensity);
        }

        EndBlendMode();
        EndTextureMode();

        // Draw scene with lighting applied
        DrawTextureRec(
            g_lighting.sceneBuffer.texture,
            { 0, 0, (float)g_lighting.width, -(float)g_lighting.height },
            { 0, 0 },
            WHITE
        );

        // Apply light map with multiply blend
        BeginBlendMode(BLEND_MULTIPLIED);
        DrawTextureRec(
            g_lighting.lightMap.texture,
            { 0, 0, (float)g_lighting.width, -(float)g_lighting.height },
            { 0, 0 },
            WHITE
        );
        EndBlendMode();
    }

    void Framework_Lighting_Update(float deltaTime) {
        // Update day/night cycle
        if (g_lighting.dayNightEnabled) {
            g_lighting.timeOfDay += deltaTime * g_lighting.dayNightSpeed / 3600.0f;  // Convert to game hours
            while (g_lighting.timeOfDay >= 24.0f) g_lighting.timeOfDay -= 24.0f;
        }

        // Update light effects and attachments
        for (auto& kv : g_lights) {
            Light2D& light = kv.second;

            // Update flicker
            if (light.flickerSpeed > 0) {
                light.flickerPhase += deltaTime * light.flickerSpeed;
            }

            // Update pulse
            if (light.pulseSpeed > 0) {
                light.pulsePhase += deltaTime * light.pulseSpeed;
            }

            // Update attachment
            if (light.attachedEntity >= 0) {
                auto it = g_transform2D.find(light.attachedEntity);
                if (it != g_transform2D.end()) {
                    light.x = it->second.position.x + light.offsetX;
                    light.y = it->second.position.y + light.offsetY;
                }
            }
        }

        // Update occluder attachments
        for (auto& kv : g_occluders) {
            ShadowOccluder& occ = kv.second;
            if (occ.attachedEntity >= 0) {
                auto it = g_transform2D.find(occ.attachedEntity);
                if (it != g_transform2D.end()) {
                    occ.x = it->second.position.x + occ.offsetX;
                    occ.y = it->second.position.y + occ.offsetY;
                }
            }
        }
    }

    // ---- Light Queries ----
    int Framework_Light_GetCount() {
        return (int)g_lights.size();
    }

    int Framework_Light_GetAt(int index) {
        int i = 0;
        for (const auto& kv : g_lights) {
            if (i == index) return kv.first;
            i++;
        }
        return -1;
    }

    int Framework_Light_GetType(int lightId) {
        auto it = g_lights.find(lightId);
        if (it != g_lights.end()) {
            return it->second.type;
        }
        return -1;
    }

    float Framework_Light_GetBrightnessAt(float x, float y) {
        float totalBrightness = g_lighting.ambientIntensity;

        for (const auto& kv : g_lights) {
            const Light2D& light = kv.second;
            if (!light.enabled) continue;

            float dx = x - light.x;
            float dy = y - light.y;
            float dist = sqrtf(dx * dx + dy * dy);

            if (dist < light.radius) {
                float t = dist / light.radius;
                float contribution = light.intensity * powf(1.0f - t, light.falloff);

                if (light.type == LIGHT_TYPE_SPOT) {
                    // Check if point is within cone
                    float angleToPoint = atan2f(dy, dx) * RAD2DEG;
                    float angleDiff = fabsf(angleToPoint - light.direction);
                    while (angleDiff > 180) angleDiff -= 360;
                    angleDiff = fabsf(angleDiff);

                    if (angleDiff > light.coneAngle) {
                        contribution = 0;
                    }
                    else {
                        contribution *= 1.0f - (angleDiff / light.coneAngle);
                    }
                }

                totalBrightness += contribution;
            }
        }

        return totalBrightness > 1.0f ? 1.0f : totalBrightness;
    }

    // ---- Global Management ----
    void Framework_Light_DestroyAll() {
        g_lights.clear();
        g_nextLightId = 1;
    }

    void Framework_Shadow_DestroyAll() {
        g_occluders.clear();
        g_nextOccluderId = 1;
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
