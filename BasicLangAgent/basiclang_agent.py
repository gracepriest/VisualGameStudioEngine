#!/usr/bin/env python3
"""
BasicLang Compiler Agent — Claude Agent SDK Application

An AI agent specialized for working with the BasicLang compiler codebase.
Supports task profiles for different development workflows: compiler work,
debugging, LSP, testing, code review, and backend code generation.

Usage:
    python basiclang_agent.py "Fix the parser bug in For loops"
    python basiclang_agent.py --profile compiler "Add support for Do...Loop"
    python basiclang_agent.py --profile debugger "Fix step-over not working"
    python basiclang_agent.py --profile review
    python basiclang_agent.py --interactive
"""

import asyncio
import argparse
import os
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
BASICLANG_DIR = os.path.join(PROJECT_ROOT, "BasicLang")
IDE_DIR = os.path.join(PROJECT_ROOT, "IDE")
TESTS_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.Tests")

# ---------------------------------------------------------------------------
# System prompt — injected into every agent invocation
# ---------------------------------------------------------------------------
SYSTEM_PROMPT = """\
You are an expert developer working on the BasicLang compiler and Visual Game Studio IDE.

## Project Overview
BasicLang is a VB-like programming language with a full compiler pipeline:
  Lexer -> Parser -> SemanticAnalyzer -> IRBuilder -> Optimizer -> Backend CodeGen

The compiler supports multiple backends: C#, LLVM, MSIL, and C++.

## Key Source Locations
- Compiler: BasicLang/ (Lexer.cs, Parser.cs, SemanticAnalyzer.cs, IRBuilder.cs)
- Backends: BasicLang/ (CSharpBackend.cs, LLVMBackend.cs, MSILBackend.cs, CppBackend.cs)
- IR Nodes: BasicLang/IRNodes.cs, BasicLang/ASTNodes.cs
- LSP Server: BasicLang/LSP/ (BasicLangLanguageServer.cs, CompletionService.cs)
- Debugger: BasicLang/Debugger/ (DebugSession.cs, DebuggableInterpreter.cs)
- IDE Editor: VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs
- IDE Shell: VisualGameStudio.Shell/ (ViewModels, Views)
- Tests: VisualGameStudio.Tests/

## Build Commands
- Build compiler: dotnet build BasicLang/BasicLang.csproj -c Release
- Build IDE: dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
- Run tests: dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
- Compile a file: BasicLang.exe compile MyFile.bas --target=csharp

## BasicLang Syntax
- VB-like: Dim, Sub, Function, Class, Module, Interface
- For loops: For i As Integer = 1 To 10 Step 2
- ForEach: For Each item In collection
- Pattern matching with When guards
- LINQ, Async/Await, Generics
- .NET interop via Using directive
- Preprocessor: #IfDef / #IfNDef / #Else / #EndIf

## Coding Standards
- Follow existing C# patterns in the codebase
- All new features need corresponding IR nodes in IRNodes.cs
- Backend changes must be applied to ALL backends (C#, LLVM, MSIL, C++)
- Run tests after changes: dotnet test
- Keep backward compatibility with existing .bas files
"""

