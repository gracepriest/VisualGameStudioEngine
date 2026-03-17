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
from xml.etree import ElementTree

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

# ---------------------------------------------------------------------------
# System prompt — deep VS extension knowledge
# ---------------------------------------------------------------------------
SYSTEM_PROMPT = """\
You are an expert Visual Studio 2022 extension developer specializing in the
BasicLang language extension. You maintain a CPS-based VSIX extension that
provides first-class language support for BasicLang (.bas/.bl files) in VS 2022.

## Extension Architecture

```
BasicLang.VisualStudio/src/BasicLang.VisualStudio/
├── Package/
│   ├── BasicLangPackage.cs        — AsyncPackage, auto-load, registers factory
│   └── Guids.cs                   — All GUIDs (package, commands, project type)
├── ProjectSystem/
│   ├── BasicLangProjectFactory.cs — Flavored project factory for .blproj
│   ├── BasicLangProjectCapability.cs — CPS capability export
│   ├── BasicLangUnconfiguredProject.cs — MEF unconfigured project
│   ├── BasicLangConfiguredProject.cs   — Configuration services
│   └── BasicLangProjectTreeProvider.cs — Solution Explorer icons
├── LanguageService/
│   ├── BasicLangLanguageClient.cs — ILanguageClient, launches BasicLang.exe --lsp
│   ├── BasicLangContentType.cs    — Content type definitions (.bas, .bl, .blproj)
│   └── BasicLangGrammar.json      — TextMate grammar for syntax highlighting
├── Commands/
│   ├── BasicLangCommands.vsct     — Menu/command definitions (XML)
│   └── CommandHandlers.cs         — Build, Run, ChangeBackend, RestartServer, etc.
├── Options/
│   ├── GeneralOptionsPage.cs      — LSP path, semantic highlighting, inlay hints
│   └── CompilerOptionsPage.cs     — Backend, warnings, optimizations
├── Templates/
│   ├── Projects/                  — 4 project templates (ConsoleApp, ClassLibrary, WinFormsApp, WpfApp)
│   ├── Items/                     — 3 item templates (Class, Module, Interface)
│   ├── BasicLang.ProjectTemplates.vstman — Template manifest for projects
│   └── BasicLang.ItemTemplates.vstman    — Template manifest for items
├── BuildSystem/
│   └── BasicLang.targets          — MSBuild targets for BasicLang compilation
├── BasicLang.VisualStudio.csproj  — SDK-style project with VSSDK
├── BasicLang.VisualStudio.pkgdef  — Manual registry entries
└── source.extension.vsixmanifest  — VSIX manifest (v2.4.0)
```

## Key GUIDs

- Package: {95a8f3e1-1234-4567-8901-abcdef123456}
- Command Set: {95a8f3e1-1234-4567-8902-abcdef123456}
- Project Type: {95a8f3e1-1234-4567-8903-abcdef123456}
- Language Service: {95a8f3e1-1234-4567-8906-abcdef123456}

## CRITICAL VS 2022 Extension Rules

### 1. Template ProjectType
VS 2022 ONLY shows templates with built-in ProjectType values in the New Project
dialog. Custom values like "BasicLang" are silently ignored. You MUST use:
```xml
<ProjectType>VisualBasic</ProjectType>
<LanguageTag>visualbasic</LanguageTag>
```
Templates appear under Visual Basic, searchable by "BasicLang".

### 2. Manual pkgdef (GeneratePkgDefFile=false)
Since auto-generation failed with SDK-style projects, ALL registrations are manual.
Every [ProvideX] attribute on the package class needs a matching pkgdef section:
- [PackageRegistration] → [$RootKey$\\Packages\\{guid}]
- [ProvideMenuResource] → [$RootKey$\\Menus]
- [ProvideAutoLoad] → [$RootKey$\\AutoLoadPackages\\{context-guid}]
- [ProvideProjectFactory] → [$RootKey$\\Projects\\{guid}]
- [ProvideOptionPage] → Auto-handled by VS, but needs package registration

### 3. vstman TemplateFileName
The vstman (template manifest) TemplateFileName must reference the .vstemplate
file name INSIDE the template zip, NOT the zip filename itself:
- CORRECT: <TemplateFileName>ConsoleApp.vstemplate</TemplateFileName>
- WRONG: <TemplateFileName>ConsoleApp.zip</TemplateFileName>

### 4. VSIX Dependencies vs Prerequisites
- Dependencies = Other VSIX extensions or .NET Framework requirements
- Prerequisites = VS Setup components (CoreEditor, Roslyn, etc.)
- NEVER put VS Setup component IDs (like CoreEditor) in Dependencies

### 5. Build Requirements
The VSIX must be built with VS 2022 MSBuild, NOT dotnet build:
```
"C:\\Program Files\\Microsoft Visual Studio\\2022\\Enterprise\\MSBuild\\Current\\Bin\\MSBuild.exe" ^
    BasicLang.VisualStudio.csproj -p:Configuration=Release
```
The csproj imports Microsoft.VsSDK.targets and uses custom MSBuild targets:
- CreateTemplateZips: Creates template zip files via PowerShell Compress-Archive
- AddTemplateZipsToVsix: Adds template zips as VSIXSourceItems
- AddTemplatesToVsix: Post-build injection of templates into VSIX via ZipFile API
- _CreateVsixAfterBuild: Ensures VSIX is created after build

### 6. Template Zip Creation
Template zips are NOT created by VSSDK template processing (DeployVSTemplates=false).
Instead, custom MSBuild targets use PowerShell Compress-Archive to zip each template
folder, then inject the zips into the VSIX.

### 7. CPS Project System
Uses MEF [Export]/[Import] with [AppliesTo("BasicLang")] attributes.
- BasicLangProjectFactory: Registered via [ProvideProjectFactory] + pkgdef
- Project capabilities flow from the .blproj file through CPS
- Solution Explorer icons via BasicLangProjectTreeProvider

### 8. LSP Client
Implements ILanguageClient and ILanguageClientCustomMessage2.
Server discovery order:
1. Extension directory (bundled)
2. %LOCALAPPDATA%\\BasicLang\\BasicLang.exe
3. %ProgramFiles%\\BasicLang\\BasicLang.exe
4. Development build paths (..\\..\\BasicLang\\bin\\)
5. IDE\\ folder
6. PATH environment variable

Launches: BasicLang.exe --lsp on stdin/stdout.
Capabilities: completions, hover, diagnostics, semantic highlighting,
inlay hints, code lens, signature help.

### 9. Commands (VSCT)
6 commands in BasicLang menu:
- Build (0x0100): Finds .blproj, runs BasicLang.exe build
- Run (0x0101): Build + launch output .exe
- ChangeBackend (0x0102): STUB — shows message box with backend list
- RestartServer (0x0103): Stops and restarts LSP server
- GoToDefinition (0x0104): Delegates to Edit.GoToDefinition
- FindReferences (0x0105): Delegates to Edit.FindAllReferences
Commands enabled only when active document is .bas/.bl/.blproj.

### 10. Options Pages
Tools → Options → BasicLang:
- General: Auto-start LSP, LSP path, semantic highlighting, inlay hints,
  code lens, diagnostics, log level
- Compiler: Backend selection, framework version, warnings, optimizations

## BasicLang Compiler Integration

- Source files: .bas extension (NOT .bl)
- Project files: .blproj
- LSP server: BasicLang.exe --lsp
- Compile: BasicLang.exe compile MyFile.bas --target=csharp
- Build project: BasicLang.exe build MyProject.blproj
- 4 backends: CSharp, MSIL, LLVM, CPlusPlus

## Current Version: 2.4.0

## Known Issues to Fix
- ChangeBackend command just shows a message box (no actual switching)
- GoToDefinition/FindReferences delegate to generic VS commands, not LSP
- Debug launch provider not implemented (CPS debug APIs not publicly available)
- BasicLang.exe not bundled in extension (relies on external install)
"""

