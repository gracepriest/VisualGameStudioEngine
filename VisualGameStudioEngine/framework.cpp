// framework.cpp
#include "pch.h"
#include "framework.h"
#include <unordered_map>
#include <algorithm>
#include <cctype>
#include <string>

// Forward declarations for functions used before they are defined later
extern "C" void Framework_UpdateAllMusic();
extern "C" void Framework_ResourcesShutdown();

// ----------------------------------------------------------------------------
// Simple handle maps for SFX and basic music (as in your original section)
// ----------------------------------------------------------------------------
static std::unordered_map<int, Sound>  g_sounds;
//static std::unordered_map<int, Music>  g_music;
static int g_nextSound = 1;
// --- Fixed timestep helpers ---
static double g_fixedStep = 1.0 / 60.0;
static double g_accum = 0.0;

extern "C" {

    // ---------------------------
    // Window/App lifecycle
    // ---------------------------
    bool Framework_Initialize(int width, int height, const char* title) {
        InitWindow(width, height, title);
        SetTargetFPS(60);
        return true;
    }

    static DrawCallback userDrawCallback = nullptr;

    void Framework_SetDrawCallback(DrawCallback callback) {
        userDrawCallback = callback;
    }

    void Framework_Update() {
        BeginDrawing();
       // ClearBackground(RAYWHITE);

        if (userDrawCallback != nullptr) {
            userDrawCallback();
        }

        EndDrawing();
        Framework_UpdateAllMusic(); // keep music streams alive

        g_accum += (double)GetFrameTime();
    }

    void Framework_BeginDrawing() { BeginDrawing(); }
    void Framework_EndDrawing() { EndDrawing(); }

    void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        ClearBackground(color);
    }

    bool Framework_ShouldClose() { return WindowShouldClose(); }
    void Framework_Shutdown() { Framework_ResourcesShutdown(); CloseWindow(); }

    void   Framework_SetTargetFPS(int fps) { SetTargetFPS(fps); }
    float  Framework_GetFrameTime() { return GetFrameTime(); }
    double Framework_GetTime() { return GetTime(); }
    int    Framework_GetFPS() { return GetFPS(); }   // FIXED

    // ---------------------------
    // (Optional) Window mgmt helpers you already had
    // ---------------------------
    void   Framework_SetWindowTitle(const char* title) { SetWindowTitle(title); }
    void   Framework_SetWindowIcon(Image image) { SetWindowIcon(image); }
    void   Framework_SetWindowPosition(int x, int y) { SetWindowPosition(x, y); }
    void   Framework_SetWindowMonitor(int monitor) { SetWindowMonitor(monitor); }
    void   Framework_SetWindowMinSize(int width, int height) { SetWindowMinSize(width, height); }
    void   Framework_SetWindowSize(int width, int height) { SetWindowSize(width, height); }
    Vector2 Framework_GetScreenToWorld2D(Vector2 p, Camera2D c) { return GetScreenToWorld2D(p, c); }

    // ---------------------------
    // Keyboard
    // ---------------------------
    bool Framework_IsKeyPressed(int key) { return IsKeyPressed(key); }
    bool Framework_IsKeyPressedRepeat(int key) { return IsKeyPressedRepeat(key); }
    bool Framework_IsKeyDown(int key) { return IsKeyDown(key); }
    bool Framework_IsKeyReleased(int key) { return IsKeyReleased(key); }
    bool Framework_IsKeyUp(int key) { return IsKeyUp(key); }
    int  Framework_GetKeyPressed() { return GetKeyPressed(); }
    int  Framework_GetCharPressed() { return GetCharPressed(); }
    void Framework_SetExitKey(int key) { SetExitKey(key); }

    // ---------------------------
    // Mouse
    // ---------------------------
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

    // ---------------------------
    // Drawing: text + basic shapes
    // ---------------------------
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

    void Framework_DrawPixel(int x, int y,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawPixel(x, y, color);
    }

    void Framework_DrawLine(int x0, int y0, int x1, int y1,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawLine(x0, y0, x1, y1, color);
    }

    void Framework_DrawCircle(int cx, int cy, float radius,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color color = { r, g, b, a };
        DrawCircle(cx, cy, radius, color);
    }

    // ---------------------------
    // Collision
    // ---------------------------
    bool      Framework_CheckCollisionRecs(Rectangle a, Rectangle b) { return CheckCollisionRecs(a, b); }
    bool      Framework_CheckCollisionCircles(Vector2 c1, float r1, Vector2 c2, float r2) { return CheckCollisionCircles(c1, r1, c2, r2); }
    bool      Framework_CheckCollisionCircleRec(Vector2 c, float r, Rectangle rec) { return CheckCollisionCircleRec(c, r, rec); }
    bool      Framework_CheckCollisionCircleLine(Vector2 c, float r, Vector2 p1, Vector2 p2) { return CheckCollisionCircleLine(c, r, p1, p2); }
    bool      Framework_CheckCollisionPointRec(Vector2 p, Rectangle rec) { return CheckCollisionPointRec(p, rec); }
    bool      Framework_CheckCollisionPointCircle(Vector2 p, Vector2 c, float r) { return CheckCollisionPointCircle(p, c, r); }
    bool      Framework_CheckCollisionPointTriangle(Vector2 p, Vector2 p1, Vector2 p2, Vector2 p3) { return CheckCollisionPointTriangle(p, p1, p2, p3); }
    bool      Framework_CheckCollisionPointLine(Vector2 p, Vector2 p1, Vector2 p2, int thr) { return CheckCollisionPointLine(p, p1, p2, thr); }
    bool      Framework_CheckCollisionPointPoly(Vector2 p, const Vector2* pts, int n) { return CheckCollisionPointPoly(p, pts, n); }
    bool      Framework_CheckCollisionLines(Vector2 s1, Vector2 e1, Vector2 s2, Vector2 e2, Vector2* cp) { return CheckCollisionLines(s1, e1, s2, e2, cp); }
    Rectangle Framework_GetCollisionRec(Rectangle a, Rectangle b) { return GetCollisionRec(a, b); }

    // =========================================================================
    //                          TEXTURE / IMAGE API
    // =========================================================================

    // --- Loaders ---
    Texture2D Framework_LoadTexture(const char* fileName) {
        return LoadTexture(fileName);
    }

    Texture2D Framework_LoadTextureFromImage(Image image) {
        return LoadTextureFromImage(image);
    }

    TextureCubemap Framework_LoadTextureCubemap(Image image, int layout) {
        return LoadTextureCubemap(image, layout);
    }

    RenderTexture2D Framework_LoadRenderTexture(int width, int height) {
        return LoadRenderTexture(width, height);
    }

    // --- Validity / Unload ---
    bool Framework_IsTextureValid(Texture2D texture) {
        // If your raylib uses IsTextureReady/IsRenderTextureReady, adjust here.
        return IsTextureValid(texture);
    }

    void Framework_UnloadTexture(Texture2D texture) {
        UnloadTexture(texture);
    }

    bool Framework_IsRenderTextureValid(RenderTexture2D target) {
        return IsRenderTextureValid(target);
    }

    void Framework_UnloadRenderTexture(RenderTexture2D target) {
        UnloadRenderTexture(target);
    }

    // --- Updates & config ---
    void Framework_UpdateTexture(Texture2D texture, const void* pixels) {
        UpdateTexture(texture, pixels);
    }

    void Framework_UpdateTextureRec(Texture2D texture, Rectangle rec, const void* pixels) {
        UpdateTextureRec(texture, rec, pixels);
    }

    void Framework_GenTextureMipmaps(Texture2D* texture) {
        GenTextureMipmaps(texture);
    }

    void Framework_SetTextureFilter(Texture2D texture, int filter) {
        SetTextureFilter(texture, filter); // e.g., TEXTURE_FILTER_BILINEAR
    }

    void Framework_SetTextureWrap(Texture2D texture, int wrap) {
        SetTextureWrap(texture, wrap);     // e.g., TEXTURE_WRAP_CLAMP
    }

    // --- Drawing wrappers (with tint as RGBA bytes for easy VB marshalling) ---
    void Framework_DrawTexture(Texture2D texture, int posX, int posY,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTexture(texture, posX, posY, tint);
    }

    void Framework_DrawTextureV(Texture2D texture, Vector2 position,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTextureV(texture, position, tint);
    }

    void Framework_DrawTextureEx(Texture2D texture, Vector2 position, float rotation, float scale,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTextureEx(texture, position, rotation, scale, tint);
    }

    void Framework_DrawTextureRec(Texture2D texture, Rectangle source, Vector2 position,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTextureRec(texture, source, position, tint);
    }

    void Framework_DrawTexturePro(Texture2D texture, Rectangle source, Rectangle dest, Vector2 origin, float rotation,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTexturePro(texture, source, dest, origin, rotation, tint);
    }

    void Framework_DrawTextureNPatch(Texture2D texture, NPatchInfo nPatchInfo, Rectangle dest, Vector2 origin, float rotation,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTextureNPatch(texture, nPatchInfo, dest, origin, rotation, tint);
    }

    // --- Render-to-texture & 2D camera ---
    void Framework_BeginTextureMode(RenderTexture2D rt) { BeginTextureMode(rt); }
    void Framework_EndTextureMode() { EndTextureMode(); }

    void Framework_BeginMode2D(Camera2D cam) { BeginMode2D(cam); }
    void Framework_EndMode2D() { EndMode2D(); }

    // --- Sprite sheet helper (0-based frame index) ---
    Rectangle Framework_SpriteFrame(Rectangle sheetArea, int frameW, int frameH, int index, int columns) {
        Rectangle r{};
        r.x = sheetArea.x + (index % columns) * frameW;
        r.y = sheetArea.y + (index / columns) * frameH;
        r.width = (float)frameW;
        r.height = (float)frameH;
        return r;
    }

    // --- Image utilities ---
    Image Framework_LoadImage(const char* fileName) { return LoadImage(fileName); }
    void  Framework_UnloadImage(Image img) { UnloadImage(img); }
    void  Framework_ImageColorInvert(Image* img) { ImageColorInvert(img); }
    void  Framework_ImageResize(Image* img, int w, int h) { ImageResize(img, w, h); }
    void  Framework_ImageFlipVertical(Image* img) { ImageFlipVertical(img); }

    // --- Fonts & advanced text ---
    Font Framework_LoadFontEx(const char* fileName, int fontSize, int* glyphs, int glyphCount) {
        return LoadFontEx(fileName, fontSize, glyphs, glyphCount);
    }
    void Framework_UnloadFont(Font font) { UnloadFont(font); }
    void Framework_DrawTextEx(Font font, const char* text, Vector2 pos, float fontSize, float spacing,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        Color tint = { r, g, b, a };
        DrawTextEx(font, text, pos, fontSize, spacing, tint);
    }

   

    void   Framework_SetFixedStep(double seconds) { g_fixedStep = seconds; }
    void   Framework_ResetFixedClock() { g_accum = 0.0; }
    bool Framework_StepFixed() {
        if (g_accum >= g_fixedStep) { g_accum -= g_fixedStep; return true; }
        return false;
    }
    double Framework_GetFixedStep() { return g_fixedStep; }
    double Framework_GetAccumulator() { return g_accum; }
    void   Framework_DrawFPS(int x, int y) { DrawFPS(x, y); }
    void   Framework_DrawGrid(int slices, float spacing) { DrawGrid(slices, spacing); }

    // --- Audio core ---
    bool Framework_InitAudio() { InitAudioDevice(); return IsAudioDeviceReady(); }
    void Framework_CloseAudio() {
        for (auto& kv : g_sounds) UnloadSound(kv.second);
        g_sounds.clear();
        CloseAudioDevice();
    }

    


    // --- Sound (SFX) ---
    int  Framework_LoadSoundH(const char* file) {
        Sound s = LoadSound(file);
        int id = g_nextSound++;
        g_sounds[id] = s;
        return id;
    }
    void Framework_UnloadSoundH(int h) { if (g_sounds.count(h)) { UnloadSound(g_sounds[h]); g_sounds.erase(h); } }
    void Framework_PlaySoundH(int h) { if (g_sounds.count(h)) PlaySound(g_sounds[h]); }
    void Framework_StopSoundH(int h) { if (g_sounds.count(h)) StopSound(g_sounds[h]); }
    void Framework_PauseSoundH(int h) { if (g_sounds.count(h)) PauseSound(g_sounds[h]); }
    void Framework_ResumeSoundH(int h) { if (g_sounds.count(h)) ResumeSound(g_sounds[h]); }
    void Framework_SetSoundVolumeH(int h, float v) { if (g_sounds.count(h)) SetSoundVolume(g_sounds[h], v); }
    void Framework_SetSoundPitchH(int h, float p) { if (g_sounds.count(h)) SetSoundPitch(g_sounds[h], p); }
    void Framework_SetSoundPanH(int h, float pan) { if (g_sounds.count(h)) SetSoundPan(g_sounds[h], pan); }

    //// --- Music (legacy simple map) ---
    //int  Framework_LoadMusicH(const char* file) {
    //    Music m = LoadMusicStream(file);
    //    int id = g_nextMusic++;
    //    g_music[id] = m;
    //    return id;
    //}
    //void Framework_UnloadMusicH(int h) { if (g_music.count(h)) { UnloadMusicStream(g_music[h]); g_music.erase(h); } }
    //void Framework_PlayMusicH(int h) { if (g_music.count(h)) PlayMusicStream(g_music[h]); }
    //void Framework_StopMusicH(int h) { if (g_music.count(h)) StopMusicStream(g_music[h]); }
    //void Framework_PauseMusicH(int h) { if (g_music.count(h)) PauseMusicStream(g_music[h]); }
    //void Framework_ResumeMusicH(int h) { if (g_music.count(h)) ResumeMusicStream(g_music[h]); }
    //void Framework_UpdateMusicH(int h) { if (g_music.count(h)) UpdateMusicStream(g_music[h]); }
    //void Framework_SetMusicVolumeH(int h, float v) { if (g_music.count(h)) SetMusicVolume(g_music[h], v); }
    //void Framework_SetMusicPitchH(int h, float p) { if (g_music.count(h)) SetMusicPitch(g_music[h], p); }

    // --- Shaders ---
    Shader Framework_LoadShaderF(const char* vsPath, const char* fsPath) {
        return LoadShader(vsPath, fsPath);
    }
    void   Framework_UnloadShader(Shader sh) { UnloadShader(sh); }
    void   Framework_BeginShaderMode(Shader sh) { BeginShaderMode(sh); }
    void   Framework_EndShaderMode() { EndShaderMode(); }
    int    Framework_GetShaderLocation(Shader sh, const char* name) { return GetShaderLocation(sh, name); }

    void Framework_SetShaderValue1f(Shader sh, int loc, float v) { SetShaderValue(sh, loc, &v, SHADER_UNIFORM_FLOAT); }
    void Framework_SetShaderValue2f(Shader sh, int loc, float x, float y) { float a[2]{ x,y }; SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC2); }
    void Framework_SetShaderValue3f(Shader sh, int loc, float x, float y, float z) { float a[3]{ x,y,z }; SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC3); }
    void Framework_SetShaderValue4f(Shader sh, int loc, float x, float y, float z, float w) { float a[4]{ x,y,z,w }; SetShaderValue(sh, loc, a, SHADER_UNIFORM_VEC4); }
    void Framework_SetShaderValue1i(Shader sh, int loc, int v) { SetShaderValue(sh, loc, &v, SHADER_UNIFORM_INT); }

} // extern "C"

