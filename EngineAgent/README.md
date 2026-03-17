# Visual Game Studio Engine Agent

An AI-powered development agent for the C++ game engine and VB.NET P/Invoke wrapper, built with the [Claude Agent SDK](https://docs.anthropic.com/en/docs/claude-agent-sdk).

## Setup

### Prerequisites

- Python 3.10+
- Claude Code CLI installed and authenticated
- Visual Studio 2022 with C++ workload (for building the engine DLL)
- .NET 8 SDK (for building RaylibWrapper)

### Install

```bash
cd EngineAgent
python -m venv .venv

# Windows
.venv\Scripts\activate

# macOS/Linux
source .venv/bin/activate

pip install -r requirements.txt
```

## Usage

### One-Shot Tasks

```bash
python engine_agent.py "Add a new SetShaderValue export to framework.h"
python engine_agent.py -v "Fix the camera follow smoothing"
```

### Task Profiles

Profiles configure the agent with domain-specific instructions and tool access:

| Profile | Description | Tools |
|---------|-------------|-------|
| `engine` | C++ framework.h/cpp: adding/modifying exports | Read, Edit, Write, Bash, Glob, Grep |
| `wrapper` | VB.NET RaylibWrapper.vb P/Invoke declarations, marshaling | Read, Edit, Write, Bash, Glob, Grep |
| `ecs` | Entity Component System: components, systems, queries | Read, Edit, Write, Bash, Glob, Grep |
| `audio` | Audio: basic + advanced manager, spatial, groups | Read, Edit, Write, Bash, Glob, Grep |
| `rendering` | Drawing, textures, camera, shaders, sprites, fonts | Read, Edit, Write, Bash, Glob, Grep |
| `docs` | Documentation accuracy (docs/ folder) | Read, Edit, Write, Bash, Glob, Grep |
| `tests` | TestVbDLL, CPPengineTest, sample games | Read, Edit, Write, Bash, Glob, Grep |
| `review` | Read-only code review for memory safety, API quality | Read, Glob, Grep |

```bash
python engine_agent.py --profile engine "Add particle system functions"
python engine_agent.py --profile wrapper "Add missing P/Invoke for DrawTexture"
python engine_agent.py --profile review

python engine_agent.py --list-profiles
```

### Interactive Mode

```bash
python engine_agent.py --interactive
python engine_agent.py --interactive --profile engine
```

In interactive mode:
- Type your task and press Enter
- Type `profile <name>` to switch profiles
- Type `profiles` to list available profiles
- Type `quit` to exit

### Custom MCP Tools

The agent exposes 7 custom tools via an in-process MCP server:

| Tool | Description |
|------|-------------|
| `build_engine` | Build C++ DLL via MSBuild (auto-discovers VS path) |
| `build_wrapper` | Build VB.NET RaylibWrapper via dotnet |
| `run_engine_tests` | Build + run TestVbDLL --test |
| `count_exports` | Count `__declspec(dllexport)` in framework.h |
| `count_pinvokes` | Count `DllImport` in RaylibWrapper.vb |
| `check_sync` | Compare C++ exports vs VB.NET P/Invokes, find gaps |
| `get_engine_architecture` | Return engine architecture overview |

## Architecture

```
engine_agent.py
├── SYSTEM_PROMPT          # Deep C++/VB.NET engine knowledge
├── TASK_PROFILES          # 8 domain-specific configurations
├── Custom Tools (MCP)     # build, test, sync check, architecture
├── run_agent()            # One-shot execution via query()
├── interactive_mode()     # REPL-style interaction loop
└── main_cli()             # CLI with argparse
```

## Examples

```bash
# Add a new C++ export and its VB.NET binding
python engine_agent.py -v --profile engine \
  "Add SetShaderValueFloat that sets a float uniform on a shader"

# Check C++/VB.NET sync status
python engine_agent.py -v \
  "Run check_sync and add the top 10 missing P/Invoke declarations"

# Review engine for memory safety
python engine_agent.py -v --profile review \
  "Check framework.cpp for buffer overflows and use-after-free"

# Fix audio system
python engine_agent.py -v --profile audio \
  "The spatial audio falloff is using linear instead of inverse-square"

# Update docs to match code
python engine_agent.py -v --profile docs \
  "Verify docs/rendering.md matches current framework.h exports"
```