# ---------------------------------------------------------------------------
# Task profiles
# ---------------------------------------------------------------------------
TASK_PROFILES: dict[str, dict[str, Any]] = {
    "vsix": {
        "description": "VSIX manifest, build pipeline, MSBuild targets, packaging",
        "system_prompt_suffix": (
            "\n\n## Active Profile: VSIX Build\n"
            "Focus on: source.extension.vsixmanifest, .csproj MSBuild targets "
            "(CreateTemplateZips, AddTemplateZipsToVsix, AddTemplatesToVsix, "
            "_CreateVsixAfterBuild), VSIX content structure, Dependencies vs "
            "Prerequisites, ProductArchitecture. Build requires VS 2022 MSBuild."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Analyze the VSIX build pipeline and identify any issues "
            "with packaging or manifest."
        ),
    },
    "cps": {
        "description": "CPS project system: factory, capabilities, unconfigured/configured project",
        "system_prompt_suffix": (
            "\n\n## Active Profile: CPS Project System\n"
            "Focus on: ProjectSystem/ folder — BasicLangProjectFactory.cs, "
            "BasicLangProjectCapability.cs, BasicLangUnconfiguredProject.cs, "
            "BasicLangConfiguredProject.cs, BasicLangProjectTreeProvider.cs. "
            "CPS uses MEF [Export]/[Import] with [AppliesTo] attributes. "
            "Project type GUID: {95a8f3e1-1234-4567-8903-abcdef123456}."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Review the CPS project system for completeness and correctness."
        ),
    },
    "lsp": {
        "description": "LSP client, language server discovery, content types, TextMate grammar",
        "system_prompt_suffix": (
            "\n\n## Active Profile: LSP Client\n"
            "Focus on: LanguageService/ folder — BasicLangLanguageClient.cs "
            "(ILanguageClient, ILanguageClientCustomMessage2), "
            "BasicLangContentType.cs, BasicLangGrammar.json. "
            "Server: BasicLang.exe --lsp on stdin/stdout. "
            "Discovery: extension dir → LocalAppData → ProgramFiles → PATH."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Review the LSP client for correct server lifecycle "
            "and capability negotiation."
        ),
    },
    "templates": {
        "description": "Project/item templates, vstemplates, vstman manifests, template zips",
        "system_prompt_suffix": (
            "\n\n## Active Profile: Templates\n"
            "Focus on: Templates/ folder — 4 project templates (ConsoleApp, "
            "ClassLibrary, WinFormsApp, WpfApp) + 3 item templates (Class, "
            "Module, Interface). CRITICAL: <ProjectType>VisualBasic</ProjectType> "
            "in vstemplates (not BasicLang — VS ignores custom types). vstman "
            "TemplateFileName must be the .vstemplate name INSIDE the zip, not "
            "the zip filename. Template zips created by CreateTemplateZips "
            "MSBuild target."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Verify all templates are correctly configured and will "
            "appear in VS 2022 New Project dialog."
        ),
    },
    "pkgdef": {
        "description": "Registry entries: package, menus, project factory, auto-load, templates, language service",
        "system_prompt_suffix": (
            "\n\n## Active Profile: pkgdef Registry\n"
            "Focus on: BasicLang.VisualStudio.pkgdef. Since "
            "GeneratePkgDefFile=false, ALL registrations must be manual: "
            "Package, Menus, Projects (factory), ContentTypes, Languages, "
            "AutoLoadPackages, SolutionPersistence, MSBuild SafeImports, "
            "TemplateEngine, NewProjectTemplates. Every [ProvideX] attribute "
            "needs a matching pkgdef section."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Audit pkgdef for missing registrations — cross-reference "
            "with package attributes."
        ),
    },
    "commands": {
        "description": "VSCT commands, command handlers, options pages",
        "system_prompt_suffix": (
            "\n\n## Active Profile: Commands & Options\n"
            "Focus on: Commands/BasicLangCommands.vsct, "
            "Commands/CommandHandlers.cs, Options/GeneralOptionsPage.cs, "
            "Options/CompilerOptionsPage.cs. Commands: Build (0x0100), "
            "Run (0x0101), ChangeBackend (0x0102), RestartServer (0x0103), "
            "GoToDefinition (0x0104), FindReferences (0x0105). "
            "Command set GUID in Guids.cs."
        ),
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": (
            "Review command handlers for correctness and implement "
            "any stub commands."
        ),
    },
    "review": {
        "description": "Read-only code review for VS 2022 compatibility issues",
        "system_prompt_suffix": (
            "\n\n## Active Profile: Review (Read-Only)\n"
            "Read-only review mode. Check for: incorrect ProjectType in "
            "vstemplates, missing pkgdef entries, manifest issues, threading "
            "violations (missing ThrowIfNotOnUIThread), deprecated API usage, "
            "missing async patterns, content type mismatches."
        ),
        "allowed_tools": ["Read", "Glob", "Grep"],
        "default_prompt": (
            "Review the entire extension for VS 2022 compatibility issues, "
            "missing registrations, and common VSIX pitfalls."
        ),
    },
}


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _find_msbuild() -> str | None:
    """Find MSBuild.exe via vswhere or known VS installation paths."""
    # Try vswhere first
    vswhere_paths = [
        os.path.join(
            os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
            "Microsoft Visual Studio", "Installer", "vswhere.exe",
        ),
        os.path.join(
            os.environ.get("ProgramFiles", r"C:\Program Files"),
            "Microsoft Visual Studio", "Installer", "vswhere.exe",
        ),
    ]

    for vswhere in vswhere_paths:
        if os.path.exists(vswhere):
            try:
                result = subprocess.run(
                    [vswhere, "-latest", "-requires",
                     "Microsoft.Component.MSBuild", "-find",
                     r"MSBuild\**\Bin\MSBuild.exe"],
                    capture_output=True, text=True, timeout=10,
                )
                if result.returncode == 0 and result.stdout.strip():
                    path = result.stdout.strip().splitlines()[0]
                    if os.path.exists(path):
                        return path
            except (subprocess.TimeoutExpired, FileNotFoundError):
                pass

    # Fallback to known paths
    known_paths = [
        r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    ]
    for path in known_paths:
        if os.path.exists(path):
            return path

    return None


