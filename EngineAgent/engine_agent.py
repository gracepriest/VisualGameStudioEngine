#!/usr/bin/env python3
"""
Visual Game Studio Engine Agent — Claude Agent SDK Application

An AI agent specialized for maintaining the C++ game engine DLL and VB.NET
P/Invoke wrapper. Supports task profiles for engine, wrapper, ECS, audio,
rendering, docs, tests, and code review.

Usage:
    python engine_agent.py "Add a particle emitter component to the ECS"
    python engine_agent.py --profile engine "Fix camera shake duration"
    python engine_agent.py --profile wrapper "Add missing P/Invoke for tilemap"
    python engine_agent.py --profile review
    python engine_agent.py --interactive
"""

import asyncio
import argparse
import os
import re
import subprocess
import sys
from typing import Any

from claude_agent_sdk import (
    query,
    tool,
    create_sdk_mcp_server,
    ClaudeAgentOptions,
    AssistantMessage,
    ResultMessage,
    SystemMessage,
)

# ---------------------------------------------------------------------------
# Project paths
# ---------------------------------------------------------------------------
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENGINE_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudioEngine")
WRAPPER_DIR = os.path.join(PROJECT_ROOT, "RaylibWrapper")
VBTEST_DIR = os.path.join(PROJECT_ROOT, "TestVbDLL")
DOCS_DIR = os.path.join(PROJECT_ROOT, "docs")

FRAMEWORK_H = os.path.join(ENGINE_DIR, "framework.h")
FRAMEWORK_CPP = os.path.join(ENGINE_DIR, "framework.cpp")
WRAPPER_VB = os.path.join(WRAPPER_DIR, "RaylibWrapper.vb")

