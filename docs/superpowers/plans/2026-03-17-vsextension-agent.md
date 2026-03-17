# VS Extension Agent Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a Claude Agent SDK application that maintains the BasicLang VS 2022 extension — VSIX build, CPS project system, LSP client, templates, pkgdef, and commands.

**Architecture:** Single Python file (`vsextension_agent.py`) following the same pattern as BasicLangAgent, IDEAgent, and EngineAgent. 7 task profiles for each VS extensibility domain. 6 custom MCP tools for building, validating, and inspecting the VSIX. System prompt encodes all the hard-won VS 2022 extension knowledge from CLAUDE.md and memory files.

**Tech Stack:** Python 3.10+, Claude Agent SDK, asyncio

---

## File Structure

| File | Responsibility |
|------|---------------|
| `VSExtensionAgent/vsextension_agent.py` | Main agent: system prompt, 7 profiles, 6 MCP tools, CLI |
| `VSExtensionAgent/CLAUDE.md` | Agent context for Claude Code sessions |
| `VSExtensionAgent/README.md` | Setup and usage docs |
| `VSExtensionAgent/requirements.txt` | `claude-agent-sdk>=0.1.0,<2.0` |
| `VSExtensionAgent/pyproject.toml` | Python project config |
| `VSExtensionAgent/.env.example` | API key template |

---

### Task 1: Create vsextension_agent.py — Imports, Paths, System Prompt

**Files:**
- Create: `VSExtensionAgent/vsextension_agent.py`

- [ ] **Step 1: Write file header, imports, and project paths**

```python
#!/usr/bin/env python3
"""
VS Extension Agent — Claude Agent SDK Application

An AI agent specialized for maintaining the BasicLang Visual Studio 2022
extension. Supports task profiles for VSIX build, CPS project system,
LSP client, templates, pkgdef, and commands.

Usage:
    python vsextension_agent.py "Fix template not appearing in New Project"
    python vsextension_agent.py --profile vsix "Rebuild VSIX with updated manifest"
    python vsextension_agent.py --profile templates "Add Game project template"
    python vsextension_agent.py --interactive
"""

import asyncio
import argparse
import os
import re
import subprocess
import sys
import zipfile
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
EXTENSION_DIR = os.path.join(
    PROJECT_ROOT, "BasicLang.VisualStudio", "src", "BasicLang.VisualStudio"
)
CSPROJ_PATH = os.path.join(EXTENSION_DIR, "BasicLang.VisualStudio.csproj")
PKGDEF_PATH = os.path.join(EXTENSION_DIR, "BasicLang.VisualStudio.pkgdef")
MANIFEST_PATH = os.path.join(EXTENSION_DIR, "source.extension.vsixmanifest")
VSIX_OUTPUT = os.path.join(
    EXTENSION_DIR, "bin", "Release", "net48", "BasicLang.VisualStudio.vsix"
)
TEMPLATES_DIR = os.path.join(EXTENSION_DIR, "Templates")
```

- [ ] **Step 2: Write the SYSTEM_PROMPT constant**

The system prompt must encode:
- Extension architecture overview (CPS, LSP, templates, pkgdef, VSCT, options)
- Key file locations within `BasicLang.VisualStudio/src/BasicLang.VisualStudio/`
- VS 2022 extension constraints:
  - `GeneratePkgDefFile=false` — manual pkgdef required, must register ALL attributes manually
  - `<ProjectType>VisualBasic</ProjectType>` in vstemplates (VS 2022 ignores custom ProjectTypes)
  - vstman `TemplateFileName` must reference `.vstemplate` inside zip, not the `.zip` filename
  - `Dependencies` = other VSIX/NDP; `Prerequisites` = VS Setup components (never mix)
  - Template zips created by custom MSBuild `CreateTemplateZips` target
  - VSIX build requires VS 2022 MSBuild (not `dotnet build`)
- Build command: `MSBuild.exe BasicLang.VisualStudio.csproj -p:Configuration=Release`
- Current version: 2.4.0
- CPS project system: BasicLangProjectFactory, capabilities, unconfigured/configured project
- LSP client: `ILanguageClient` impl, launches `BasicLang.exe --lsp`, server discovery paths
- Commands: Build, Run, ChangeBackend, RestartServer, GoToDefinition, FindReferences
- Options: General (LSP path, semantic highlighting, inlay hints) + Compiler (backend, warnings)
- Known issues: ChangeBackend is stub, GoToDefinition/FindReferences delegate to generic VS commands