def _text_result(text: str) -> dict[str, Any]:
    """Helper to create a text content result."""
    return {"content": [{"type": "text", "text": text}]}


# ---------------------------------------------------------------------------
# Custom MCP tools
# ---------------------------------------------------------------------------

@tool(
    "build_vsix",
    "Build the BasicLang VS 2022 extension VSIX via MSBuild",
    {
        "type": "object",
        "properties": {
            "configuration": {
                "type": "string",
                "description": "Build configuration: Debug or Release",
                "default": "Release",
            },
        },
    },
)
async def build_vsix(args: dict[str, Any]) -> dict[str, Any]:
    """Build the VSIX extension package."""
    config = args.get("configuration", "Release")

    msbuild = _find_msbuild()
    if not msbuild:
        return _text_result(
            "MSBuild.exe not found. Install Visual Studio 2022 with "
            "the 'Visual Studio extension development' workload."
        )

    if not os.path.exists(CSPROJ_PATH):
        return _text_result(f"Project file not found: {CSPROJ_PATH}")

    try:
        proc = await asyncio.create_subprocess_exec(
            msbuild, CSPROJ_PATH,
            f"-p:Configuration={config}",
            "-restore",
            "-verbosity:minimal",
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.STDOUT,
            cwd=EXTENSION_DIR,
        )
        stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=300)
        output = stdout.decode("utf-8", errors="replace")

        vsix_path = os.path.join(
            EXTENSION_DIR, "bin", config, "net48",
            "BasicLang.VisualStudio.vsix",
        )
        vsix_exists = os.path.exists(vsix_path)
        vsix_size = (
            f"{os.path.getsize(vsix_path) / 1024:.0f} KB"
            if vsix_exists else "N/A"
        )

        return _text_result(
            f"Build {'succeeded' if proc.returncode == 0 else 'FAILED'} "
            f"(exit code {proc.returncode})\n"
            f"VSIX exists: {vsix_exists} ({vsix_size})\n"
            f"VSIX path: {vsix_path}\n\n"
            f"Output:\n{output[-3000:]}"
        )
    except asyncio.TimeoutError:
        return _text_result("Build timed out after 5 minutes.")
    except FileNotFoundError:
        return _text_result(f"MSBuild not found at: {msbuild}")