// ============================================================================
//                           RESOURCE CACHES
//                  (Textures, Fonts, Music - handle-based)
// ============================================================================

namespace {

    // path normalization: lowercase + forward slashes
    std::string NormalizePath(std::string p) {
        std::replace(p.begin(), p.end(), '\\', '/');
        std::transform(p.begin(), p.end(), p.begin(),
            [](unsigned char c) { return (unsigned char)std::tolower(c); });
        return p;
    }

    // ------------------- TEXTURES -------------------
    struct TexEntry {
        Texture2D   tex{};
        int         refCount = 0;
        std::string path;
        bool        valid = false;
    };

    static std::unordered_map<int, TexEntry>        g_texByHandle;
    static std::unordered_map<std::string, int>     g_handleByTexPath;
    static int                                      g_nextTexHandle = 1;

    int AcquireTextureH_Internal(const char* cpath) {
        std::string path = NormalizePath(cpath ? cpath : "");
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

    // ------------------- FONTS -------------------
    struct FontEntry {
        Font        font{};
        int         refCount = 0;
        std::string key;   // path|size (normalized)
        bool        valid = false;
    };

    static std::unordered_map<int, FontEntry>       g_fontByHandle;
    static std::unordered_map<std::string, int>     g_handleByFontKey;
    static int                                      g_nextFontHandle = 1;

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
        Font f = LoadFontEx(cpath, size, nullptr, 0);
        int h = g_nextFontHandle++;
        FontEntry e;
        e.font = f;
        e.refCount = 1;
        e.key = key;
        e.valid = (f.texture.id != 0); // heuristic: font has an atlas texture
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

    // ------------------- MUSIC (streaming) -------------------
    struct MusicEntry {
        Music       mus{};
        int         refCount = 0;
        std::string path;
        bool        valid = false;
        bool        playing = false;
    };

    static std::unordered_map<int, MusicEntry>      g_musByHandle;
    static std::unordered_map<std::string, int>     g_handleByMusPath;
    static int                                      g_nextMusicHandle = 1;

    int AcquireMusicH_Internal(const char* cpath) {
        std::string path = NormalizePath(cpath ? cpath : "");
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
        e.valid = (m.ctxData != nullptr); // raylib: valid if internal ctx exists
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

} // anonymous namespace

// ======= EXPORTED WRAPPERS =======
extern "C" {

    // --- Textures ---
    int  Framework_AcquireTextureH(const char* path) { return AcquireTextureH_Internal(path); }
    void Framework_ReleaseTextureH(int handle) { ReleaseTextureH_Internal(handle); }
    bool Framework_IsTextureValidH(int handle) { return GetTextureH_Internal(handle) != nullptr; }

    void Framework_DrawTextureH(int handle, int x, int y,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) {
            Color tint{ r, g, b, a };
            DrawTexture(*tex, x, y, tint);
        }
    }
    void Framework_DrawTextureVH(int handle, Vector2 pos,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) {
            Color tint{ r, g, b, a };
            DrawTextureV(*tex, pos, tint);
        }
    }
    void Framework_DrawTextureExH(int handle, Vector2 pos, float rotation, float scale,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) {
            Color tint{ r, g, b, a };
            DrawTextureEx(*tex, pos, rotation, scale, tint);
        }
    }
    void Framework_DrawTextureRecH(int handle, Rectangle src, Vector2 pos,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) {
            Color tint{ r, g, b, a };
            DrawTextureRec(*tex, src, pos, tint);
        }
    }
    void Framework_DrawTextureProH(int handle, Rectangle src, Rectangle dst, Vector2 origin, float rotation,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Texture2D* tex = GetTextureH_Internal(handle);
        if (tex) {
            Color tint{ r, g, b, a };
            DrawTexturePro(*tex, src, dst, origin, rotation, tint);
        }
    }

    // --- Fonts ---
    int  Framework_AcquireFontH(const char* path, int fontSize) { return AcquireFontH_Internal(path, fontSize); }
    void Framework_ReleaseFontH(int handle) { ReleaseFontH_Internal(handle); }
    bool Framework_IsFontValidH(int handle) { return GetFontH_Internal(handle) != nullptr; }

    void Framework_DrawTextExH(int handle, const char* text, Vector2 pos, float fontSize, float spacing,
        unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
        const Font* f = GetFontH_Internal(handle);
        if (f) {
            Color tint{ r, g, b, a };
            DrawTextEx(*f, text, pos, fontSize, spacing, tint);
        }
    }

    // --- Music (cache) ---
    int  Framework_AcquireMusicH(const char* path) { return AcquireMusicH_Internal(path); }
    void Framework_ReleaseMusicH(int handle) { ReleaseMusicH_Internal(handle); }
    bool Framework_IsMusicValidH(int handle) { return GetMusicH_Internal(handle) != nullptr; }

    void Framework_PlayMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) {
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
            g_musByHandle[handle].playing = false;
        }
    }
    void Framework_ResumeMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) {
            ResumeMusicStream(*m);
            g_musByHandle[handle].playing = true;
        }
    }

    void Framework_SetMusicVolumeH(int handle, float v) { Music* m = GetMusicH_Internal(handle); if (m) SetMusicVolume(*m, v); }
    void Framework_SetMusicPitchH(int handle, float p) { Music* m = GetMusicH_Internal(handle); if (m) SetMusicPitch(*m, p); }

    void Framework_UpdateMusicH(int handle) {
        Music* m = GetMusicH_Internal(handle);
        if (m) UpdateMusicStream(*m);
    }

    void Framework_UpdateAllMusic() {
        // Cache-based
        for (auto& kv : g_musByHandle) {
            if (kv.second.playing) UpdateMusicStream(kv.second.mus);
        }
       
        }
    

    // --- Unified resources shutdown ---
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
    }

} // extern "C"



    // ================================