Full system prompt text (~200 lines) covering all the above in structured sections.

- [ ] **Step 3: Verify file parses**

Run: `python -c "import ast; ast.parse(open('VSExtensionAgent/vsextension_agent.py').read()); print('OK')"`
Expected: OK

---

### Task 2: Task Profiles

**Files:**
- Modify: `VSExtensionAgent/vsextension_agent.py`

- [ ] **Step 1: Add TASK_PROFILES dictionary**

7 profiles:

```python
TASK_PROFILES: dict[str, dict[str, Any]] = {
    "vsix": {
        "description": "VSIX manifest, build pipeline, MSBuild targets, packaging",
        "system_prompt_suffix": "Focus on: source.extension.vsixmanifest, .csproj MSBuild targets (CreateTemplateZips, AddTemplatesToVsix, _CreateVsixAfterBuild), VSIX content structure, Dependencies vs Prerequisites, ProductArchitecture. Build requires VS 2022 MSBuild.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Analyze the VSIX build pipeline and identify any issues with packaging or manifest.",
    },
    "cps": {
        "description": "CPS project system: factory, capabilities, unconfigured/configured project",
        "system_prompt_suffix": "Focus on: ProjectSystem/ folder — BasicLangProjectFactory.cs, BasicLangProjectCapability.cs, BasicLangUnconfiguredProject.cs, BasicLangConfiguredProject.cs, BasicLangProjectTreeProvider.cs. CPS uses MEF [Export]/[Import] with [AppliesTo] attributes. Project type GUID: {95a8f3e1-1234-4567-8903-abcdef123456}.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review the CPS project system for completeness and correctness.",
    },
    "lsp": {
        "description": "LSP client, language server discovery, content types, TextMate grammar",
        "system_prompt_suffix": "Focus on: LanguageService/ folder — BasicLangLanguageClient.cs (ILanguageClient, ILanguageClientCustomMessage2), BasicLangContentType.cs, BasicLangGrammar.json. Server: BasicLang.exe --lsp on stdin/stdout. Discovery: extension dir → LocalAppData → ProgramFiles → PATH.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review the LSP client for correct server lifecycle and capability negotiation.",
    },
    "templates": {
        "description": "Project/item templates, vstemplates, vstman manifests, template zips",
        "system_prompt_suffix": "Focus on: Templates/ folder — 4 project templates (ConsoleApp, ClassLibrary, WinFormsApp, WpfApp) + 3 item templates (Class, Module, Interface). CRITICAL: <ProjectType>VisualBasic</ProjectType> in vstemplates (not BasicLang — VS ignores custom types). vstman TemplateFileName must be the .vstemplate name INSIDE the zip, not the zip filename. Template zips created by CreateTemplateZips MSBuild target.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Verify all templates are correctly configured and will appear in VS 2022 New Project dialog.",
    },
    "pkgdef": {
        "description": "Registry entries: package, menus, project factory, auto-load, templates, language service",
        "system_prompt_suffix": "Focus on: BasicLang.VisualStudio.pkgdef. Since GeneratePkgDefFile=false, ALL registrations must be manual: Package, Menus, Projects (factory), ContentTypes, Languages, AutoLoadPackages, SolutionPersistence, MSBuild SafeImports, TemplateEngine, NewProjectTemplates. Every [ProvideX] attribute needs a matching pkgdef section.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Audit pkgdef for missing registrations — cross-reference with package attributes.",
    },
    "commands": {
        "description": "VSCT commands, command handlers, options pages",
        "system_prompt_suffix": "Focus on: Commands/BasicLangCommands.vsct, Commands/CommandHandlers.cs, Options/GeneralOptionsPage.cs, Options/CompilerOptionsPage.cs. Commands: Build (0x0100), Run (0x0101), ChangeBackend (0x0102), RestartServer (0x0103), GoToDefinition (0x0104), FindReferences (0x0105). Command set GUID in Guids.cs.",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review command handlers for correctness and implement any stub commands.",
    },
    "review": {
        "description": "Read-only code review for VS 2022 compatibility issues",
        "system_prompt_suffix": "Read-only review mode. Check for: incorrect ProjectType in vstemplates, missing pkgdef entries, manifest issues, threading violations (missing ThrowIfNotOnUIThread), deprecated API usage, missing async patterns, content type mismatches.",
        "allowed_tools": ["Read", "Glob", "Grep"],
        "default_prompt": "Review the entire extension for VS 2022 compatibility issues, missing registrations, and common VSIX pitfalls.",
    },
}
```