@tool(
    "validate_pkgdef",
    "Parse pkgdef file for syntax errors and check for missing registrations",
    {"type": "object", "properties": {}},
)
async def validate_pkgdef(args: dict[str, Any]) -> dict[str, Any]:
    """Validate the pkgdef file for correctness."""
    if not os.path.exists(PKGDEF_PATH):
        return _text_result(f"pkgdef not found: {PKGDEF_PATH}")

    with open(PKGDEF_PATH, "r", encoding="utf-8", errors="replace") as f:
        content = f.read()

    lines = content.splitlines()
    issues: list[str] = []
    sections_found: set[str] = set()

    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        if not stripped or stripped.startswith(";"):
            continue

        # Track registry key sections
        if stripped.startswith("[$RootKey$"):
            key_match = re.match(r'\[\$RootKey\$\\([^\\]+)', stripped)
            if key_match:
                sections_found.add(key_match.group(1))

            # Check balanced brackets
            if not stripped.endswith("]"):
                issues.append(f"Line {i}: Unbalanced brackets: {stripped}")

        # Check DWORD values
        dword_match = re.search(r'=dword:([0-9a-fA-F]*)', stripped)
        if dword_match and len(dword_match.group(1)) != 8:
            issues.append(
                f"Line {i}: DWORD should be 8 hex digits: {stripped}"
            )

        # Check string values with unmatched quotes
        if '="' in stripped:
            after_eq = stripped.split('="', 1)[1]
            if after_eq.count('"') % 2 != 1:
                issues.append(
                    f"Line {i}: Possible unmatched quotes: {stripped}"
                )

    # Check for expected sections
    expected_sections = [
        "Packages", "Menus", "Projects", "ContentTypes",
        "Languages", "AutoLoadPackages", "TemplateEngine",
    ]
    missing = [s for s in expected_sections if s not in sections_found]

    parts = [
        f"Sections found ({len(sections_found)}):",
        *[f"  {s}" for s in sorted(sections_found)],
        "",
    ]

    if missing:
        parts.append(f"Missing expected sections ({len(missing)}):")
        parts.extend(f"  {s}" for s in missing)
        parts.append("")

    if issues:
        parts.append(f"Issues found ({len(issues)}):")
        parts.extend(f"  {issue}" for issue in issues)
    else:
        parts.append("No syntax issues found.")

    return _text_result("\n".join(parts))