# ---------------------------------------------------------------------------
# Task profiles — pre-configured agent configurations for common workflows
# ---------------------------------------------------------------------------
TASK_PROFILES: dict[str, dict[str, Any]] = {
    "compiler": {
        "description": "Compiler pipeline work (lexer, parser, semantic analysis, IR, codegen)",
        "system_prompt_suffix": """
Focus on the compiler pipeline. When implementing new language features:
1. Add tokens to Lexer.cs if needed
2. Add AST nodes to ASTNodes.cs
3. Update Parser.cs to parse the new syntax
4. Add semantic checks in SemanticAnalyzer.cs
5. Create IR nodes in IRNodes.cs
6. Update IRBuilder.cs to generate IR
7. Implement code generation in ALL backends
8. Add tests in VisualGameStudio.Tests/
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep", "Agent"],
        "default_prompt": "Analyze the compiler pipeline and suggest improvements.",
    },
    "debugger": {
        "description": "Debugger and DAP (Debug Adapter Protocol) work",
        "system_prompt_suffix": """
Focus on the debugger subsystem:
- DebugSession.cs implements the DAP server (handles DAP JSON commands)
- DebuggableInterpreter.cs is the runtime that executes BasicLang IR with debug support
- DebugService.cs in ProjectSystem bridges the IDE to the DAP session
- IDebugService.cs defines the debug interface

DAP flow: IDE -> DebugService -> stdin/stdout JSON -> DebugSession -> DebuggableInterpreter
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review the debugger for correctness and missing DAP features.",
    },
    "lsp": {
        "description": "LSP (Language Server Protocol) server work",
        "system_prompt_suffix": """
Focus on the LSP server in BasicLang/LSP/:
- BasicLangLanguageServer.cs: main server, handles JSON-RPC
- CompletionService.cs: completion provider
- The server is launched with: BasicLang.exe --lsp
- IDE connects via stdin/stdout

Key LSP features: completion, hover, diagnostics, go-to-definition,
signature help, document highlights, formatting, rename, code actions.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Audit LSP server for missing features and protocol compliance.",
    },
    "tests": {
        "description": "Test suite work: writing, fixing, and running tests",
        "system_prompt_suffix": """
Focus on the test suite in VisualGameStudio.Tests/.
- Tests use xUnit framework
- Current count: 1636 tests, all passing
- Run with: dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
- Test naming: MethodName_Scenario_ExpectedResult
- Integration tests hit real compiler pipeline (no mocking the compiler)
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Run the test suite and report results. Identify gaps in test coverage.",
    },
    "review": {
        "description": "Code review: find bugs, security issues, and quality problems",
        "system_prompt_suffix": """
Perform a thorough code review. Look for:
- Logic errors and edge cases
- Null reference risks
- Resource leaks (streams, processes not disposed)
- Thread safety issues
- Missing error handling at system boundaries
- Performance bottlenecks
Do NOT suggest style-only changes. Focus on real bugs.
""",
        "allowed_tools": ["Read", "Glob", "Grep"],
        "default_prompt": "Review recent changes for bugs and correctness issues.",
    },
    "backend": {
        "description": "Backend code generation (C#, LLVM, MSIL, C++)",
        "system_prompt_suffix": """
Focus on backend code generation. Each backend visits IR nodes and emits target code:
- CSharpBackend.cs: generates C# source code
- LLVMBackend.cs: generates LLVM IR text
- MSILBackend.cs: generates .NET IL via System.Reflection.Emit
- CppBackend.cs: generates C++ source code

When adding new IR node support, implement Visit() in ALL four backends.
Test each backend: BasicLang.exe compile test.bas --target=csharp|llvm|msil|cpp
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Check all backends for missing IR node implementations.",
    },
}

# ---------------------------------------------------------------------------
# Custom tools — project-specific operations exposed via MCP
# ---------------------------------------------------------------------------

@tool(
    "compile_basiclang",
    "Compile a BasicLang source file with the specified backend",
    {
        "type": "object",
        "properties": {
            "file_path": {"type": "string", "description": "Path to the .bas file"},
            "backend": {"type": "string", "description": "Target backend: csharp, llvm, msil, cpp"},
        },
        "required": ["file_path"],
    },
)
async def compile_basiclang(args: dict[str, Any]) -> dict[str, Any]:
    """Compile a .bas file using the BasicLang compiler."""
    file_path = args["file_path"]
    backend = args.get("backend", "csharp")
    compiler = os.path.join(IDE_DIR, "BasicLang.exe")

    if not os.path.exists(compiler):
        return _text_result(f"Compiler not found at {compiler}")

    proc = await asyncio.create_subprocess_exec(
        compiler, "compile", file_path, f"--target={backend}",
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await proc.communicate()
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\nOutput:\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "run_tests",
    "Run the BasicLang / VGS test suite and return results",
    {
        "type": "object",
        "properties": {
            "filter": {"type": "string", "description": "Optional test filter expression"},
        },
    },
)
async def run_tests(args: dict[str, Any]) -> dict[str, Any]:
    """Run the project test suite."""
    cmd = [
        "dotnet", "test",
        os.path.join(TESTS_DIR, "VisualGameStudio.Tests.csproj"),
        "-c", "Release", "--no-build", "--verbosity", "minimal",
    ]
    test_filter = args.get("filter", "")
    if test_filter:
        cmd.extend(["--filter", test_filter])

    proc = await asyncio.create_subprocess_exec(
        *cmd,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await proc.communicate()
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "build_project",
    "Build a .NET project in the solution",
    {
        "type": "object",
        "properties": {
            "project": {"type": "string", "description": "Project name or path to .csproj"},
            "configuration": {"type": "string", "description": "Build configuration (default: Release)"},
        },
        "required": ["project"],
    },
)
async def build_project(args: dict[str, Any]) -> dict[str, Any]:
    """Build a specific project."""
    project = args["project"]
    config = args.get("configuration", "Release")

    if not project.endswith(".csproj"):
        project = os.path.join(PROJECT_ROOT, project, f"{os.path.basename(project)}.csproj")
    elif not os.path.isabs(project):
        project = os.path.join(PROJECT_ROOT, project)

    proc = await asyncio.create_subprocess_exec(
        "dotnet", "build", project, "-c", config,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await proc.communicate()
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "get_compiler_pipeline",
    "Get an overview of the compiler pipeline stages and key classes",
    {"type": "object", "properties": {}},
)
async def get_compiler_pipeline(args: dict[str, Any]) -> dict[str, Any]:
    """Return a summary of the compiler pipeline."""
    return _text_result(
        "BasicLang Compiler Pipeline:\n\n"
        "1. LEXER (Lexer.cs)\n"
        "   Input: source text -> Output: Token stream\n"
        "   Key: TokenType enum, Tokenize() method\n\n"
        "2. PARSER (Parser.cs)\n"
        "   Input: Token stream -> Output: AST (ASTNodes.cs)\n"
        "   Key: ParseProgram(), recursive descent\n\n"
        "3. SEMANTIC ANALYZER (SemanticAnalyzer.cs)\n"
        "   Input: AST -> Output: validated AST with type info\n"
        "   Key: Two-pass (RegisterDeclarations + Analyze), Symbol table\n\n"
        "4. IR BUILDER (IRBuilder.cs)\n"
        "   Input: typed AST -> Output: IR (IRNodes.cs)\n"
        "   Key: Visit pattern, IRProgram root node\n\n"
        "5. OPTIMIZER (Optimizer.cs)\n"
        "   Input: IR -> Output: optimized IR\n"
        "   Key: Constant folding, dead code elimination\n\n"
        "6. BACKENDS\n"
        "   - CSharpBackend.cs  -> C# source code\n"
        "   - LLVMBackend.cs    -> LLVM IR text\n"
        "   - MSILBackend.cs    -> .NET IL (Reflection.Emit)\n"
        "   - CppBackend.cs     -> C++ source code\n"
    )


def _text_result(text: str) -> dict[str, Any]:
    """Helper to create a text content result."""
    return {"content": [{"type": "text", "text": text}]}


# ---------------------------------------------------------------------------
# MCP server with all custom tools
# ---------------------------------------------------------------------------
basiclang_mcp_server = create_sdk_mcp_server(
    name="basiclang-tools",
    version="1.0.0",
    tools=[compile_basiclang, run_tests, build_project, get_compiler_pipeline],
)

# ---------------------------------------------------------------------------
# Agent runner
# ---------------------------------------------------------------------------

async def run_agent(
    prompt: str,
    profile_name: str | None = None,
    verbose: bool = False,
) -> str:
    """Run the BasicLang agent with the given prompt and optional task profile."""

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
        "mcp__basiclang-tools__compile_basiclang",
        "mcp__basiclang-tools__run_tests",
        "mcp__basiclang-tools__build_project",
        "mcp__basiclang-tools__get_compiler_pipeline",
    ]
    allowed_tools.extend(custom_tool_names)

    options = ClaudeAgentOptions(
        system_prompt=system,
        allowed_tools=allowed_tools,
        permission_mode="acceptEdits",
        cwd=PROJECT_ROOT,
        mcp_servers={
            "basiclang-tools": basiclang_mcp_server,
        },
    )

    result_text = ""

    async for message in query(prompt=prompt, options=options):
        if isinstance(message, AssistantMessage):
            for block in message.content:
                block_type = getattr(block, "type", None)
                if block_type == "text":
                    if verbose:
                        print(f"Agent: {block.text}")
                elif block_type == "tool_use":
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
    print("BasicLang Agent -- Interactive Mode")
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
        description="BasicLang Compiler Agent -- Claude Agent SDK",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Task Profiles:
  compiler    Compiler pipeline work (lexer, parser, IR, codegen)
  debugger    Debugger and DAP protocol work
  lsp         LSP server features and protocol
  tests       Test suite: writing, fixing, running
  review      Code review for bugs and quality
  backend     Backend code generation (C#, LLVM, MSIL, C++)

Examples:
  %(prog)s "Fix the parser bug in For loops"
  %(prog)s --profile compiler "Add Do...Loop support"
  %(prog)s --profile debugger "Fix step-over"
  %(prog)s --profile review
  %(prog)s --interactive
  %(prog)s --interactive --profile compiler
""",
    )

    parser.add_argument(
        "prompt",
        nargs="?",
        default="",
        help="Task description for the agent",
    )
    parser.add_argument(
        "--profile", "-p",
        choices=list(TASK_PROFILES.keys()),
        help="Task profile to use",
    )
    parser.add_argument(
        "--interactive", "-i",
        action="store_true",
        help="Run in interactive mode",
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show agent reasoning and tool calls",
    )
    parser.add_argument(
        "--list-profiles",
        action="store_true",
        help="List available task profiles",
    )

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
        args.prompt,
        profile_name=args.profile,
        verbose=args.verbose,
    ))

    if result and not args.verbose:
        print(result)


if __name__ == "__main__":
    main_cli()