- [ ] **Step 2: Verify file parses**

Run: `python -c "import ast; ast.parse(open('VSExtensionAgent/vsextension_agent.py').read()); print('OK')"`
Expected: OK

---

### Task 3: Custom MCP Tools

**Files:**
- Modify: `VSExtensionAgent/vsextension_agent.py`

- [ ] **Step 1: Add `_find_msbuild()` helper**

Same pattern as EngineAgent — uses `vswhere.exe` to find MSBuild.exe, falls back to known VS paths (Enterprise, Professional, Community).

- [ ] **Step 2: Add `_text_result()` helper**

```python
def _text_result(text: str) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": text}]}
```

- [ ] **Step 3: Add `build_vsix` tool**

```python
@tool(
    "build_vsix",
    "Build the BasicLang VS 2022 extension VSIX via MSBuild",
    {
        "type": "object",
        "properties": {
            "configuration": {
                "type": "string",
                "description": "Build configuration (Debug or Release)",
                "default": "Release",
            }
        },
    },
)
```
- Find MSBuild via `_find_msbuild()`
- Run: `MSBuild.exe BasicLang.VisualStudio.csproj -p:Configuration={config}`
- Return build output, check for VSIX file existence after build

- [ ] **Step 4: Add `validate_pkgdef` tool**

```python
@tool(
    "validate_pkgdef",
    "Parse pkgdef file for syntax errors and check for missing registrations",
    {"type": "object", "properties": {}},
)
```
- Read pkgdef file
- Check for: balanced brackets in registry keys, proper string quoting, DWORD format
- Cross-reference expected sections: Packages, Menus, Projects, ContentTypes, Languages, AutoLoadPackages, TemplateEngine
- Report any missing sections

- [ ] **Step 5: Add `check_vsix_contents` tool**

```python
@tool(
    "check_vsix_contents",
    "List all files inside the built VSIX zip archive",
    {"type": "object", "properties": {}},
)
```
- Open VSIX_OUTPUT as zipfile
- List all entries with sizes
- Flag missing expected files (DLL, pkgdef, templates, vstman, grammar, targets)

- [ ] **Step 6: Add `check_templates` tool**