@tool(
    "check_vsix_contents",
    "List all files inside the built VSIX zip archive",
    {"type": "object", "properties": {}},
)
async def check_vsix_contents(args: dict[str, Any]) -> dict[str, Any]:
    """List VSIX contents and flag missing expected files."""
    if not os.path.exists(VSIX_OUTPUT):
        return _text_result(
            f"VSIX not found: {VSIX_OUTPUT}\n"
            "Run build_vsix first to create the VSIX."
        )

    try:
        with zipfile.ZipFile(VSIX_OUTPUT, "r") as zf:
            entries = zf.infolist()
    except zipfile.BadZipFile:
        return _text_result(f"Invalid zip file: {VSIX_OUTPUT}")

    parts = [f"VSIX contents ({len(entries)} files):"]
    total_size = 0
    entry_names: set[str] = set()

    for entry in sorted(entries, key=lambda e: e.filename):
        size_kb = entry.file_size / 1024
        total_size += entry.file_size
        parts.append(f"  {entry.filename} ({size_kb:.1f} KB)")
        entry_names.add(entry.filename.lower().replace("\\", "/"))

    parts.append(f"\nTotal uncompressed: {total_size / 1024:.0f} KB")

    # Check for expected files
    expected = {
        "BasicLang.VisualStudio.dll": "Extension assembly",
        "BasicLang.VisualStudio.pkgdef": "Registry entries",
        "LanguageService/BasicLangGrammar.json": "TextMate grammar",
        "BuildSystem/BasicLang.targets": "MSBuild targets",
    }
    template_prefixes = [
        "ProjectTemplates/BasicLang/ConsoleApp",
        "ProjectTemplates/BasicLang/ClassLibrary",
        "ProjectTemplates/BasicLang/WinFormsApp",
        "ProjectTemplates/BasicLang/WpfApp",
        "ItemTemplates/BasicLang/Class",
        "ItemTemplates/BasicLang/Module",
        "ItemTemplates/BasicLang/Interface",
    ]

    missing: list[str] = []
    for filename, desc in expected.items():
        if filename.lower() not in entry_names:
            missing.append(f"  {filename} ({desc})")

    for prefix in template_prefixes:
        found = any(
            name.startswith(prefix.lower()) for name in entry_names
        )
        if not found:
            missing.append(f"  {prefix}.zip (template)")

    # Check vstman files
    for vstman in [
        "ProjectTemplates/BasicLang.ProjectTemplates.vstman",
        "ItemTemplates/BasicLang.ItemTemplates.vstman",
    ]:
        if vstman.lower() not in entry_names:
            missing.append(f"  {vstman} (template manifest)")

    if missing:
        parts.append(f"\nMissing expected files ({len(missing)}):")
        parts.extend(missing)
    else:
        parts.append("\nAll expected files present.")

    return _text_result("\n".join(parts))