# ---------------------------------------------------------------------------
# System prompt — deep engine architecture knowledge
# ---------------------------------------------------------------------------
SYSTEM_PROMPT = """\
You are an expert developer working on the Visual Game Studio Engine, a C++ 2D game
engine DLL built on Raylib 5.5.0 with a VB.NET P/Invoke wrapper for managed consumption.

## Engine Architecture

### C++ Engine (VisualGameStudioEngine/)
- framework.h (4,123 lines): All __declspec(dllexport) declarations inside extern "C" block.
  ~2,313 exported functions. Enums, callback typedefs, ABI-safe structs.
- framework.cpp (27,314 lines): Full implementation. Anonymous namespace for global state.
  Uses Raylib 5.5.0 internally (InitWindow, BeginDrawing, DrawTexture, etc.)
- Build: MSBuild VisualGameStudioEngine.vcxproj -p:Configuration=Release -p:Platform=x64
- Output: VisualGameStudioEngine.dll (C++ DynamicLibrary, MSVC v143, C++17)

### VB.NET Wrapper (RaylibWrapper/)
- RaylibWrapper.vb (10,276 lines): 2,229 DllImport declarations in Public Module FrameworkWrapper
  Organized into 76+ #Region blocks by feature category
- UtiliyClasses.vb: TextureHandle, FontHandle, MusicHandle (IDisposable wrappers)
- Utiliy.vb: Helper utilities
- Build: dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release

### The Sync Requirement (CRITICAL)
Every __declspec(dllexport) function in framework.h MUST have a matching
<DllImport(ENGINE_DLL)> declaration in RaylibWrapper.vb. When adding new engine
functions, ALWAYS add both the C++ export AND the VB.NET P/Invoke in the same task.

### P/Invoke Marshaling Rules
- bool -> <MarshalAs(UnmanagedType.I1)> Boolean
- const char* -> String (input), <MarshalAs(UnmanagedType.LPStr)> String (return)
- int*/float* (out) -> ByRef ... As Integer/Single
- unsigned char -> Byte
- Callbacks -> Delegate types with CallingConvention.Cdecl

## Correct Function Names (Common Mistakes in Old Docs)
- Framework_Initialize() NOT Framework_Init()
- Framework_Update() + Framework_BeginDrawing() NOT Framework_BeginFrame()
- Framework_EndDrawing() NOT Framework_EndFrame()
- Framework_AcquireTextureH() NOT Framework_LoadTextureH()
- Framework_AcquireMusicH() NOT Framework_LoadMusicH()
- Framework_Ecs_SetTransformPosition() NOT Framework_Ecs_SetPosition()
- Framework_Ecs_SetVelocity() NOT Framework_Ecs_SetVelocity2D()
- Framework_Ecs_DrawSprites() NOT Framework_Ecs_DrawAll()
- Framework_Ecs_IsAlive() NOT Framework_Ecs_EntityExists()
- Framework_Camera_SetFollowLerp() NOT Framework_Camera_SetFollowSmoothing()

## Game Loop Pattern
Framework_Initialize(800, 600, "Title")
Framework_SetTargetFPS(60)
Framework_InitAudio()
While Not Framework_ShouldClose()
    Framework_Update()
    Framework_Camera_Update()
    Framework_Ecs_UpdateVelocities()
    Framework_BeginDrawing()
    Framework_ClearBackground(r, g, b, a)
    Framework_Camera_BeginMode()
        Framework_Ecs_DrawSprites()
    Framework_Camera_EndMode()
    Framework_Debug_Render()
    Framework_EndDrawing()
    Framework_UpdateAllMusic()
    Framework_Audio_Update()
End While
Framework_CloseAudio()
Framework_Shutdown()

## 31 API Categories (~2,313 functions)
Engine State, Draw Control, Timing, Input (Keyboard/Mouse), Collisions,
Textures (raw + handle), Images, Render Textures, Camera2D (managed),
Fonts (raw + handle), Shaders, Audio (basic + manager), Scene System (+transitions),
ECS (Entities, Transform2D, Sprite2D, Velocity2D, BoxCollider2D, Hierarchy,
Name, Tag, Enabled), ECS Systems, Physics, Debug, Tilemap, Animation,
Particle System, UI System, Behavior Trees, Tweening, Event/Timer/Pool,
AI/Pathfinding, Dialogue/Inventory/Quest, Lighting, Screen Effects,
Save/Load, Networking, Sprite Batching, and more

## ECS Component System
Components: Transform2D, Sprite2D, Name, Tag, Hierarchy, Velocity2D,
BoxCollider2D, Enabled, Tilemap, Animator, ParticleEmitter
Entity lifecycle: CreateEntity -> add components -> DestroyEntity
Systems: UpdateVelocities, DrawSprites, UpdateCollisions

## Test Projects
- CPPengineTest/: C++ game loop test (links to .lib)
- TestVbDLL/: VB.NET tests (FrameworkTests.vb ~293KB)
- SampleGames/Pong/: BasicLang pong game

## Coding Standards
- All C++ exports prefixed with Framework_ (C ABI, no mangling)
- All exports inside extern "C" { } block
- Global state in anonymous namespace in framework.cpp
- New features: export in framework.h, implement in framework.cpp, P/Invoke in RaylibWrapper.vb
- Group functions by system using comment headers (C++) and #Region (VB.NET)
- Parameter ordering: id/handle first, data params, RGBA color last
"""

