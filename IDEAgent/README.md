# Visual Game Studio IDE Agent

An AI-powered development agent for the Visual Game Studio IDE, built with the [Claude Agent SDK](https://docs.anthropic.com/en/docs/claude-agent-sdk).

## Setup

### Prerequisites

- Python 3.10+
- Claude Code CLI installed and authenticated
- .NET 8 SDK (for building/testing the IDE)

### Install

```bash
cd IDEAgent
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
python ide_agent.py "Fix the breakpoint margin rendering"
python ide_agent.py -v "Add keyboard shortcut for toggle bookmarks"
```

### Task Profiles

Profiles configure the agent with domain-specific instructions and tool access:

| Profile | Description | Tools |
|---------|-------------|-------|
| `editor` | CodeEditorControl, 7 renderers, margins, multi-cursor, folding | Read, Edit, Write, Bash, Glob, Grep |
| `shell` | MainWindow, 17 panel VMs, 40+ dialog VMs, MVVM | Read, Edit, Write, Bash, Glob, Grep, Agent |
| `debugger` | DebugService, DAP, debug panels (Variables, Watch, CallStack) | Read, Edit, Write, Bash, Glob, Grep |
| `project` | ProjectService, BuildService, solution management | Read, Edit, Write, Bash, Glob, Grep |
| `ui` | Avalonia AXAML views, styling, theming, layout | Read, Edit, Write, Bash, Glob, Grep |
| `refactoring` | 28+ refactoring dialogs, code actions, LSP | Read, Edit, Write, Bash, Glob, Grep |
| `review` | Read-only code review for quality | Read, Glob, Grep |

```bash
python ide_agent.py --profile editor "Fix syntax highlighting for strings"
python ide_agent.py --profile debugger "Fix step-over not working"
python ide_agent.py --profile review

python ide_agent.py --list-profiles
```

### Interactive Mode

```bash
python ide_agent.py --interactive
python ide_agent.py --interactive --profile editor
```

In interactive mode:
- Type your task and press Enter
- Type `profile <name>` to switch profiles
- Type `profiles` to list available profiles
- Type `quit` to exit

### Custom MCP Tools

The agent exposes 6 custom tools via an in-process MCP server:

| Tool | Description |
|------|-------------|
| `build_ide` | Build VisualGameStudio.Shell project |
| `run_tests` | Run test suite with optional filter/category |
| `run_ide` | Launch the IDE executable |
| `list_services` | List all 29 service interfaces and implementations |
| `get_ide_architecture` | Get 4-layer architecture diagram |
| `list_viewmodels` | List all ViewModels by category |

## Architecture

```
ide_agent.py
├── SYSTEM_PROMPT          # Deep IDE architecture knowledge
├── TASK_PROFILES          # 7 domain-specific configurations
├── Custom Tools (MCP)     # build, test, run, list services/VMs/architecture
├── run_agent()            # One-shot execution via query()
├── interactive_mode()     # REPL-style interaction loop
└── main_cli()             # CLI with argparse
```

## Examples

```bash
# Fix an editor rendering bug
python ide_agent.py -v --profile editor \
  "The IndentationGuideRenderer draws guides on blank lines incorrectly"

# Add a new panel
python ide_agent.py -v --profile shell \
  "Add a Performance Profiler panel that shows method execution times"

# Review debug subsystem
python ide_agent.py -v --profile debugger \
  "Check the DebugService for thread safety issues"

# Audit refactoring completeness
python ide_agent.py -v --profile refactoring \
  "Which refactoring dialogs are not yet wired to LSP code actions?"

# Full code review
python ide_agent.py -v --profile review \
  "Review the editor for memory leaks and event subscription issues"
```
