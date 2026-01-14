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

            // ==================== EXTENDED DRAWING (available in wrapper) ====================
            ["DrawRectangleLines"] = new StdLibFunction { Name = "DrawRectangleLines", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawCircleLines"] = new StdLibFunction { Name = "DrawCircleLines", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Single", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawTriangle"] = new StdLibFunction { Name = "DrawTriangle", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawTriangleLines"] = new StdLibFunction { Name = "DrawTriangleLines", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer", "Integer" }, ReturnType = "Void" },
            ["DrawFPS"] = new StdLibFunction { Name = "DrawFPS", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },

            // ==================== TIME ====================
            ["SetTargetFPS"] = new StdLibFunction { Name = "SetTargetFPS", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["GetFrameTime"] = new StdLibFunction { Name = "GetFrameTime", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },
            ["GetTime"] = new StdLibFunction { Name = "GetTime", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Double" },
            ["SetTimeScale"] = new StdLibFunction { Name = "SetTimeScale", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["GetTimeScale"] = new StdLibFunction { Name = "GetTimeScale", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },

            // ==================== CURSOR ====================
            ["ShowCursor"] = new StdLibFunction { Name = "ShowCursor", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["HideCursor"] = new StdLibFunction { Name = "HideCursor", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["IsCursorHidden"] = new StdLibFunction { Name = "IsCursorHidden", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Boolean" },
            ["SetMousePosition"] = new StdLibFunction { Name = "SetMousePosition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },
            ["GetMouseWheelMove"] = new StdLibFunction { Name = "GetMouseWheelMove", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },

            // ==================== FONTS (handle-based) ====================
            ["LoadFont"] = new StdLibFunction { Name = "LoadFont", Category = StdLibCategory.System, ParameterTypes = new[] { "String", "Integer" }, ReturnType = "Integer" },
            ["UnloadFont"] = new StdLibFunction { Name = "UnloadFont", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },

            // ==================== CAMERA ====================
            ["CameraBeginMode"] = new StdLibFunction { Name = "CameraBeginMode", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["CameraEndMode"] = new StdLibFunction { Name = "CameraEndMode", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["CameraGetZoom"] = new StdLibFunction { Name = "CameraGetZoom", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },
            ["CameraGetRotation"] = new StdLibFunction { Name = "CameraGetRotation", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Single" },
            ["CameraSetRotation"] = new StdLibFunction { Name = "CameraSetRotation", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["CameraSetTarget"] = new StdLibFunction { Name = "CameraSetTarget", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },
            ["CameraSetOffset"] = new StdLibFunction { Name = "CameraSetOffset", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },
            ["CameraUpdate"] = new StdLibFunction { Name = "CameraUpdate", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["CameraReset"] = new StdLibFunction { Name = "CameraReset", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["CameraPanTo"] = new StdLibFunction { Name = "CameraPanTo", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single" }, ReturnType = "Void" },
            ["CameraZoomTo"] = new StdLibFunction { Name = "CameraZoomTo", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single" }, ReturnType = "Void" },
            ["CameraSetBounds"] = new StdLibFunction { Name = "CameraSetBounds", Category = StdLibCategory.System, ParameterTypes = new[] { "Single", "Single", "Single", "Single" }, ReturnType = "Void" },
            ["CameraSetBoundsEnabled"] = new StdLibFunction { Name = "CameraSetBoundsEnabled", Category = StdLibCategory.System, ParameterTypes = new[] { "Boolean" }, ReturnType = "Void" },
            ["CameraSetFollowLerp"] = new StdLibFunction { Name = "CameraSetFollowLerp", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },

            // ==================== PARTICLES (available in wrapper) ====================
            ["ParticlesUpdate"] = new StdLibFunction { Name = "ParticlesUpdate", Category = StdLibCategory.System, ParameterTypes = new[] { "Single" }, ReturnType = "Void" },
            ["ParticlesDraw"] = new StdLibFunction { Name = "ParticlesDraw", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },

            // ==================== TILEMAPS (available in wrapper) ====================
            ["TilemapsDraw"] = new StdLibFunction { Name = "TilemapsDraw", Category = StdLibCategory.System, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },

            // ==================== ANIMATION CONTROLLER ====================
            // Controller management
            ["AnimCtrlCreate"] = new StdLibFunction { Name = "AnimCtrlCreate", Category = StdLibCategory.System, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["AnimCtrlDestroy"] = new StdLibFunction { Name = "AnimCtrlDestroy", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["AnimCtrlGet"] = new StdLibFunction { Name = "AnimCtrlGet", Category = StdLibCategory.System, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["AnimCtrlIsValid"] = new StdLibFunction { Name = "AnimCtrlIsValid", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },

            // State management
            ["AnimCtrlAddState"] = new StdLibFunction { Name = "AnimCtrlAddState", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Integer" }, ReturnType = "Integer" },
            ["AnimCtrlAddBlendState1D"] = new StdLibFunction { Name = "AnimCtrlAddBlendState1D", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "String" }, ReturnType = "Integer" },
            ["AnimCtrlGetState"] = new StdLibFunction { Name = "AnimCtrlGetState", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Integer" },
            ["AnimCtrlSetDefaultState"] = new StdLibFunction { Name = "AnimCtrlSetDefaultState", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },
            ["AnimCtrlSetStateSpeed"] = new StdLibFunction { Name = "AnimCtrlSetStateSpeed", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Single" }, ReturnType = "Void" },
            ["AnimCtrlSetStateLoop"] = new StdLibFunction { Name = "AnimCtrlSetStateLoop", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Boolean" }, ReturnType = "Void" },

            // Blend clips
            ["AnimCtrlAddBlendClip"] = new StdLibFunction { Name = "AnimCtrlAddBlendClip", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer", "Single" }, ReturnType = "Void" },

            // Transitions
            ["AnimCtrlAddTransition"] = new StdLibFunction { Name = "AnimCtrlAddTransition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Integer", "Single" }, ReturnType = "Integer" },
            ["AnimCtrlAddAnyStateTransition"] = new StdLibFunction { Name = "AnimCtrlAddAnyStateTransition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Single" }, ReturnType = "Integer" },
            ["AnimCtrlAddCondition"] = new StdLibFunction { Name = "AnimCtrlAddCondition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "String", "Integer", "Single" }, ReturnType = "Void" },
            ["AnimCtrlAddBoolCondition"] = new StdLibFunction { Name = "AnimCtrlAddBoolCondition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "String", "Boolean" }, ReturnType = "Void" },
            ["AnimCtrlAddTriggerCondition"] = new StdLibFunction { Name = "AnimCtrlAddTriggerCondition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "String" }, ReturnType = "Void" },
            ["AnimCtrlSetExitTime"] = new StdLibFunction { Name = "AnimCtrlSetExitTime", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Boolean", "Single" }, ReturnType = "Void" },

            // Parameters
            ["AnimCtrlSetFloat"] = new StdLibFunction { Name = "AnimCtrlSetFloat", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Single" }, ReturnType = "Void" },
            ["AnimCtrlGetFloat"] = new StdLibFunction { Name = "AnimCtrlGetFloat", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Single" }, ReturnType = "Single" },
            ["AnimCtrlSetInt"] = new StdLibFunction { Name = "AnimCtrlSetInt", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Integer" }, ReturnType = "Void" },
            ["AnimCtrlGetInt"] = new StdLibFunction { Name = "AnimCtrlGetInt", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Integer" }, ReturnType = "Integer" },
            ["AnimCtrlSetBool"] = new StdLibFunction { Name = "AnimCtrlSetBool", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Boolean" }, ReturnType = "Void" },
            ["AnimCtrlGetBool"] = new StdLibFunction { Name = "AnimCtrlGetBool", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Boolean" }, ReturnType = "Boolean" },
            ["AnimCtrlSetTrigger"] = new StdLibFunction { Name = "AnimCtrlSetTrigger", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Void" },
            ["AnimCtrlResetTrigger"] = new StdLibFunction { Name = "AnimCtrlResetTrigger", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Void" },

            // Instance management
            ["AnimCtrlCreateInstance"] = new StdLibFunction { Name = "AnimCtrlCreateInstance", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Integer" },
            ["AnimCtrlDestroyInstance"] = new StdLibFunction { Name = "AnimCtrlDestroyInstance", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["AnimCtrlUpdateInstance"] = new StdLibFunction { Name = "AnimCtrlUpdateInstance", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single" }, ReturnType = "Void" },
            ["AnimCtrlGetCurrentState"] = new StdLibFunction { Name = "AnimCtrlGetCurrentState", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Integer" },
            ["AnimCtrlIsInTransition"] = new StdLibFunction { Name = "AnimCtrlIsInTransition", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },

            // Instance parameters
            ["AnimCtrlInstanceSetFloat"] = new StdLibFunction { Name = "AnimCtrlInstanceSetFloat", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Single" }, ReturnType = "Void" },
            ["AnimCtrlInstanceSetInt"] = new StdLibFunction { Name = "AnimCtrlInstanceSetInt", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Integer" }, ReturnType = "Void" },
            ["AnimCtrlInstanceSetBool"] = new StdLibFunction { Name = "AnimCtrlInstanceSetBool", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String", "Boolean" }, ReturnType = "Void" },
            ["AnimCtrlInstanceSetTrigger"] = new StdLibFunction { Name = "AnimCtrlInstanceSetTrigger", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Void" },

            // Playback control
            ["AnimCtrlForceState"] = new StdLibFunction { Name = "AnimCtrlForceState", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },
            ["AnimCtrlCrossFade"] = new StdLibFunction { Name = "AnimCtrlCrossFade", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Integer", "Single" }, ReturnType = "Void" },
            ["AnimCtrlPause"] = new StdLibFunction { Name = "AnimCtrlPause", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["AnimCtrlResume"] = new StdLibFunction { Name = "AnimCtrlResume", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["AnimCtrlSetSpeed"] = new StdLibFunction { Name = "AnimCtrlSetSpeed", Category = StdLibCategory.System, ParameterTypes = new[] { "Integer", "Single" }, ReturnType = "Void" },
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

                // Extended Drawing (matches wrapper)
                "DrawRectangleLines" => $"FrameworkWrapper.Framework_DrawRectangleLines({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}))",
                "DrawCircleLines" => $"FrameworkWrapper.Framework_DrawCircleLines({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, (byte)({GetArg(arguments, 3)}), (byte)({GetArg(arguments, 4)}), (byte)({GetArg(arguments, 5)}), (byte)({GetArg(arguments, 6)}))",
                "DrawTriangle" => $"FrameworkWrapper.Framework_DrawTriangle({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, {GetArg(arguments, 4)}, {GetArg(arguments, 5)}, (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}), (byte)({GetArg(arguments, 8)}), (byte)({GetArg(arguments, 9)}))",
                "DrawTriangleLines" => $"FrameworkWrapper.Framework_DrawTriangleLines({GetArg(arguments, 0)}, {GetArg(arguments, 1)}, {GetArg(arguments, 2)}, {GetArg(arguments, 3)}, {GetArg(arguments, 4)}, {GetArg(arguments, 5)}, (byte)({GetArg(arguments, 6)}), (byte)({GetArg(arguments, 7)}), (byte)({GetArg(arguments, 8)}), (byte)({GetArg(arguments, 9)}))",
                "DrawFPS" => $"FrameworkWrapper.Framework_DrawFPS({args})",

                // Time
                "SetTargetFPS" => $"FrameworkWrapper.Framework_SetTargetFPS({args})",
                "GetFrameTime" => "FrameworkWrapper.Framework_GetFrameTime()",
                "GetTime" => "FrameworkWrapper.Framework_GetTime()",
                "SetTimeScale" => $"FrameworkWrapper.Framework_SetTimeScale({args})",
                "GetTimeScale" => "FrameworkWrapper.Framework_GetTimeScale()",

                // Cursor
                "ShowCursor" => "FrameworkWrapper.Framework_ShowCursor()",
                "HideCursor" => "FrameworkWrapper.Framework_HideCursor()",
                "IsCursorHidden" => "FrameworkWrapper.Framework_IsCursorHidden()",
                "SetMousePosition" => $"FrameworkWrapper.Framework_SetMousePosition({args})",
                "GetMouseWheelMove" => "FrameworkWrapper.Framework_GetMouseWheelMove()",

                // Fonts (handle-based)
                "LoadFont" => $"FrameworkWrapper.Framework_AcquireFontH({args})",
                "UnloadFont" => $"FrameworkWrapper.Framework_ReleaseFontH({args})",

                // Camera
                "CameraBeginMode" => "FrameworkWrapper.Framework_Camera_BeginMode()",
                "CameraEndMode" => "FrameworkWrapper.Framework_Camera_EndMode()",
                "CameraGetZoom" => "FrameworkWrapper.Framework_Camera_GetZoom()",
                "CameraGetRotation" => "FrameworkWrapper.Framework_Camera_GetRotation()",
                "CameraSetRotation" => $"FrameworkWrapper.Framework_Camera_SetRotation({args})",
                "CameraSetTarget" => $"FrameworkWrapper.Framework_Camera_SetTarget({args})",
                "CameraSetOffset" => $"FrameworkWrapper.Framework_Camera_SetOffset({args})",
                "CameraUpdate" => $"FrameworkWrapper.Framework_Camera_Update({args})",
                "CameraReset" => "FrameworkWrapper.Framework_Camera_Reset()",
                "CameraPanTo" => $"FrameworkWrapper.Framework_Camera_PanTo({args})",
                "CameraZoomTo" => $"FrameworkWrapper.Framework_Camera_ZoomTo({args})",
                "CameraSetBounds" => $"FrameworkWrapper.Framework_Camera_SetBounds({args})",
                "CameraSetBoundsEnabled" => $"FrameworkWrapper.Framework_Camera_SetBoundsEnabled({args})",
                "CameraSetFollowLerp" => $"FrameworkWrapper.Framework_Camera_SetFollowLerp({args})",

                // Particles (matches wrapper)
                "ParticlesUpdate" => $"FrameworkWrapper.Framework_Particles_Update({args})",
                "ParticlesDraw" => "FrameworkWrapper.Framework_Particles_Draw()",

                // Tilemaps (matches wrapper)
                "TilemapsDraw" => "FrameworkWrapper.Framework_Tilemaps_Draw()",

                // Animation Controller
                "AnimCtrlCreate" => $"FrameworkWrapper.Framework_AnimCtrl_Create({args})",
                "AnimCtrlDestroy" => $"FrameworkWrapper.Framework_AnimCtrl_Destroy({args})",
                "AnimCtrlGet" => $"FrameworkWrapper.Framework_AnimCtrl_Get({args})",
                "AnimCtrlIsValid" => $"FrameworkWrapper.Framework_AnimCtrl_IsValid({args})",
                "AnimCtrlAddState" => $"FrameworkWrapper.Framework_AnimCtrl_AddState({args})",
                "AnimCtrlAddBlendState1D" => $"FrameworkWrapper.Framework_AnimCtrl_AddBlendState1D({args})",
                "AnimCtrlGetState" => $"FrameworkWrapper.Framework_AnimCtrl_GetState({args})",
                "AnimCtrlSetDefaultState" => $"FrameworkWrapper.Framework_AnimCtrl_SetDefaultState({args})",
                "AnimCtrlSetStateSpeed" => $"FrameworkWrapper.Framework_AnimCtrl_SetStateSpeed({args})",
                "AnimCtrlSetStateLoop" => $"FrameworkWrapper.Framework_AnimCtrl_SetStateLoop({args})",
                "AnimCtrlAddBlendClip" => $"FrameworkWrapper.Framework_AnimCtrl_AddBlendClip({args})",
                "AnimCtrlAddTransition" => $"FrameworkWrapper.Framework_AnimCtrl_AddTransition({args})",
                "AnimCtrlAddAnyStateTransition" => $"FrameworkWrapper.Framework_AnimCtrl_AddAnyStateTransition({args})",
                "AnimCtrlAddCondition" => $"FrameworkWrapper.Framework_AnimCtrl_AddCondition({args})",
                "AnimCtrlAddBoolCondition" => $"FrameworkWrapper.Framework_AnimCtrl_AddBoolCondition({args})",
                "AnimCtrlAddTriggerCondition" => $"FrameworkWrapper.Framework_AnimCtrl_AddTriggerCondition({args})",
                "AnimCtrlSetExitTime" => $"FrameworkWrapper.Framework_AnimCtrl_SetExitTime({args})",
                "AnimCtrlSetFloat" => $"FrameworkWrapper.Framework_AnimCtrl_SetFloat({args})",
                "AnimCtrlGetFloat" => $"FrameworkWrapper.Framework_AnimCtrl_GetFloat({args})",
                "AnimCtrlSetInt" => $"FrameworkWrapper.Framework_AnimCtrl_SetInt({args})",
                "AnimCtrlGetInt" => $"FrameworkWrapper.Framework_AnimCtrl_GetInt({args})",
                "AnimCtrlSetBool" => $"FrameworkWrapper.Framework_AnimCtrl_SetBool({args})",
                "AnimCtrlGetBool" => $"FrameworkWrapper.Framework_AnimCtrl_GetBool({args})",
                "AnimCtrlSetTrigger" => $"FrameworkWrapper.Framework_AnimCtrl_SetTrigger({args})",
                "AnimCtrlResetTrigger" => $"FrameworkWrapper.Framework_AnimCtrl_ResetTrigger({args})",
                "AnimCtrlCreateInstance" => $"FrameworkWrapper.Framework_AnimCtrl_CreateInstance({args})",
                "AnimCtrlDestroyInstance" => $"FrameworkWrapper.Framework_AnimCtrl_DestroyInstance({args})",
                "AnimCtrlUpdateInstance" => $"FrameworkWrapper.Framework_AnimCtrl_UpdateInstance({args})",
                "AnimCtrlGetCurrentState" => $"FrameworkWrapper.Framework_AnimCtrl_GetCurrentState({args})",
                "AnimCtrlIsInTransition" => $"FrameworkWrapper.Framework_AnimCtrl_IsInTransition({args})",
                "AnimCtrlInstanceSetFloat" => $"FrameworkWrapper.Framework_AnimCtrl_InstanceSetFloat({args})",
                "AnimCtrlInstanceSetInt" => $"FrameworkWrapper.Framework_AnimCtrl_InstanceSetInt({args})",
                "AnimCtrlInstanceSetBool" => $"FrameworkWrapper.Framework_AnimCtrl_InstanceSetBool({args})",
                "AnimCtrlInstanceSetTrigger" => $"FrameworkWrapper.Framework_AnimCtrl_InstanceSetTrigger({args})",
                "AnimCtrlForceState" => $"FrameworkWrapper.Framework_AnimCtrl_ForceState({args})",
                "AnimCtrlCrossFade" => $"FrameworkWrapper.Framework_AnimCtrl_CrossFade({args})",
                "AnimCtrlPause" => $"FrameworkWrapper.Framework_AnimCtrl_Pause({args})",
                "AnimCtrlResume" => $"FrameworkWrapper.Framework_AnimCtrl_Resume({args})",
                "AnimCtrlSetSpeed" => $"FrameworkWrapper.Framework_AnimCtrl_SetSpeed({args})",

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
    // Special keys
    public const int Space = 32, Escape = 256, Enter = 257, Tab = 258, Backspace = 259;
    public const int Insert = 260, Delete = 261, Right = 262, Left = 263, Down = 264, Up = 265;
    public const int PageUp = 266, PageDown = 267, Home = 268, End = 269;
    public const int CapsLock = 280, ScrollLock = 281, NumLock = 282, PrintScreen = 283;
    public const int Pause = 284;

    // Function keys
    public const int F1 = 290, F2 = 291, F3 = 292, F4 = 293, F5 = 294, F6 = 295;
    public const int F7 = 296, F8 = 297, F9 = 298, F10 = 299, F11 = 300, F12 = 301;

    // Modifier keys
    public const int LeftShift = 340, LeftControl = 341, LeftAlt = 342, LeftSuper = 343;
    public const int RightShift = 344, RightControl = 345, RightAlt = 346, RightSuper = 347;

    // Letters
    public const int A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74;
    public const int K = 75, L = 76, M = 77, N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84;
    public const int U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90;

    // Numbers
    public const int Num0 = 48, Num1 = 49, Num2 = 50, Num3 = 51, Num4 = 52;
    public const int Num5 = 53, Num6 = 54, Num7 = 55, Num8 = 56, Num9 = 57;

    // Numpad
    public const int KP0 = 320, KP1 = 321, KP2 = 322, KP3 = 323, KP4 = 324;
    public const int KP5 = 325, KP6 = 326, KP7 = 327, KP8 = 328, KP9 = 329;
    public const int KPDecimal = 330, KPDivide = 331, KPMultiply = 332;
    public const int KPSubtract = 333, KPAdd = 334, KPEnter = 335, KPEqual = 336;
}

// Mouse button codes
public static class Mouse
{
    public const int Left = 0, Right = 1, Middle = 2;
    public const int Side = 3, Extra = 4, Forward = 5, Back = 6;
}

// Gamepad button codes
public static class GamepadButton
{
    public const int Unknown = 0;
    public const int LeftFaceUp = 1, LeftFaceRight = 2, LeftFaceDown = 3, LeftFaceLeft = 4;
    public const int RightFaceUp = 5, RightFaceRight = 6, RightFaceDown = 7, RightFaceLeft = 8;
    public const int LeftTrigger1 = 9, LeftTrigger2 = 10, RightTrigger1 = 11, RightTrigger2 = 12;
    public const int MiddleLeft = 13, Middle = 14, MiddleRight = 15;
    public const int LeftThumb = 16, RightThumb = 17;
}

// Gamepad axis codes
public static class GamepadAxis
{
    public const int LeftX = 0, LeftY = 1;
    public const int RightX = 2, RightY = 3;
    public const int LeftTrigger = 4, RightTrigger = 5;
}

// Easing types for tweening
public static class Easing
{
    public const int Linear = 0, QuadIn = 1, QuadOut = 2, QuadInOut = 3;
    public const int CubicIn = 4, CubicOut = 5, CubicInOut = 6;
    public const int SineIn = 7, SineOut = 8, SineInOut = 9;
    public const int ExpoIn = 10, ExpoOut = 11, ExpoInOut = 12;
    public const int BounceOut = 13, ElasticOut = 14;
    public const int BackIn = 15, BackOut = 16, BackInOut = 17;
    public const int CircIn = 18, CircOut = 19, CircInOut = 20;
}

// Physics body types
public static class BodyType
{
    public const int Static = 0, Dynamic = 1, Kinematic = 2;
}

// Common color constants (RGBA values as packed integers)
public static class Colors
{
    public static (byte R, byte G, byte B, byte A) White => (245, 245, 245, 255);
    public static (byte R, byte G, byte B, byte A) Black => (0, 0, 0, 255);
    public static (byte R, byte G, byte B, byte A) Red => (230, 41, 55, 255);
    public static (byte R, byte G, byte B, byte A) Green => (0, 228, 48, 255);
    public static (byte R, byte G, byte B, byte A) Blue => (0, 121, 241, 255);
    public static (byte R, byte G, byte B, byte A) Yellow => (253, 249, 0, 255);
    public static (byte R, byte G, byte B, byte A) Orange => (255, 161, 0, 255);
    public static (byte R, byte G, byte B, byte A) Pink => (255, 109, 194, 255);
    public static (byte R, byte G, byte B, byte A) Purple => (200, 122, 255, 255);
    public static (byte R, byte G, byte B, byte A) Gray => (130, 130, 130, 255);
    public static (byte R, byte G, byte B, byte A) DarkGray => (80, 80, 80, 255);
    public static (byte R, byte G, byte B, byte A) LightGray => (200, 200, 200, 255);
    public static (byte R, byte G, byte B, byte A) Transparent => (0, 0, 0, 0);
}
";
    }
}
