# BasicLang Compiler Agent

An AI-powered development agent for the BasicLang compiler, built with the [Claude Agent SDK](https://docs.anthropic.com/en/docs/claude-agent-sdk).

## Setup

### Prerequisites

- Python 3.10+
- Claude Code CLI installed and authenticated
- .NET 8 SDK (for building/testing the compiler)

### Install

```bash
cd BasicLangAgent
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
# General task
python basiclang_agent.py "Fix the parser bug in For loops"

# With verbose output (shows reasoning + tool calls)
python basiclang_agent.py -v "Add support for Do...While loops"
```

### Task Profiles

Profiles configure the agent with domain-specific instructions and tool access:

| Profile | Description | Tools |
|---------|-------------|-------|
| `compiler` | Compiler pipeline (lexer, parser, IR, codegen) | Read, Edit, Write, Bash, Glob, Grep, Agent |
| `debugger` | Debugger and DAP protocol | Read, Edit, Write, Bash, Glob, Grep |
| `lsp` | LSP server features | Read, Edit, Write, Bash, Glob, Grep |
| `tests` | Test suite management | Read, Edit, Write, Bash, Glob, Grep |
| `review` | Code review (read-only) | Read, Glob, Grep |
| `backend` | Backend code generation | Read, Edit, Write, Bash, Glob, Grep |

```bash
# Use a specific profile
python basiclang_agent.py --profile compiler "Add Do...Loop syntax"
python basiclang_agent.py --profile debugger "Fix step-over not working"
python basiclang_agent.py --profile review  # uses default review prompt

# List all profiles
python basiclang_agent.py --list-profiles
```

### Interactive Mode

```bash
# Start interactive session
python basiclang_agent.py --interactive

# With a default profile
python basiclang_agent.py --interactive --profile compiler
```

In interactive mode:
- Type your task and press Enter
- Type `profile <name>` to switch profiles
- Type `profiles` to list available profiles
- Type `quit` to exit

### Custom MCP Tools

The agent exposes 4 custom tools via an in-process MCP server:

| Tool | Description |
|------|-------------|
| `compile_basiclang` | Compile a `.bas` file with a specified backend |
| `run_tests` | Run the xUnit test suite (with optional filter) |
| `build_project` | Build a .NET project in the solution |
| `get_compiler_pipeline` | Get compiler pipeline overview |

These are automatically available to the agent alongside built-in tools (Read, Edit, Bash, etc.).

## Architecture

```
basiclang_agent.py
├── SYSTEM_PROMPT          # Base knowledge about the codebase
├── TASK_PROFILES          # 6 domain-specific configurations
├── Custom Tools (MCP)     # compile, test, build, pipeline info
├── run_agent()            # One-shot execution via query()
├── interactive_mode()     # REPL-style interaction loop
└── main_cli()             # CLI with argparse
```

## Examples

```bash
# Fix a compiler bug
python basiclang_agent.py -v --profile compiler \
  "The For Each loop doesn't handle Dictionary iteration correctly"

# Add a new language feature
python basiclang_agent.py -v --profile compiler \
  "Add support for Select Case with pattern matching"

# Review recent changes
python basiclang_agent.py -v --profile review \
  "Review the debugger for thread safety issues"

# Run and analyze tests
python basiclang_agent.py -v --profile tests \
  "Run all parser tests and identify any failures"

# Check backend parity
python basiclang_agent.py -v --profile backend \
  "Find IR nodes that are missing Visit() in the C++ backend"
```
