using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BasicLang.Compiler.StdLib.Framework
{
    /// <summary>
    /// VisualGameStudioEngine Framework bindings for BasicLang
    /// Allows BasicLang programs to use the game engine directly
    /// </summary>
    public class FrameworkStdLibProvider : IStdLibProvider
    {
        private static readonly Dictionary<string, StdLibFunction> _functions = new Dictionary<string, StdLibFunction>(StringComparer.OrdinalIgnoreCase)
        {
            // ==================== CORE ====================
            ["GameInit"] = new StdLibFunction { Name = "GameInit", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "String" }, ReturnType = "Void" },
            ["GameShutdown"] = new StdLibFunction { Name = "GameShutdown", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["GameBeginFrame"] = new StdLibFunction { Name = "GameBeginFrame", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["GameEndFrame"] = new StdLibFunction { Name = "GameEndFrame", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["GameShouldClose"] = new StdLibFunction { Name = "GameShouldClose", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Boolean" },
            ["GameGetDeltaTime"] = new StdLibFunction { Name = "GameGetDeltaTime", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },
            ["GameGetFPS"] = new StdLibFunction { Name = "GameGetFPS", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Integer" },

            // ==================== INPUT ====================
            ["IsKeyPressed"] = new StdLibFunction { Name = "IsKeyPressed", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["IsKeyDown"] = new StdLibFunction { Name = "IsKeyDown", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["IsKeyReleased"] = new StdLibFunction { Name = "IsKeyReleased", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["IsMouseButtonPressed"] = new StdLibFunction { Name = "IsMouseButtonPressed", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["IsMouseButtonDown"] = new StdLibFunction { Name = "IsMouseButtonDown", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["GetMouseX"] = new StdLibFunction { Name = "GetMouseX", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Integer" },
            ["GetMouseY"] = new StdLibFunction { Name = "GetMouseY", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Integer" },

            // ==================== DRAWING ====================
            ["ClearBackground"] = new StdLibFunction { Name = "ClearBackground", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawRectangle"] = new StdLibFunction { Name = "DrawRectangle", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawCircle"] = new StdLibFunction { Name = "DrawCircle", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawLine"] = new StdLibFunction { Name = "DrawLine", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawText"] = new StdLibFunction { Name = "DrawText", Category = StdLibCategory.System, ParameterTypes = new[] { "String", "Single", "Single", "Integer", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },

            // ==================== TEXTURES ====================
            ["LoadTexture"] = new StdLibFunction { Name = "LoadTexture", Category = StdLibCategory.System, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["UnloadTexture"] = new StdLibFunction { Name = "UnloadTexture", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["DrawTexture"] = new StdLibFunction { Name = "DrawTexture", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawTextureEx"] = new StdLibFunction { Name = "DrawTextureEx", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single", "Single", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },

            // ==================== ENTITIES ====================
            ["CreateEntity"] = new StdLibFunction { Name = "CreateEntity", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Integer" },
            ["DestroyEntity"] = new StdLibFunction { Name = "DestroyEntity", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["EntitySetPosition"] = new StdLibFunction { Name = "EntitySetPosition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single" }, ReturnType = "Void" },
            ["EntityGetX"] = new StdLibFunction { Name = "EntityGetX", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Single" },
            ["EntityGetY"] = new StdLibFunction { Name = "EntityGetY", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Single" },
            ["EntitySetVelocity"] = new StdLibFunction { Name = "EntitySetVelocity", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single" }, ReturnType = "Void" },
            ["EntitySetSprite"] = new StdLibFunction { Name = "EntitySetSprite", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },
            ["EntitySetCollider"] = new StdLibFunction { Name = "EntitySetCollider", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single" }, ReturnType = "Void" },
            ["EntityIsActive"] = new StdLibFunction { Name = "EntityIsActive", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["EntitySetActive"] = new StdLibFunction { Name = "EntitySetActive", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Boolean" }, ReturnType = "Void" },

            // ==================== AUDIO ====================
            ["LoadSound"] = new StdLibFunction { Name = "LoadSound", Category = StdLibCategory.System, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["PlaySound"] = new StdLibFunction { Name = "PlaySound", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["StopSound"] = new StdLibFunction { Name = "StopSound", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["SetSoundVolume"] = new StdLibFunction { Name = "SetSoundVolume", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single" }, ReturnType = "Void" },
            ["LoadMusic"] = new StdLibFunction { Name = "LoadMusic", Category = StdLibCategory.System, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["PlayMusic"] = new StdLibFunction { Name = "PlayMusic", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["StopMusic"] = new StdLibFunction { Name = "StopMusic", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },

            // ==================== PHYSICS ====================
            ["PhysicsSetGravity"] = new StdLibFunction { Name = "PhysicsSetGravity", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },
            ["PhysicsUpdate"] = new StdLibFunction { Name = "PhysicsUpdate", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["CreatePhysicsBody"] = new StdLibFunction { Name = "CreatePhysicsBody", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Integer" },
            ["PhysicsBodyApplyForce"] = new StdLibFunction { Name = "PhysicsBodyApplyForce", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single" }, ReturnType = "Void" },

            // ==================== UI ====================
            ["UICreateLabel"] = new StdLibFunction { Name = "UICreateLabel", Category = StdLibCategory.System, ParameterTypes = new[] { "String", "Single", "Single" }, ReturnType = "Integer" },
            ["UICreateButton"] = new StdLibFunction { Name = "UICreateButton", Category = StdLibCategory.System, ParameterTypes = new[] { "String", "Single", "Single", "Single", "Single" }, ReturnType = "Integer" },
            ["UIIsButtonClicked"] = new StdLibFunction { Name = "UIIsButtonClicked", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["UISetText"] = new StdLibFunction { Name = "UISetText", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Void" },
            ["UIUpdate"] = new StdLibFunction { Name = "UIUpdate", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["UIRender"] = new StdLibFunction { Name = "UIRender", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },

            // ==================== CAMERA ====================
            ["CameraSetPosition"] = new StdLibFunction { Name = "CameraSetPosition", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },
            ["CameraSetZoom"] = new StdLibFunction { Name = "CameraSetZoom", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["CameraFollow"] = new StdLibFunction { Name = "CameraFollow", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single" }, ReturnType = "Void" },
            ["CameraShake"] = new StdLibFunction { Name = "CameraShake", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },

            // ==================== TWEENING ====================
            ["TweenFloat"] = new StdLibFunction { Name = "TweenFloat", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single", "Integer" }, ReturnType = "Integer" },
            ["TweenEntityPosition"] = new StdLibFunction { Name = "TweenEntityPosition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single", "Single", "Single", "Integer" }, ReturnType = "Integer" },
            ["TweenUpdate"] = new StdLibFunction { Name = "TweenUpdate", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },

            // ==================== TIMER ====================
            ["TimerAfter"] = new StdLibFunction { Name = "TimerAfter", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Integer" },
            ["TimerIsFinished"] = new StdLibFunction { Name = "TimerIsFinished", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["TimerCancel"] = new StdLibFunction { Name = "TimerCancel", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["TimerUpdate"] = new StdLibFunction { Name = "TimerUpdate", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
        };

        public bool CanHandle(string functionName) => _functions.ContainsKey(functionName);

        public string EmitCall(string functionName, string[] arguments)
        {
            if (!_functions.TryGetValue(functionName, out var func))
                return null;

            var args = string.Join(", ", arguments);

            // Map BasicLang function name to Framework DLL function
            return functionName switch
            {
                // Core
                "GameInit" => $"FrameworkRuntime.Initialize({args})",
                "GameShutdown" => "FrameworkRuntime.Shutdown()",
                "GameBeginFrame" => "FrameworkRuntime.BeginFrame()",
                "GameEndFrame" => "FrameworkRuntime.EndFrame()",
                "GameShouldClose" => "FrameworkRuntime.ShouldClose()",
                "GameGetDeltaTime" => "FrameworkRuntime.GetDeltaTime()",
                "GameGetFPS" => "FrameworkRuntime.GetFPS()",

                // Input
                "IsKeyPressed" => $"FrameworkRuntime.IsKeyPressed({args})",
                "IsKeyDown" => $"FrameworkRuntime.IsKeyDown({args})",
                "IsKeyReleased" => $"FrameworkRuntime.IsKeyReleased({args})",
                "IsMouseButtonPressed" => $"FrameworkRuntime.IsMouseButtonPressed({args})",
                "IsMouseButtonDown" => $"FrameworkRuntime.IsMouseButtonDown({args})",
                "GetMouseX" => "FrameworkRuntime.GetMouseX()",
                "GetMouseY" => "FrameworkRuntime.GetMouseY()",

                // Drawing
                "ClearBackground" => $"FrameworkRuntime.ClearBackground({args})",
                "DrawRectangle" => $"FrameworkRuntime.DrawRectangle({args})",
                "DrawCircle" => $"FrameworkRuntime.DrawCircle({args})",
                "DrawLine" => $"FrameworkRuntime.DrawLine({args})",
                "DrawText" => $"FrameworkRuntime.DrawText({args})",

                // Textures
                "LoadTexture" => $"FrameworkRuntime.LoadTexture({args})",
                "UnloadTexture" => $"FrameworkRuntime.UnloadTexture({args})",
                "DrawTexture" => $"FrameworkRuntime.DrawTexture({args})",
                "DrawTextureEx" => $"FrameworkRuntime.DrawTextureEx({args})",

                // Entities
                "CreateEntity" => "FrameworkRuntime.CreateEntity()",
                "DestroyEntity" => $"FrameworkRuntime.DestroyEntity({args})",
                "EntitySetPosition" => $"FrameworkRuntime.EntitySetPosition({args})",
                "EntityGetX" => $"FrameworkRuntime.EntityGetX({args})",
                "EntityGetY" => $"FrameworkRuntime.EntityGetY({args})",
                "EntitySetVelocity" => $"FrameworkRuntime.EntitySetVelocity({args})",
                "EntitySetSprite" => $"FrameworkRuntime.EntitySetSprite({args})",
                "EntitySetCollider" => $"FrameworkRuntime.EntitySetCollider({args})",
                "EntityIsActive" => $"FrameworkRuntime.EntityIsActive({args})",
                "EntitySetActive" => $"FrameworkRuntime.EntitySetActive({args})",

                // Audio
                "LoadSound" => $"FrameworkRuntime.LoadSound({args})",
                "PlaySound" => $"FrameworkRuntime.PlaySound({args})",
                "StopSound" => $"FrameworkRuntime.StopSound({args})",
                "SetSoundVolume" => $"FrameworkRuntime.SetSoundVolume({args})",
                "LoadMusic" => $"FrameworkRuntime.LoadMusic({args})",
                "PlayMusic" => $"FrameworkRuntime.PlayMusic({args})",
                "StopMusic" => $"FrameworkRuntime.StopMusic({args})",

                // Physics
                "PhysicsSetGravity" => $"FrameworkRuntime.PhysicsSetGravity({args})",
                "PhysicsUpdate" => $"FrameworkRuntime.PhysicsUpdate({args})",
                "CreatePhysicsBody" => $"FrameworkRuntime.CreatePhysicsBody({args})",
                "PhysicsBodyApplyForce" => $"FrameworkRuntime.PhysicsBodyApplyForce({args})",

                // UI
                "UICreateLabel" => $"FrameworkRuntime.UICreateLabel({args})",
                "UICreateButton" => $"FrameworkRuntime.UICreateButton({args})",
                "UIIsButtonClicked" => $"FrameworkRuntime.UIIsButtonClicked({args})",
                "UISetText" => $"FrameworkRuntime.UISetText({args})",
                "UIUpdate" => "FrameworkRuntime.UIUpdate()",
                "UIRender" => "FrameworkRuntime.UIRender()",

                // Camera
                "CameraSetPosition" => $"FrameworkRuntime.CameraSetPosition({args})",
                "CameraSetZoom" => $"FrameworkRuntime.CameraSetZoom({args})",
                "CameraFollow" => $"FrameworkRuntime.CameraFollow({args})",
                "CameraShake" => $"FrameworkRuntime.CameraShake({args})",

                // Tweening
                "TweenFloat" => $"FrameworkRuntime.TweenFloat({args})",
                "TweenEntityPosition" => $"FrameworkRuntime.TweenEntityPosition({args})",
                "TweenUpdate" => $"FrameworkRuntime.TweenUpdate({args})",

                // Timer
                "TimerAfter" => $"FrameworkRuntime.TimerAfter({args})",
                "TimerIsFinished" => $"FrameworkRuntime.TimerIsFinished({args})",
                "TimerCancel" => $"FrameworkRuntime.TimerCancel({args})",
                "TimerUpdate" => $"FrameworkRuntime.TimerUpdate({args})",

                _ => null
            };
        }

        public IEnumerable<string> GetRequiredImports(string functionName)
        {
            yield return "System";
            yield return "System.Runtime.InteropServices";
        }

        public string GetInlineImplementation(string functionName)
        {
            // Return the FrameworkRuntime helper class that needs to be included
            return null;
        }

        /// <summary>
        /// Get the runtime helper class source code that needs to be included in generated code
        /// </summary>
        public static string GetRuntimeHelperCode() => @"
/// <summary>
/// Runtime helper for VisualGameStudioEngine Framework P/Invoke calls
/// </summary>
public static class FrameworkRuntime
{
    private const string DllName = ""VisualGameStudioEngine.dll"";

    // Core
    [DllImport(DllName)] public static extern void Framework_Initialize(int width, int height, [MarshalAs(UnmanagedType.LPStr)] string title);
    [DllImport(DllName)] public static extern void Framework_Shutdown();
    [DllImport(DllName)] public static extern void Framework_BeginFrame();
    [DllImport(DllName)] public static extern void Framework_EndFrame();
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_WindowShouldClose();
    [DllImport(DllName)] public static extern float Framework_GetDeltaTime();
    [DllImport(DllName)] public static extern int Framework_GetFPS();

    // Input
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_IsKeyPressed(int key);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_IsKeyDown(int key);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_IsKeyReleased(int key);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_IsMouseButtonPressed(int button);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_IsMouseButtonDown(int button);
    [DllImport(DllName)] public static extern int Framework_GetMouseX();
    [DllImport(DllName)] public static extern int Framework_GetMouseY();

    // Drawing
    [DllImport(DllName)] public static extern void Framework_ClearBackground(byte r, byte g, byte b, byte a);
    [DllImport(DllName)] public static extern void Framework_DrawRectangle(float x, float y, float w, float h, byte r, byte g, byte b, byte a);
    [DllImport(DllName)] public static extern void Framework_DrawCircle(float x, float y, float radius, byte r, byte g, byte b, byte a);
    [DllImport(DllName)] public static extern void Framework_DrawLine(float x1, float y1, float x2, float y2, byte r, byte g, byte b, byte a);
    [DllImport(DllName)] public static extern void Framework_DrawText([MarshalAs(UnmanagedType.LPStr)] string text, float x, float y, int fontSize, byte r, byte g, byte b, byte a);

    // Textures
    [DllImport(DllName)] public static extern int Framework_LoadTexture([MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport(DllName)] public static extern void Framework_UnloadTexture(int handle);
    [DllImport(DllName)] public static extern void Framework_DrawTextureSimple(int handle, float x, float y, byte r, byte g, byte b, byte a);
    [DllImport(DllName)] public static extern void Framework_DrawTextureEx(int handle, float x, float y, float rotation, float scale, byte r, byte g, byte b, byte a);

    // Entities
    [DllImport(DllName)] public static extern int Framework_Entity_Create();
    [DllImport(DllName)] public static extern void Framework_Entity_Destroy(int entity);
    [DllImport(DllName)] public static extern void Framework_Entity_SetPosition(int entity, float x, float y);
    [DllImport(DllName)] public static extern float Framework_Entity_GetPositionX(int entity);
    [DllImport(DllName)] public static extern float Framework_Entity_GetPositionY(int entity);
    [DllImport(DllName)] public static extern void Framework_Entity_SetVelocity(int entity, float vx, float vy);
    [DllImport(DllName)] public static extern void Framework_Entity_SetSprite(int entity, int textureHandle);
    [DllImport(DllName)] public static extern void Framework_Entity_SetColliderBox(int entity, float w, float h);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_Entity_IsActive(int entity);
    [DllImport(DllName)] public static extern void Framework_Entity_SetActive(int entity, [MarshalAs(UnmanagedType.I1)] bool active);

    // Audio
    [DllImport(DllName)] public static extern int Framework_LoadSound([MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport(DllName)] public static extern void Framework_PlaySound(int handle);
    [DllImport(DllName)] public static extern void Framework_StopSound(int handle);
    [DllImport(DllName)] public static extern void Framework_SetSoundVolume(int handle, float volume);
    [DllImport(DllName)] public static extern int Framework_LoadMusic([MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport(DllName)] public static extern void Framework_PlayMusic(int handle);
    [DllImport(DllName)] public static extern void Framework_StopMusic(int handle);

    // Physics
    [DllImport(DllName)] public static extern void Framework_Physics_SetGravity(float x, float y);
    [DllImport(DllName)] public static extern void Framework_Physics_Update(float deltaTime);
    [DllImport(DllName)] public static extern int Framework_Physics_CreateBody(int entity, int bodyType);
    [DllImport(DllName)] public static extern void Framework_Physics_ApplyForce(int bodyId, float fx, float fy);

    // UI
    [DllImport(DllName)] public static extern int Framework_UI_CreateLabel([MarshalAs(UnmanagedType.LPStr)] string text, float x, float y);
    [DllImport(DllName)] public static extern int Framework_UI_CreateButton([MarshalAs(UnmanagedType.LPStr)] string text, float x, float y, float w, float h);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_UI_IsClicked(int elementId);
    [DllImport(DllName)] public static extern void Framework_UI_SetText(int elementId, [MarshalAs(UnmanagedType.LPStr)] string text);
    [DllImport(DllName)] public static extern void Framework_UI_Update();
    [DllImport(DllName)] public static extern void Framework_UI_Render();

    // Camera
    [DllImport(DllName)] public static extern void Framework_Camera_SetPosition(float x, float y);
    [DllImport(DllName)] public static extern void Framework_Camera_SetZoom(float zoom);
    [DllImport(DllName)] public static extern void Framework_Camera_Follow(int entity, float smoothing);
    [DllImport(DllName)] public static extern void Framework_Camera_Shake(float intensity, float duration);

    // Tweening
    [DllImport(DllName)] public static extern int Framework_Tween_Float(float from, float to, float duration, int easing);
    [DllImport(DllName)] public static extern int Framework_Tween_EntityPosition(int entity, float toX, float toY, float duration, int easing);
    [DllImport(DllName)] public static extern void Framework_Tween_Update(float deltaTime);

    // Timer
    [DllImport(DllName)] public static extern int Framework_Timer_After(float delay);
    [DllImport(DllName)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool Framework_Timer_IsFinished(int timerId);
    [DllImport(DllName)] public static extern void Framework_Timer_Cancel(int timerId);
    [DllImport(DllName)] public static extern void Framework_Timer_Update(float deltaTime);

    // Wrapper methods for BasicLang
    public static void Initialize(int w, int h, string title) => Framework_Initialize(w, h, title);
    public static void Shutdown() => Framework_Shutdown();
    public static void BeginFrame() => Framework_BeginFrame();
    public static void EndFrame() => Framework_EndFrame();
    public static bool ShouldClose() => Framework_WindowShouldClose();
    public static float GetDeltaTime() => Framework_GetDeltaTime();
    public static int GetFPS() => Framework_GetFPS();

    public static bool IsKeyPressed(int key) => Framework_IsKeyPressed(key);
    public static bool IsKeyDown(int key) => Framework_IsKeyDown(key);
    public static bool IsKeyReleased(int key) => Framework_IsKeyReleased(key);
    public static bool IsMouseButtonPressed(int btn) => Framework_IsMouseButtonPressed(btn);
    public static bool IsMouseButtonDown(int btn) => Framework_IsMouseButtonDown(btn);
    public static int GetMouseX() => Framework_GetMouseX();
    public static int GetMouseY() => Framework_GetMouseY();

    public static void ClearBackground(int r, int g, int b) => Framework_ClearBackground((byte)r, (byte)g, (byte)b, 255);
    public static void DrawRectangle(float x, float y, float w, float h, int r, int g, int b, int a) => Framework_DrawRectangle(x, y, w, h, (byte)r, (byte)g, (byte)b, (byte)a);
    public static void DrawCircle(float x, float y, float radius, int r, int g, int b, int a) => Framework_DrawCircle(x, y, radius, (byte)r, (byte)g, (byte)b, (byte)a);
    public static void DrawLine(float x1, float y1, float x2, float y2, int r, int g, int b, int a) => Framework_DrawLine(x1, y1, x2, y2, (byte)r, (byte)g, (byte)b, (byte)a);
    public static void DrawText(string text, float x, float y, int size, int r, int g, int b, int a) => Framework_DrawText(text, x, y, size, (byte)r, (byte)g, (byte)b, (byte)a);

    public static int LoadTexture(string path) => Framework_LoadTexture(path);
    public static void UnloadTexture(int h) => Framework_UnloadTexture(h);
    public static void DrawTexture(int h, float x, float y, int r, int g, int b, int a) => Framework_DrawTextureSimple(h, x, y, (byte)r, (byte)g, (byte)b, (byte)a);
    public static void DrawTextureEx(int h, float x, float y, float rot, float scale, int r, int g, int b, int a) => Framework_DrawTextureEx(h, x, y, rot, scale, (byte)r, (byte)g, (byte)b, (byte)a);

    public static int CreateEntity() => Framework_Entity_Create();
    public static void DestroyEntity(int e) => Framework_Entity_Destroy(e);
    public static void EntitySetPosition(int e, float x, float y) => Framework_Entity_SetPosition(e, x, y);
    public static float EntityGetX(int e) => Framework_Entity_GetPositionX(e);
    public static float EntityGetY(int e) => Framework_Entity_GetPositionY(e);
    public static void EntitySetVelocity(int e, float vx, float vy) => Framework_Entity_SetVelocity(e, vx, vy);
    public static void EntitySetSprite(int e, int tex) => Framework_Entity_SetSprite(e, tex);
    public static void EntitySetCollider(int e, float w, float h) => Framework_Entity_SetColliderBox(e, w, h);
    public static bool EntityIsActive(int e) => Framework_Entity_IsActive(e);
    public static void EntitySetActive(int e, bool a) => Framework_Entity_SetActive(e, a);

    public static int LoadSound(string p) => Framework_LoadSound(p);
    public static void PlaySound(int h) => Framework_PlaySound(h);
    public static void StopSound(int h) => Framework_StopSound(h);
    public static void SetSoundVolume(int h, float v) => Framework_SetSoundVolume(h, v);
    public static int LoadMusic(string p) => Framework_LoadMusic(p);
    public static void PlayMusic(int h) => Framework_PlayMusic(h);
    public static void StopMusic(int h) => Framework_StopMusic(h);

    public static void PhysicsSetGravity(float x, float y) => Framework_Physics_SetGravity(x, y);
    public static void PhysicsUpdate(float dt) => Framework_Physics_Update(dt);
    public static int CreatePhysicsBody(int e, int type) => Framework_Physics_CreateBody(e, type);
    public static void PhysicsBodyApplyForce(int b, float fx, float fy) => Framework_Physics_ApplyForce(b, fx, fy);

    public static int UICreateLabel(string t, float x, float y) => Framework_UI_CreateLabel(t, x, y);
    public static int UICreateButton(string t, float x, float y, float w, float h) => Framework_UI_CreateButton(t, x, y, w, h);
    public static bool UIIsButtonClicked(int e) => Framework_UI_IsClicked(e);
    public static void UISetText(int e, string t) => Framework_UI_SetText(e, t);
    public static void UIUpdate() => Framework_UI_Update();
    public static void UIRender() => Framework_UI_Render();

    public static void CameraSetPosition(float x, float y) => Framework_Camera_SetPosition(x, y);
    public static void CameraSetZoom(float z) => Framework_Camera_SetZoom(z);
    public static void CameraFollow(int e, float s) => Framework_Camera_Follow(e, s);
    public static void CameraShake(float i, float d) => Framework_Camera_Shake(i, d);

    public static int TweenFloat(float from, float to, float dur, int ease) => Framework_Tween_Float(from, to, dur, ease);
    public static int TweenEntityPosition(int e, float tx, float ty, float dur, int ease) => Framework_Tween_EntityPosition(e, tx, ty, dur, ease);
    public static void TweenUpdate(float dt) => Framework_Tween_Update(dt);

    public static int TimerAfter(float delay) => Framework_Timer_After(delay);
    public static bool TimerIsFinished(int t) => Framework_Timer_IsFinished(t);
    public static void TimerCancel(int t) => Framework_Timer_Cancel(t);
    public static void TimerUpdate(float dt) => Framework_Timer_Update(dt);
}

// Key codes for BasicLang
public static class Keys
{
    public const int Space = 32, Escape = 256, Enter = 257, Tab = 258, Backspace = 259;
    public const int Right = 262, Left = 263, Down = 264, Up = 265;
    public const int A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74;
    public const int K = 75, L = 76, M = 77, N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84;
    public const int U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90;
    public const int Num0 = 48, Num1 = 49, Num2 = 50, Num3 = 51, Num4 = 52;
    public const int Num5 = 53, Num6 = 54, Num7 = 55, Num8 = 56, Num9 = 57;
}

// Easing types for tweening
public static class Easing
{
    public const int Linear = 0, QuadIn = 1, QuadOut = 2, QuadInOut = 3;
    public const int CubicIn = 4, CubicOut = 5, CubicInOut = 6;
    public const int SineIn = 7, SineOut = 8, SineInOut = 9;
    public const int ExpoIn = 10, ExpoOut = 11, ExpoInOut = 12;
    public const int BounceOut = 13, ElasticOut = 14;
}

// Physics body types
public static class BodyType
{
    public const int Static = 0, Dynamic = 1, Kinematic = 2;
}
";
    }
}