@tool(
    "check_templates",
    "Verify template source files and zips contain correct vstemplate files",
    {"type": "object", "properties": {}},
)
async def check_templates(args: dict[str, Any]) -> dict[str, Any]:
    """Verify templates are correctly configured."""
    if not os.path.exists(TEMPLATES_DIR):
        return _text_result(f"Templates directory not found: {TEMPLATES_DIR}")

    parts: list[str] = []
    issues: list[str] = []

    # Check project templates
    projects_dir = os.path.join(TEMPLATES_DIR, "Projects")
    if os.path.isdir(projects_dir):
        parts.append("Project Templates:")
        for name in sorted(os.listdir(projects_dir)):
            template_dir = os.path.join(projects_dir, name)
            if not os.path.isdir(template_dir):
                continue

            vstemplate = os.path.join(template_dir, f"{name}.vstemplate")
            if not os.path.exists(vstemplate):
                issues.append(f"  Missing: {name}/{name}.vstemplate")
                parts.append(f"  {name}: MISSING vstemplate")
                continue

            with open(vstemplate, "r", encoding="utf-8", errors="replace") as f:
                content = f.read()

            # Check ProjectType
            pt_match = re.search(
                r"<ProjectType>(\w+)</ProjectType>", content
            )
            project_type = pt_match.group(1) if pt_match else "NOT FOUND"
            if project_type != "VisualBasic":
                issues.append(
                    f"  {name}: ProjectType is '{project_type}', "
                    f"should be 'VisualBasic'"
                )

            parts.append(f"  {name}: ProjectType={project_type}")

    # Check item templates
    items_dir = os.path.join(TEMPLATES_DIR, "Items")
    if os.path.isdir(items_dir):
        parts.append("\nItem Templates:")
        for name in sorted(os.listdir(items_dir)):
            template_dir = os.path.join(items_dir, name)
            if not os.path.isdir(template_dir):
                continue

            vstemplate = os.path.join(template_dir, f"{name}.vstemplate")
            if not os.path.exists(vstemplate):
                issues.append(f"  Missing: {name}/{name}.vstemplate")
                parts.append(f"  {name}: MISSING vstemplate")
                continue

            parts.append(f"  {name}: OK")

    # Check vstman files
    parts.append("\nTemplate Manifests:")
    for vstman_name in [
        "BasicLang.ProjectTemplates.vstman",
        "BasicLang.ItemTemplates.vstman",
    ]:
        vstman_path = os.path.join(TEMPLATES_DIR, vstman_name)
        if not os.path.exists(vstman_path):
            issues.append(f"  Missing: {vstman_name}")
            parts.append(f"  {vstman_name}: MISSING")
            continue

        with open(vstman_path, "r", encoding="utf-8", errors="replace") as f:
            content = f.read()

        # Check TemplateFileName references .vstemplate not .zip
        bad_refs = re.findall(
            r"<TemplateFileName>(\w+\.zip)</TemplateFileName>", content
        )
        if bad_refs:
            for ref in bad_refs:
                issues.append(
                    f"  {vstman_name}: TemplateFileName '{ref}' "
                    f"should reference .vstemplate, not .zip"
                )
        parts.append(f"  {vstman_name}: OK")

    if issues:
        parts.append(f"\nIssues ({len(issues)}):")
        parts.extend(issues)
    else:
        parts.append("\nAll templates correctly configured.")

    return _text_result("\n".join(parts))