//   Native Scene Manager (stack)
// ================================
#include <vector>
#include <unordered_map>

namespace {
    struct ScriptScene {
        SceneCallbacks cb{};
    };

    static std::unordered_map<int, ScriptScene> g_scenes;
    static std::vector<int>                     g_stack;
    static int                                  g_nextSceneHandle = 1;

    static inline ScriptScene* GetScene(int h) {
        auto it = g_scenes.find(h);
        return (it == g_scenes.end()) ? nullptr : &it->second;
    }

    static inline ScriptScene* TopScene() {
        if (g_stack.empty()) return nullptr;
        return GetScene(g_stack.back());
    }
}

extern "C" {

    // ---- Scene objects (script-driven) ----
    int Framework_CreateScriptScene(SceneCallbacks cb) {
        int h = g_nextSceneHandle++;
        g_scenes[h] = ScriptScene{ cb };
        return h;
    }

    void Framework_DestroyScene(int sceneHandle) {
        // If it’s on the stack, pop it first
        for (int i = (int)g_stack.size() - 1; i >= 0; --i) {
            if (g_stack[i] == sceneHandle) {
                // If top, call OnExit
                if (i == (int)g_stack.size() - 1) {
                    if (auto sc = GetScene(sceneHandle); sc && sc->cb.onExit) sc->cb.onExit();
                }
                g_stack.erase(g_stack.begin() + i);
            }
        }
        g_scenes.erase(sceneHandle);
    }

    // ---- Stack ops ----
    void Framework_SceneChange(int sceneHandle) {
        // Exit current top
        if (!g_stack.empty()) {
            if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
            g_stack.pop_back();
        }
        // Push new
        g_stack.push_back(sceneHandle);
        if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
    }

    void Framework_ScenePush(int sceneHandle) {
        g_stack.push_back(sceneHandle);
        if (auto sc = TopScene(); sc && sc->cb.onEnter) sc->cb.onEnter();
    }

    void Framework_ScenePop() {
        if (g_stack.empty()) return;
        // Exit current
        if (auto sc = TopScene(); sc && sc->cb.onExit) sc->cb.onExit();
        g_stack.pop_back();
        // Resume previous
        if (auto sc = TopScene(); sc && sc->cb.onResume) sc->cb.onResume();
    }

    bool Framework_SceneHas() { return !g_stack.empty(); }

    // ---- Per-frame tick ----
    // Uses your fixed-step helpers (Framework_StepFixed / Framework_GetFixedStep / Framework_GetFrameTime)
    void Framework_SceneTick() {
        auto sc = TopScene();
        if (!sc) return;

        // Fixed updates: run 0..N times this frame
        while (Framework_StepFixed()) {
            if (sc->cb.onUpdateFixed) sc->cb.onUpdateFixed(Framework_GetFixedStep());
        }

        // Variable per-frame update
        if (sc->cb.onUpdateFrame) sc->cb.onUpdateFrame((float)Framework_GetFrameTime());

        // Draw
        if (sc->cb.onDraw) sc->cb.onDraw();
    }

} // extern "C"