# ---------------------------------------------------------------------------
# Task profiles — 8 domain-specific configurations
# ---------------------------------------------------------------------------
TASK_PROFILES: dict[str, dict[str, Any]] = {
    "engine": {
        "description": "C++ engine: framework.h declarations, framework.cpp implementation",
        "system_prompt_suffix": """
Focus on the C++ engine in VisualGameStudioEngine/:
- framework.h: All __declspec(dllexport) declarations inside extern "C" { }
- framework.cpp: Full implementation, 27,314 lines, anonymous namespace for global state.
- Build: MSBuild VisualGameStudioEngine.vcxproj -p:Configuration=Release -p:Platform=x64

When adding new engine functions:
1. Add __declspec(dllexport) declaration to framework.h in the correct section
2. Implement in framework.cpp with proper error handling
3. Add matching P/Invoke to RaylibWrapper.vb in the matching #Region
4. Add test to TestVbDLL/FrameworkTests.vb
5. Update docs if applicable

C ABI rules: no C++ classes/templates in signatures, use int handles for opaque
objects, strings via const char*, output params via pointers.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Analyze framework.h and framework.cpp for bugs, missing features, or API inconsistencies.",
    },
    "wrapper": {
        "description": "VB.NET P/Invoke wrapper: RaylibWrapper.vb, handle classes, marshaling",
        "system_prompt_suffix": """
Focus on the VB.NET wrapper in RaylibWrapper/:
- RaylibWrapper.vb: 10,276 lines, 2,229 DllImport declarations
  76+ #Region blocks organized by feature category
- UtiliyClasses.vb: TextureHandle, FontHandle, MusicHandle (IDisposable)
- Build: dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release

Marshaling patterns:
- bool: <MarshalAs(UnmanagedType.I1)> Boolean
- const char*: String (input), <MarshalAs(UnmanagedType.LPStr)> String (return)
- int*/float* out: ByRef ... As Integer/Single
- Callbacks: Named Delegate types with CallingConvention.Cdecl
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Check RaylibWrapper.vb for marshaling errors and missing P/Invoke declarations.",
    },
    "ecs": {
        "description": "Entity Component System: entities, components, systems, queries",
        "system_prompt_suffix": """
Focus on the ECS subsystem:

Components: Transform2D, Sprite2D, Name, Tag, Hierarchy, Velocity2D,
BoxCollider2D, Enabled, Tilemap, Animator, ParticleEmitter

Entity API: CreateEntity -> Add components -> Set properties -> DestroyEntity
Systems: UpdateVelocities, DrawSprites, UpdateCollisions
Queries: QueryByTag, QueryByComponent

Both C++ (framework.h) and VB.NET (RaylibWrapper.vb) must stay in sync.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review the ECS for completeness, performance, and missing component types.",
    },
    "audio": {
        "description": "Audio: basic Raylib audio + advanced manager, spatial, groups",
        "system_prompt_suffix": """
Focus on the audio subsystem:

Basic Audio: InitAudio/CloseAudio, LoadSoundH/PlaySoundH,
AcquireMusicH/PlayMusicH/UpdateAllMusic. Handle-based (H suffix = int handle).

Advanced Audio Manager: Groups (MASTER, MUSIC, SFX, VOICE, AMBIENT),
spatial audio, sound pools, crossfading.

VB.NET: MusicHandle class (IDisposable, .Play, .Pause, .Stop, .SetVolume)
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review audio system for missing features and handle lifecycle correctness.",
    },
    "rendering": {
        "description": "Drawing, textures, camera, shaders, sprites, fonts, batching",
        "system_prompt_suffix": """
Focus on the rendering subsystem:

Primitives: DrawText, DrawRectangle, DrawLine, DrawCircle (RGBA as last 4 params)
Textures: Raw (LoadTexture/DrawTexture) and Handle-based (AcquireTextureH/DrawTextureH)
Camera2D: Single global, BeginMode/EndMode, follow with lerp, shake, zoom
Fonts: Raw and Handle-based
Shaders: Load/Begin/End/SetUniform
Sprite batching: SpriteBatch_* functions

VB.NET: TextureHandle (.Draw, .DrawEx, .DrawPro), FontHandle (.DrawText)
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review rendering pipeline for correctness and performance.",
    },
    "docs": {
        "description": "Documentation: docs/ folder, API reference accuracy",
        "system_prompt_suffix": """
Focus on documentation in docs/:
- API_REFERENCE.md, API.md: Function reference
- GETTING_STARTED.md, UserGuide.md: Guides

CRITICAL: Many old docs had WRONG function names. Always verify against framework.h.
Common mistakes: Init->Initialize, BeginFrame->Update+BeginDrawing,
LoadTextureH->AcquireTextureH, SetPosition->SetTransformPosition.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Audit docs for incorrect function names and missing API coverage.",
    },
    "tests": {
        "description": "TestVbDLL, CPPengineTest, CppTitle0, sample games",
        "system_prompt_suffix": """
Focus on test projects:

TestVbDLL/ (VB.NET): FrameworkTests.vb (~293KB), 30+ test suites
  Run: dotnet build TestVbDLL/TestVbDLL.vbproj then TestVbDLL.exe --test
  Note: Tests need Framework_Initialize() (creates a window)

CPPengineTest/ (C++): Game loop test, links to .lib
CppTitle0/ (C++): Mario platformer demo
SampleGames/Pong/ (BasicLang): Pong game using engine
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review test coverage and identify untested engine systems.",
    },
    "review": {
        "description": "Read-only code review: memory safety, API design, sync issues",
        "system_prompt_suffix": """
Perform a thorough code review. Look for:
- Memory leaks (unfreed allocations, missing cleanup in Shutdown)
- Buffer overflows (strcpy without bounds, fixed-size arrays)
- Null pointer dereferences (missing handle/id validation)
- Thread safety (global state accessed from callbacks)
- API inconsistencies (naming, parameter ordering)
- C++/VB.NET sync issues (exports without P/Invoke, wrong marshaling)
- Handle lifecycle bugs (double-free, use-after-free)
- Performance (O(n) scans where hash lookup possible)
Do NOT suggest style-only changes. Focus on real bugs and safety issues.
""",
        "allowed_tools": ["Read", "Glob", "Grep"],
        "default_prompt": "Review the engine for memory safety, API consistency, and correctness.",
    },
}

# ---------------------------------------------------------------------------
# Helper: find MSBuild.exe
# ---------------------------------------------------------------------------

def _find_msbuild() -> str | None:
    """Discover MSBuild.exe via vswhere or known paths."""
    vswhere = os.path.join(
        os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
        "Microsoft Visual Studio", "Installer", "vswhere.exe",
    )
    if os.path.exists(vswhere):
        try:
            result = subprocess.run(
                [vswhere, "-latest", "-products", "*",
                 "-requires", "Microsoft.Component.MSBuild",
                 "-find", r"MSBuild\**\Bin\MSBuild.exe"],
                capture_output=True, text=True, timeout=10,
            )
            paths = result.stdout.strip().splitlines()
            if paths:
                return paths[0]
        except (subprocess.TimeoutExpired, FileNotFoundError):
            pass

    for edition in ["Enterprise", "Professional", "Community"]:
        path = os.path.join(
            r"C:\Program Files\Microsoft Visual Studio\2022", edition,
            r"MSBuild\Current\Bin\MSBuild.exe",
        )
        if os.path.exists(path):
            return path
    return None


# ---------------------------------------------------------------------------
# Custom MCP tools — engine-specific operations
# ---------------------------------------------------------------------------

@tool(
    "build_engine",
    "Build the C++ VisualGameStudioEngine DLL via MSBuild",
    {
        "type": "object",
        "properties": {
            "configuration": {
                "type": "string",
                "description": "Debug or Release (default: Release)",
            },
            "platform": {
                "type": "string",
                "description": "Win32 or x64 (default: x64)",
            },
        },
    },
)
async def build_engine(args: dict[str, Any]) -> dict[str, Any]:
    """Build the C++ engine DLL."""
    config = args.get("configuration", "Release")
    platform = args.get("platform", "x64")
    msbuild = _find_msbuild()

    if not msbuild:
        return _text_result("MSBuild.exe not found. Install Visual Studio 2022 with C++ workload.")

    vcxproj = os.path.join(ENGINE_DIR, "VisualGameStudioEngine.vcxproj")
    if not os.path.exists(vcxproj):
        return _text_result(f"Project not found at {vcxproj}")

    proc = await asyncio.create_subprocess_exec(
        msbuild, vcxproj, f"-p:Configuration={config}", f"-p:Platform={platform}",
        "-verbosity:minimal",
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=ENGINE_DIR,
    )
    stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=300)
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "build_wrapper",
    "Build the VB.NET RaylibWrapper project",
    {
        "type": "object",
        "properties": {
            "configuration": {
                "type": "string",
                "description": "Debug or Release (default: Release)",
            },
        },
    },
)
async def build_wrapper(args: dict[str, Any]) -> dict[str, Any]:
    """Build the VB.NET wrapper."""
    config = args.get("configuration", "Release")
    vbproj = os.path.join(WRAPPER_DIR, "RaylibWrapper.vbproj")

    if not os.path.exists(vbproj):
        return _text_result(f"Project not found at {vbproj}")

    proc = await asyncio.create_subprocess_exec(
        "dotnet", "build", vbproj, "-c", config,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=300)
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "run_engine_tests",
    "Build and run TestVbDLL framework tests",
    {
        "type": "object",
        "properties": {
            "build_only": {
                "type": "boolean",
                "description": "Only build, do not run (default: false)",
            },
        },
    },
)
async def run_engine_tests(args: dict[str, Any]) -> dict[str, Any]:
    """Build and run VB.NET engine tests."""
    build_only = args.get("build_only", False)
    vbproj = os.path.join(VBTEST_DIR, "TestVbDLL.vbproj")

    if not os.path.exists(vbproj):
        return _text_result(f"Test project not found at {vbproj}")

    proc = await asyncio.create_subprocess_exec(
        "dotnet", "build", vbproj, "-c", "Release",
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=300)
    build_output = stdout.decode("utf-8", errors="replace")

    if proc.returncode != 0:
        errors = stderr.decode("utf-8", errors="replace")
        return _text_result(f"Build failed (exit {proc.returncode}):\n{build_output}\n{errors}")

    if build_only:
        return _text_result(f"Build succeeded:\n{build_output}")

    exe = os.path.join(VBTEST_DIR, "bin", "Release", "net8.0-windows", "TestVbDLL.exe")
    if not os.path.exists(exe):
        exe = os.path.join(VBTEST_DIR, "bin", "Release", "net8.0", "TestVbDLL.exe")

    if not os.path.exists(exe):
        return _text_result(f"Build succeeded but exe not found. Check output path.\n{build_output}")

    return _text_result(
        f"Build succeeded.\nTest executable: {exe}\n"
        "Note: Tests require a display (Framework_Initialize creates a window).\n"
        "Run manually: TestVbDLL.exe --test"
    )


@tool(
    "count_exports",
    "Count __declspec(dllexport) functions in framework.h",
    {"type": "object", "properties": {}},
)
async def count_exports(args: dict[str, Any]) -> dict[str, Any]:
    """Count exported functions in the C++ header."""
    if not os.path.exists(FRAMEWORK_H):
        return _text_result(f"framework.h not found at {FRAMEWORK_H}")

    with open(FRAMEWORK_H, "r", encoding="utf-8", errors="replace") as f:
        content = f.read()

    export_pattern = re.compile(r"__declspec\s*\(\s*dllexport\s*\)")
    lines = content.splitlines()
    total = sum(1 for line in lines if export_pattern.search(line))

    section_counts: dict[str, int] = {}
    current_section = "Ungrouped"
    for line in lines:
        section_match = re.match(r"^\s*//\s*[-=]{3,}\s*(.+?)\s*[-=]*\s*$", line)
        if section_match:
            current_section = section_match.group(1).strip()
        if export_pattern.search(line):
            section_counts[current_section] = section_counts.get(current_section, 0) + 1

    breakdown = "\n".join(f"  {name}: {count}" for name, count in section_counts.items())
    return _text_result(f"Total exports in framework.h: {total}\n\nBy section:\n{breakdown}")


@tool(
    "count_pinvokes",
    "Count DllImport declarations in RaylibWrapper.vb",
    {"type": "object", "properties": {}},
)
async def count_pinvokes(args: dict[str, Any]) -> dict[str, Any]:
    """Count P/Invoke declarations in the VB.NET wrapper."""
    if not os.path.exists(WRAPPER_VB):
        return _text_result(f"RaylibWrapper.vb not found at {WRAPPER_VB}")

    with open(WRAPPER_VB, "r", encoding="utf-8", errors="replace") as f:
        lines = f.readlines()

    total = sum(1 for line in lines if "DllImport(" in line)

    region_counts: dict[str, int] = {}
    current_region = "Outside Region"
    for line in lines:
        region_match = re.match(r'^\s*#Region\s+"(.+)"', line)
        if region_match:
            current_region = region_match.group(1)
        if "DllImport(" in line:
            region_counts[current_region] = region_counts.get(current_region, 0) + 1

    breakdown = "\n".join(f"  {name}: {count}" for name, count in region_counts.items())
    return _text_result(f"Total DllImport in RaylibWrapper.vb: {total}\n\nBy #Region:\n{breakdown}")


@tool(
    "check_sync",
    "Compare C++ exports vs VB.NET P/Invokes to find missing wrappers",
    {"type": "object", "properties": {}},
)
async def check_sync(args: dict[str, Any]) -> dict[str, Any]:
    """Find functions that exist in C++ but not VB.NET (or vice versa)."""
    if not os.path.exists(FRAMEWORK_H):
        return _text_result(f"framework.h not found at {FRAMEWORK_H}")
    if not os.path.exists(WRAPPER_VB):
        return _text_result(f"RaylibWrapper.vb not found at {WRAPPER_VB}")

    with open(FRAMEWORK_H, "r", encoding="utf-8", errors="replace") as f:
        h_content = f.read()

    export_re = re.compile(r"__declspec\s*\(\s*dllexport\s*\)\s+\S+\s+(\w+)\s*\(")
    cpp_exports = set(export_re.findall(h_content))

    with open(WRAPPER_VB, "r", encoding="utf-8", errors="replace") as f:
        vb_content = f.read()

    pinvoke_re = re.compile(r"Public\s+(?:Function|Sub)\s+(\w+)\s*\(")
    vb_funcs = set(pinvoke_re.findall(vb_content))

    missing_in_vb = sorted(cpp_exports - vb_funcs)
    missing_in_cpp = sorted(vb_funcs - cpp_exports)

    parts = [
        f"C++ exports: {len(cpp_exports)}",
        f"VB.NET P/Invokes: {len(vb_funcs)}",
        "",
    ]

    if missing_in_vb:
        parts.append(f"Missing VB.NET P/Invoke ({len(missing_in_vb)} functions):")
        for name in missing_in_vb[:50]:
            parts.append(f"  {name}")
        if len(missing_in_vb) > 50:
            parts.append(f"  ... and {len(missing_in_vb) - 50} more")
    else:
        parts.append("All C++ exports have VB.NET P/Invoke wrappers!")

    if missing_in_cpp:
        parts.append(f"\nVB.NET without C++ export ({len(missing_in_cpp)}):")
        for name in missing_in_cpp[:20]:
            parts.append(f"  {name}")
        if len(missing_in_cpp) > 20:
            parts.append(f"  ... and {len(missing_in_cpp) - 20} more")

    return _text_result("\n".join(parts))


@tool(
    "get_engine_architecture",
    "Return engine architecture overview with component counts",
    {"type": "object", "properties": {}},
)
async def get_engine_architecture(args: dict[str, Any]) -> dict[str, Any]:
    """Return the engine architecture overview."""
    return _text_result("""\
Visual Game Studio Engine -- Architecture Overview

+-----------------------------------------------------------+
|          VisualGameStudioEngine.dll (C++)                  |
|  framework.h: ~2,313 exported functions (C ABI)           |
|  framework.cpp: 27,314 lines implementation               |
|  Built on Raylib 5.5.0 (NuGet), MSVC v143, C++17         |
+-----------------------------------------------------------+
                        | P/Invoke
+-----------------------------------------------------------+
|          RaylibWrapper.vb (VB.NET)                         |
|  2,229 DllImport declarations, 76+ #Region blocks         |
|  Handle classes: TextureHandle, FontHandle, MusicHandle    |
+-----------------------------------------------------------+
                        | Used by
+-----------------------------------------------------------+
|  Games: BasicLang (.bas), VB.NET, C# (via wrapper)        |
|  Tests: TestVbDLL (293KB), CPPengineTest, CppTitle0       |
|  Samples: Pong, SpaceShooter                              |
+-----------------------------------------------------------+

31 API Categories:
  Core: Engine State, Draw Control, Timing, Input (KB/Mouse)
  Graphics: Textures, Images, Fonts, Shaders, Camera2D, Render Textures
  Audio: Basic (Sound/Music), Advanced Manager (Groups, Spatial, Effects)
  ECS: Entities, Transform2D, Sprite2D, Velocity2D, BoxCollider2D,
       Hierarchy, Name, Tag, Enabled, Systems (Update/Draw/Collisions)
  Scenes: Scene System, Transitions (14 effects), Loading Screens
  Physics: Overlap Queries, Full Physics (Bodies, Joints)
  Advanced: Tilemap, Animation, Particles, UI, Tweening, Behavior Trees,
            AI/Pathfinding, Dialogue, Inventory, Quest, 2D Lighting,
            Screen Effects, Save/Load, Networking, Sprite Batching

Game Loop: Initialize -> [Update -> BeginDrawing -> Draw -> EndDrawing] -> Shutdown
""")


def _text_result(text: str) -> dict[str, Any]:
    """Helper to create a text content result."""
    return {"content": [{"type": "text", "text": text}]}


# ---------------------------------------------------------------------------
# MCP server with all custom tools
# ---------------------------------------------------------------------------
engine_mcp_server = create_sdk_mcp_server(
    name="engine-tools",
    version="1.0.0",
    tools=[build_engine, build_wrapper, run_engine_tests,
           count_exports, count_pinvokes, check_sync, get_engine_architecture],
)

# ---------------------------------------------------------------------------
# Agent runner
# ---------------------------------------------------------------------------

async def run_agent(
    prompt: str,
    profile_name: str | None = None,
    verbose: bool = False,
) -> str:
    """Run the engine agent with the given prompt and optional task profile."""

    system = SYSTEM_PROMPT
    allowed_tools = ["Read", "Edit", "Write", "Bash", "Glob", "Grep"]

    if profile_name and profile_name in TASK_PROFILES:
        profile = TASK_PROFILES[profile_name]
        system += "\n" + profile["system_prompt_suffix"]
        allowed_tools = list(profile["allowed_tools"])
        if not prompt:
            prompt = profile["default_prompt"]
        if verbose:
            print(f"Using profile: {profile_name} -- {profile['description']}")

    custom_tool_names = [
        "mcp__engine-tools__build_engine",
        "mcp__engine-tools__build_wrapper",
        "mcp__engine-tools__run_engine_tests",
        "mcp__engine-tools__count_exports",
        "mcp__engine-tools__count_pinvokes",
        "mcp__engine-tools__check_sync",
        "mcp__engine-tools__get_engine_architecture",
    ]
    allowed_tools.extend(custom_tool_names)

    options = ClaudeAgentOptions(
        system_prompt=system,
        allowed_tools=allowed_tools,
        permission_mode="acceptEdits",
        cwd=PROJECT_ROOT,
        mcp_servers={
            "engine-tools": engine_mcp_server,
        },
    )

    result_text = ""

    async for message in query(prompt=prompt, options=options):
        if isinstance(message, AssistantMessage):
            for block in message.content:
                if getattr(block, "type", None) == "text":
                    if verbose:
                        print(f"Agent: {block.text}")
                elif getattr(block, "type", None) == "tool_use":
                    if verbose:
                        print(f"  [tool] {block.name}")

        elif isinstance(message, ResultMessage):
            if not message.is_error:
                result_text = message.result or ""
                if verbose:
                    print(f"\nResult:\n{result_text}")
            else:
                result_text = f"Error: {message.subtype}"
                print(f"Agent error: {message.subtype}", file=sys.stderr)

        elif isinstance(message, SystemMessage) and verbose:
            print(f"  [system] {message.subtype}: {message.data}")

    return result_text


# ---------------------------------------------------------------------------
# Interactive mode
# ---------------------------------------------------------------------------

async def interactive_mode(profile_name: str | None = None):
    """Run the agent in interactive loop mode."""
    print("Visual Game Studio Engine Agent -- Interactive Mode")
    print("Type 'quit' to exit, 'profile <name>' to switch profiles")
    print(f"Available profiles: {', '.join(TASK_PROFILES.keys())}")
    if profile_name:
        print(f"Active profile: {profile_name}")
    print("-" * 60)

    current_profile = profile_name

    while True:
        try:
            user_input = input("\n> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nGoodbye!")
            break

        if not user_input:
            continue

        if user_input.lower() == "quit":
            break

        if user_input.lower().startswith("profile "):
            name = user_input.split(None, 1)[1].strip()
            if name in TASK_PROFILES:
                current_profile = name
                print(f"Switched to profile: {name}")
            else:
                print(f"Unknown profile. Available: {', '.join(TASK_PROFILES.keys())}")
            continue

        if user_input.lower() == "profiles":
            for name, info in TASK_PROFILES.items():
                marker = " *" if name == current_profile else ""
                print(f"  {name}{marker}: {info['description']}")
            continue

        await run_agent(user_input, profile_name=current_profile, verbose=True)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main_cli():
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description="Visual Game Studio Engine Agent -- Claude Agent SDK",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Task Profiles:
  engine      C++ framework.h/cpp: exports, implementation
  wrapper     VB.NET P/Invoke: RaylibWrapper.vb, marshaling
  ecs         Entity Component System: components, systems
  audio       Audio: basic + advanced manager, spatial
  rendering   Drawing, textures, camera, shaders, fonts
  docs        Documentation accuracy (docs/ folder)
  tests       TestVbDLL, CPPengineTest, sample games
  review      Read-only code review: memory safety, API quality

Examples:
  %(prog)s "Add a particle emitter component to the ECS"
  %(prog)s --profile engine "Fix camera shake duration"
  %(prog)s --profile wrapper "Add missing P/Invoke for tilemap"
  %(prog)s --profile ecs "Add a CircleCollider2D component"
  %(prog)s --profile review
  %(prog)s --interactive
  %(prog)s --interactive --profile engine
""",
    )

    parser.add_argument("prompt", nargs="?", default="", help="Task description")
    parser.add_argument("--profile", "-p", choices=list(TASK_PROFILES.keys()), help="Task profile")
    parser.add_argument("--interactive", "-i", action="store_true", help="Interactive mode")
    parser.add_argument("--verbose", "-v", action="store_true", help="Show reasoning")
    parser.add_argument("--list-profiles", action="store_true", help="List profiles")

    args = parser.parse_args()

    if args.list_profiles:
        print("Available Task Profiles:\n")
        for name, info in TASK_PROFILES.items():
            print(f"  {name:12s}  {info['description']}")
        return

    if args.interactive:
        asyncio.run(interactive_mode(profile_name=args.profile))
        return

    if not args.prompt and not args.profile:
        parser.print_help()
        return

    result = asyncio.run(run_agent(
        args.prompt, profile_name=args.profile, verbose=args.verbose,
    ))

    if result and not args.verbose:
        print(result)


if __name__ == "__main__":
    main_cli()
