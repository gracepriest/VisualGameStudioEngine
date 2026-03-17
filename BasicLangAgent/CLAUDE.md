# BasicLang Agent — Claude Code Instructions

This directory contains a Claude Agent SDK application for the BasicLang compiler.

## What This Is

A Python application that uses the Claude Agent SDK to create an AI agent specialized for working with the BasicLang compiler and Visual Game Studio IDE codebase. The agent has:

- **Task profiles** for different workflows (compiler, debugger, LSP, tests, review, backend)
- **Custom MCP tools** for compiling, testing, and building
- **Interactive mode** for conversational development sessions

## Running the Agent

```bash
# Install dependencies
pip install -r requirements.txt

# One-shot task
python basiclang_agent.py "Fix the parser bug in For loops"

# With a task profile
python basiclang_agent.py --profile compiler "Add Do...Loop support"

# Interactive mode
python basiclang_agent.py --interactive

# List profiles
python basiclang_agent.py --list-profiles
```

## Project Context

The agent operates on the parent directory (VisualGameStudioEngine), which contains:
- **BasicLang/** — Full compiler (lexer, parser, semantic analyzer, IR builder, 4 backends)
- **VisualGameStudio.Editor/** — Avalonia-based code editor
- **VisualGameStudio.Shell/** — IDE application shell
- **VisualGameStudio.Tests/** — 1636 xUnit tests
- **BasicLang/Debugger/** — DAP-based debugger
- **BasicLang/LSP/** — Language Server Protocol server

## Key Conventions

- Source files use `.bas` extension (NOT `.bl`)
- LSP server: `BasicLang.exe --lsp`
- All 4 backends must be updated together (C#, LLVM, MSIL, C++)
- Tests must pass after changes: `dotnet test`