@tool(
    "get_extension_architecture",
    "Return the VS extension architecture overview",
    {"type": "object", "properties": {}},
)
async def get_extension_architecture(args: dict[str, Any]) -> dict[str, Any]:
    """Return the extension architecture overview."""
    return _text_result("""\
BasicLang Visual Studio 2022 Extension — Architecture Overview

Extension Type: CPS-based VSIX (Common Project System)
Target: Visual Studio 2022 (17.x), .NET Framework 4.8
Current Version: 2.4.0

Components:
  1. Package (BasicLangPackage.cs)
     - AsyncPackage with background loading
     - Registers project factory, commands, options
     - Auto-loads on solution open/close

  2. CPS Project System (ProjectSystem/)
     - BasicLangProjectFactory: .blproj file association
     - Project capabilities via MEF [AppliesTo("BasicLang")]
     - Solution Explorer tree customization

  3. LSP Client (LanguageService/)
     - ILanguageClient launching BasicLang.exe --lsp
     - TextMate grammar for syntax highlighting
     - Content type registration for .bas/.bl/.blproj

  4. Templates (Templates/)
     - 4 project: ConsoleApp, ClassLibrary, WinFormsApp, WpfApp
     - 3 item: Class, Module, Interface
     - vstman manifests for VS 2017+ template discovery
     - Template zips created by MSBuild target, injected into VSIX

  5. Commands (Commands/)
     - VSCT-defined menu (BasicLang top-level menu)
     - 6 commands: Build, Run, ChangeBackend, RestartServer, GoToDef, FindRefs
     - Context-sensitive: enabled only on .bas/.bl/.blproj files

  6. Options (Options/)
     - General: LSP config, editor features
     - Compiler: Backend, warnings, optimizations

  7. Build System (BuildSystem/)
     - BasicLang.targets for MSBuild integration
     - Invokes BasicLang.exe for compilation

  8. Registry (pkgdef)
     - Manual pkgdef (GeneratePkgDefFile=false)
     - Package, Menus, Projects, ContentTypes, Languages,
       AutoLoad, TemplateEngine, MSBuild SafeImports

Build Pipeline:
  MSBuild → Compile C# → VSCT → Create template zips →
  Create VSIX container → Inject templates → Output .vsix

VSIX Contents:
  DLL, pkgdef, grammar, targets, 4 project zips + vstman,
  3 item zips + vstman, license
""")


@tool(
    "check_manifest",
    "Validate source.extension.vsixmanifest for common issues",
    {"type": "object", "properties": {}},
)
async def check_manifest(args: dict[str, Any]) -> dict[str, Any]:
    """Validate the VSIX manifest file."""
    if not os.path.exists(MANIFEST_PATH):
        return _text_result(f"Manifest not found: {MANIFEST_PATH}")

    with open(MANIFEST_PATH, "r", encoding="utf-8", errors="replace") as f:
        content = f.read()

    issues: list[str] = []
    parts: list[str] = []

    try:
        root = ElementTree.fromstring(content)
    except ElementTree.ParseError as e:
        return _text_result(f"XML parse error: {e}")

    # Namespace handling — VSIX manifest uses a default namespace
    ns = {"v": "http://schemas.microsoft.com/developer/vsx-schema/2011"}

    # Check Identity
    identity = root.find(".//v:Identity", ns)
    if identity is not None:
        version = identity.get("Version", "NOT SET")
        publisher = identity.get("Publisher", "NOT SET")
        parts.append(f"Identity: v{version} by {publisher}")
    else:
        issues.append("Missing <Identity> element")

    # Check InstallationTarget
    targets = root.findall(".//v:InstallationTarget", ns)
    for target in targets:
        version_range = target.get("Version", "NOT SET")
        parts.append(
            f"InstallationTarget: {target.get('Id')} {version_range}"
        )

    # Check ProductArchitecture
    arch = root.find(".//v:ProductArchitecture", ns)
    if arch is not None:
        parts.append(f"ProductArchitecture: {arch.text}")
    else:
        issues.append(
            "Missing <ProductArchitecture>amd64</ProductArchitecture> "
            "in Installation"
        )

    # Check Dependencies vs Prerequisites
    deps = root.findall(".//v:Dependency", ns)
    prereqs = root.findall(".//v:Prerequisite", ns)

    parts.append(f"\nDependencies: {len(deps)}")
    for dep in deps:
        dep_id = dep.get("Id", "?")
        parts.append(f"  {dep_id}")
        # Check for VS Setup components in Dependencies (wrong!)
        if dep_id.startswith("Microsoft.VisualStudio.Component"):
            issues.append(
                f"VS Setup component '{dep_id}' in Dependencies — "
                f"should be in Prerequisites"
            )

    parts.append(f"Prerequisites: {len(prereqs)}")
    for prereq in prereqs:
        parts.append(f"  {prereq.get('Id', '?')}")

    # Check Assets
    assets = root.findall(".//v:Asset", ns)
    parts.append(f"\nAssets: {len(assets)}")
    for asset in assets:
        parts.append(
            f"  {asset.get('Type', '?')}: {asset.get('Path', '?')}"
        )

    if issues:
        parts.append(f"\nIssues ({len(issues)}):")
        parts.extend(f"  {issue}" for issue in issues)
    else:
        parts.append("\nNo manifest issues found.")

    return _text_result("\n".join(parts))