```python
@tool(
    "check_templates",
    "Verify template source files and zips contain correct vstemplate files",
    {"type": "object", "properties": {}},
)
```
- Scan Templates/Projects/* and Templates/Items/* for vstemplate files
- Check each vstemplate for `<ProjectType>` value (should be VisualBasic)
- Check vstman files for correct `TemplateFileName` entries
- If built zips exist, verify they contain the vstemplate

- [ ] **Step 7: Add `get_extension_architecture` tool**

```python
@tool(
    "get_extension_architecture",
    "Return the VS extension architecture overview",
    {"type": "object", "properties": {}},
)
```
- Return static architecture text covering: package, CPS, LSP, templates, commands, options, pkgdef, build pipeline

- [ ] **Step 8: Add `check_manifest` tool**

```python
@tool(
    "check_manifest",
    "Validate source.extension.vsixmanifest for common issues",
    {"type": "object", "properties": {}},
)
```
- Parse the XML manifest
- Check: Identity version, Prerequisites vs Dependencies (no VS Setup components in Dependencies), ProductArchitecture, Assets entries, InstallationTarget version range

- [ ] **Step 9: Verify file parses**

Run: `python -c "import ast; ast.parse(open('VSExtensionAgent/vsextension_agent.py').read()); print('OK')"`
Expected: OK

---

### Task 4: MCP Server, Agent Runner, Interactive Mode, CLI

**Files:**
- Modify: `VSExtensionAgent/vsextension_agent.py`

- [ ] **Step 1: Add MCP server creation**

```python
vsext_mcp_server = create_sdk_mcp_server(
    name="vsext-tools",
    version="1.0.0",
    tools=[build_vsix, validate_pkgdef, check_vsix_contents,
           check_templates, get_extension_architecture, check_manifest],
)
```

- [ ] **Step 2: Add `run_agent()` function**

Same pattern as EngineAgent:
- Build system prompt from base + profile suffix
- Build allowed_tools from profile + custom MCP tool names (`mcp__vsext-tools__*`)
- Create `ClaudeAgentOptions` with `permission_mode="acceptEdits"`, `cwd=PROJECT_ROOT`
- Iterate `query()` messages, handle AssistantMessage/ResultMessage/SystemMessage

- [ ] **Step 3: Add `interactive_mode()` function**

Same REPL pattern: prompt loop, `profile <name>` to switch, `profiles` to list, `quit` to exit.

- [ ] **Step 4: Add `main_cli()` with argparse**

Arguments: `prompt` (positional, optional), `--profile`, `--interactive`, `--list-profiles`, `-v/--verbose`

- [ ] **Step 5: Add `if __name__ == "__main__"` block**

- [ ] **Step 6: Verify full file parses and --help works**

Run: `python -c "import ast; ast.parse(open('VSExtensionAgent/vsextension_agent.py').read()); print('OK')"`
Run: `python VSExtensionAgent/vsextension_agent.py --help`
Expected: Usage info with all arguments listed

Run: `python VSExtensionAgent/vsextension_agent.py --list-profiles`
Expected: 7 profiles listed (vsix, cps, lsp, templates, pkgdef, commands, review)

---

### Task 5: Supporting Files

**Files:**
- Create: `VSExtensionAgent/CLAUDE.md`
- Create: `VSExtensionAgent/README.md`
- Create: `VSExtensionAgent/requirements.txt`
- Create: `VSExtensionAgent/pyproject.toml`
- Create: `VSExtensionAgent/.env.example`

- [ ] **Step 1: Write CLAUDE.md**

Agent context: what it is, how to run, project context (BasicLang.VisualStudio extension), key conventions (MSBuild not dotnet, VisualBasic ProjectType, manual pkgdef).

- [ ] **Step 2: Write README.md**

Setup (Python 3.10+, VS 2022, .NET 8 SDK), install (venv + pip), usage (one-shot, profiles table, interactive, MCP tools table), architecture diagram, examples.

- [ ] **Step 3: Write requirements.txt**

```
claude-agent-sdk>=0.1.0,<2.0
```

- [ ] **Step 4: Write pyproject.toml**

```toml
[build-system]
requires = ["setuptools>=68.0", "wheel"]
build-backend = "setuptools.build_meta"

[project]
name = "vsextension-agent"
version = "1.0.0"
description = "Claude Agent SDK application for the BasicLang VS 2022 extension"
requires-python = ">=3.10"
dependencies = ["claude-agent-sdk>=0.1.0,<2.0"]

[project.optional-dependencies]
dev = ["pytest>=7.0", "pytest-asyncio>=0.21"]

[project.scripts]
vsextension-agent = "vsextension_agent:main_cli"
```

- [ ] **Step 5: Write .env.example**

```
ANTHROPIC_API_KEY=your_api_key_here
```

---

### Task 6: SDK Verification

**Files:**
- Possibly modify: `VSExtensionAgent/vsextension_agent.py` (fixes from verifier)

- [ ] **Step 1: Run SDK verifier agent**

Use `agent-sdk-dev:agent-sdk-verifier-py` to verify the agent follows SDK best practices.

- [ ] **Step 2: Fix any issues reported by verifier**

Common fixes from previous agents:
- Empty `{}` input schemas → `{"type": "object", "properties": {}}`
- Missing `"required"` arrays in tool schemas
- `message.subtype == "success"` vs `not message.is_error`

- [ ] **Step 3: Final parse check**

Run: `python -c "import ast; ast.parse(open('VSExtensionAgent/vsextension_agent.py').read()); print('OK')"`
Expected: OK
