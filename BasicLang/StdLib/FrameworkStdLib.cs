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

            // Map BasicLang function name to RaylibWrapper (FrameworkWrapper module) function
            return functionName switch
            {
                // Core - maps to FrameworkWrapper module from RaylibWrapper.dll
                "GameInit" => $"FrameworkWrapper.Framework_Initialize({args})",
                "GameShutdown" => "FrameworkWrapper.Framework_Shutdown()",
                "GameBeginFrame" => "FrameworkWrapper.Framework_BeginDrawing()",
                "GameEndFrame" => "FrameworkWrapper.Framework_EndDrawing()",
                "GameShouldClose" => "FrameworkWrapper.Framework_ShouldClose()",
                "GameGetDeltaTime" => "FrameworkWrapper.Framework_GetDeltaTime()",
                "GameGetFPS" => "FrameworkWrapper.Framework_GetFPS()",

                // Input
                "IsKeyPressed" => $"FrameworkWrapper.Framework_IsKeyPressed({args})",
                "IsKeyDown" => $"FrameworkWrapper.Framework_IsKeyDown({args})",
                "IsKeyReleased" => $"FrameworkWrapper.Framework_IsKeyReleased({args})",
                "IsMouseButtonPressed" => $"FrameworkWrapper.Framework_IsMouseButtonPressed({args})",
                "IsMouseButtonDown" => $"FrameworkWrapper.Framework_IsMouseButtonDown({args})",
                "GetMouseX" => "FrameworkWrapper.Framework_GetMouseX()",
                "GetMouseY" => "FrameworkWrapper.Framework_GetMouseY()",

                // Drawing - RaylibWrapper uses byte for color params
                "ClearBackground" => $"FrameworkWrapper.Framework_ClearBackground((byte)({GetArg(arguments, 0)}), (byte)({GetArg(arguments, 1)}), (byte)({GetArg(arguments, 2)}), 255)",
                "DrawRectangle" => $"FrameworkWrapper.Framework_DrawRectangle({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}))",
                "DrawCircle" => $"FrameworkWrapper.Framework_DrawCircle({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, (byte)({GetArg(arguments, 3)}), (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}))",
                "DrawLine" => $"FrameworkWrapper.Framework_DrawLine({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}))",
                "DrawText" => $"FrameworkWrapper.Framework_DrawText({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}))",

                // Textures
                "LoadTexture" => $"FrameworkWrapper.Framework_LoadTexture({args})",
                "UnloadTexture" => $"FrameworkWrapper.Framework_UnloadTexture({args})",
                "DrawTexture" => $"FrameworkWrapper.Framework_DrawTextureSimple({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, (byte)({GetArg(arguments, 3)}), (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}))",
                "DrawTextureEx" => $"FrameworkWrapper.Framework_DrawTextureEx({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, {GetArg(arguments, 4)}, (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}), (byte)({GetArg(arguments, 8)}))",

                // Entities
                "CreateEntity" => "FrameworkWrapper.Framework_Entity_Create()",
                "DestroyEntity" => $"FrameworkWrapper.Framework_Entity_Destroy({args})",
                "EntitySetPosition" => $"FrameworkWrapper.Framework_Entity_SetPosition({args})",
                "EntityGetX" => $"FrameworkWrapper.Framework_Entity_GetPositionX({args})",
                "EntityGetY" => $"FrameworkWrapper.Framework_Entity_GetPositionY({args})",
                "EntitySetVelocity" => $"FrameworkWrapper.Framework_Entity_SetVelocity({args})",
                "EntitySetSprite" => $"FrameworkWrapper.Framework_Entity_SetSprite({args})",
                "EntitySetCollider" => $"FrameworkWrapper.Framework_Entity_SetColliderBox({args})",
                "EntityIsActive" => $"FrameworkWrapper.Framework_Entity_IsActive({args})",
                "EntitySetActive" => $"FrameworkWrapper.Framework_Entity_SetActive({args})",

                // Audio
                "LoadSound" => $"FrameworkWrapper.Framework_LoadSound({args})",
                "PlaySound" => $"FrameworkWrapper.Framework_PlaySound({args})",
                "StopSound" => $"FrameworkWrapper.Framework_StopSound({args})",
                "SetSoundVolume" => $"FrameworkWrapper.Framework_SetSoundVolume({args})",
                "LoadMusic" => $"FrameworkWrapper.Framework_LoadMusic({args})",
                "PlayMusic" => $"FrameworkWrapper.Framework_PlayMusic({args})",
                "StopMusic" => $"FrameworkWrapper.Framework_StopMusic({args})",

                // Physics
                "PhysicsSetGravity" => $"FrameworkWrapper.Framework_Physics_SetGravity({args})",
                "PhysicsUpdate" => $"FrameworkWrapper.Framework_Physics_Update({args})",
                "CreatePhysicsBody" => $"FrameworkWrapper.Framework_Physics_CreateBody({args})",
                "PhysicsBodyApplyForce" => $"FrameworkWrapper.Framework_Physics_ApplyForce({args})",

                // UI
                "UICreateLabel" => $"FrameworkWrapper.Framework_UI_CreateLabel({args})",
                "UICreateButton" => $"FrameworkWrapper.Framework_UI_CreateButton({args})",
                "UIIsButtonClicked" => $"FrameworkWrapper.Framework_UI_IsClicked({args})",
                "UISetText" => $"FrameworkWrapper.Framework_UI_SetText({args})",
                "UIUpdate" => "FrameworkWrapper.Framework_UI_Update()",
                "UIRender" => "FrameworkWrapper.Framework_UI_Render()",

                // Camera
                "CameraSetPosition" => $"FrameworkWrapper.Framework_Camera_SetPosition({args})",
                "CameraSetZoom" => $"FrameworkWrapper.Framework_Camera_SetZoom({args})",
                "CameraFollow" => $"FrameworkWrapper.Framework_Camera_Follow({args})",
                "CameraShake" => $"FrameworkWrapper.Framework_Camera_Shake({args})",

                // Tweening
                "TweenFloat" => $"FrameworkWrapper.Framework_Tween_Float({args})",
                "TweenEntityPosition" => $"FrameworkWrapper.Framework_Tween_EntityPosition({args})",
                "TweenUpdate" => $"FrameworkWrapper.Framework_Tween_Update({args})",

                // Timer
                "TimerAfter" => $"FrameworkWrapper.Framework_Timer_After({args})",
                "TimerIsFinished" => $"FrameworkWrapper.Framework_Timer_IsFinished({args})",
                "TimerCancel" => $"FrameworkWrapper.Framework_Timer_Cancel({args})",
                "TimerUpdate" => $"FrameworkWrapper.Framework_Timer_Update({args})",

                _ => null
            };
        }

        private static string GetArg(string[] args, int index)
        {
            return index < args.Length ? args[index] : "0";
        }

        public IEnumerable<string> GetRequiredImports(string functionName)
        {
            yield return "System";
            yield return "System.Runtime.InteropServices";
            yield return "RaylibWrapper";  // For FrameworkWrapper module
        }

        public string GetInlineImplementation(string functionName)
        {
            // Return the FrameworkRuntime helper class that needs to be included
            return null;
        }

        /// <summary>
        /// Get the runtime helper class source code that needs to be included in generated code.
        /// Since we use RaylibWrapper.dll, we only need key and easing constants.
        /// </summary>
        public static string GetRuntimeHelperCode() => @"
// Key codes for BasicLang game development
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