# ---------------------------------------------------------------------------
# MCP server with all custom tools
# ---------------------------------------------------------------------------
vsext_mcp_server = create_sdk_mcp_server(
    name="vsext-tools",
    version="1.0.0",
    tools=[
        build_vsix, validate_pkgdef, check_vsix_contents,
        check_templates, get_extension_architecture, check_manifest,
    ],
)

# ---------------------------------------------------------------------------
# Agent runner
# ---------------------------------------------------------------------------

async def run_agent(
    prompt: str,
    profile_name: str | None = None,
    verbose: bool = False,
) -> str:
    """Run the VS extension agent with the given prompt and optional profile."""

    system = SYSTEM_PROMPT
    allowed_tools = ["Read", "Edit", "Write", "Bash", "Glob", "Grep"]

    if profile_name and profile_name in TASK_PROFILES:
        profile = TASK_PROFILES[profile_name]
        system += profile["system_prompt_suffix"]
        allowed_tools = list(profile["allowed_tools"])
        if not prompt:
            prompt = profile["default_prompt"]
        if verbose:
            print(f"Using profile: {profile_name} -- {profile['description']}")

    custom_tool_names = [
        "mcp__vsext-tools__build_vsix",
        "mcp__vsext-tools__validate_pkgdef",
        "mcp__vsext-tools__check_vsix_contents",
        "mcp__vsext-tools__check_templates",
        "mcp__vsext-tools__get_extension_architecture",
        "mcp__vsext-tools__check_manifest",
    ]
    allowed_tools.extend(custom_tool_names)

    options = ClaudeAgentOptions(
        system_prompt=system,
        allowed_tools=allowed_tools,
        permission_mode="acceptEdits",
        cwd=PROJECT_ROOT,
        mcp_servers={
            "vsext-tools": vsext_mcp_server,
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
    print("BasicLang VS Extension Agent -- Interactive Mode")
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
            print("Goodbye!")
            break

        if user_input.lower() == "profiles":
            for name, profile in TASK_PROFILES.items():
                marker = " *" if name == current_profile else ""
                print(f"  {name}: {profile['description']}{marker}")
            continue

        if user_input.lower().startswith("profile "):
            new_profile = user_input.split(" ", 1)[1].strip()
            if new_profile in TASK_PROFILES:
                current_profile = new_profile
                print(f"Switched to profile: {current_profile}")
            else:
                print(f"Unknown profile: {new_profile}")
                print(f"Available: {', '.join(TASK_PROFILES.keys())}")
            continue

        await run_agent(user_input, current_profile, verbose=True)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main_cli():
    """CLI entry point for the VS extension agent."""
    parser = argparse.ArgumentParser(
        description=(
            "BasicLang VS Extension Agent — "
            "AI-powered extension maintenance"
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
Examples:
  python vsextension_agent.py "Fix template not appearing in New Project"
  python vsextension_agent.py --profile vsix "Rebuild VSIX"
  python vsextension_agent.py --profile templates "Add Game template"
  python vsextension_agent.py --profile pkgdef "Audit registry entries"
  python vsextension_agent.py --profile review
  python vsextension_agent.py --interactive
  python vsextension_agent.py --list-profiles
""",
    )

    parser.add_argument(
        "prompt", nargs="?", default="",
        help="Task prompt for the agent",
    )
    parser.add_argument(
        "--profile", "-p", choices=list(TASK_PROFILES.keys()),
        help="Task profile to use",
    )
    parser.add_argument(
        "--interactive", "-i", action="store_true",
        help="Run in interactive mode",
    )
    parser.add_argument(
        "--list-profiles", action="store_true",
        help="List available task profiles",
    )
    parser.add_argument(
        "--verbose", "-v", action="store_true",
        help="Show detailed agent output",
    )

    args = parser.parse_args()

    if args.list_profiles:
        print("Available profiles:")
        for name, profile in TASK_PROFILES.items():
            print(f"  {name:12s} — {profile['description']}")
        return

    if args.interactive:
        asyncio.run(interactive_mode(args.profile))
        return

    if not args.prompt and not args.profile:
        parser.print_help()
        return

    result = asyncio.run(run_agent(args.prompt, args.profile, args.verbose))
    if result and not args.verbose:
        print(result)


if __name__ == "__main__":
    main_cli()
