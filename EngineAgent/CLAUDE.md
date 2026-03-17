# Engine Agent — Claude Code Instructions

This directory contains a Claude Agent SDK application for the C++ game engine and VB.NET P/Invoke wrapper.

## What This Is

A Python application that uses the Claude Agent SDK to create an AI agent specialized for maintaining the VisualGameStudioEngine C++ DLL and its VB.NET RaylibWrapper bindings. The agent has:

- **8 task profiles** for different engine subsystems (engine, wrapper, ecs, audio, rendering, docs, tests, review)
- **7 custom MCP tools** for building, testing, sync-checking, and querying architecture
- **Interactive mode** for conversational engine development sessions

## Running the Agent

```bash
pip install -r requirements.txt

python engine_agent.py "Add a new particle system export"
python engine_agent.py --profile wrapper "Add P/Invoke for SetShaderValue"
python engine_agent.py --interactive
python engine_agent.py --list-profiles
```

## Project Context

The agent operates on the parent directory (VisualGameStudioEngine), which contains:
- **VisualGameStudioEngine/** — C++ game engine DLL (framework.h ~4,123 lines, framework.cpp ~27,314 lines)
- **RaylibWrapper/** — VB.NET P/Invoke bindings (RaylibWrapper.vb ~10,276 lines)
- **CPPengineTest/** — Native C++ engine tests
- **TestVbDLL/** — VB.NET sample game using the wrapper
- **SampleGames/** — Sample games (Pong, SpaceShooter)
- **docs/** — Engine API documentation (12 files)

## Key Conventions

- Every `__declspec(dllexport)` in framework.h MUST have a matching `<DllImport>` in RaylibWrapper.vb
- C++ exports use `extern "C"` with `__declspec(dllexport)` and `__cdecl` calling convention
- VB.NET uses `<DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>`
- String marshaling: `<MarshalAs(UnmanagedType.LPStr)>` for C strings
- Build engine: MSBuild (auto-discovered via vswhere.exe)
- Build wrapper: `dotnet build RaylibWrapper/RaylibWrapper.vbproj`
- Use `check_sync` tool to find missing P/Invoke declarations
