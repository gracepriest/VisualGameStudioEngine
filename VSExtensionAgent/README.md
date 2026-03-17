# BasicLang VS Extension Agent

An AI-powered development agent for the BasicLang Visual Studio 2022 extension, built with the [Claude Agent SDK](https://docs.anthropic.com/en/docs/claude-agent-sdk).

## Setup

### Prerequisites

- Python 3.10+
- Claude Code CLI installed and authenticated
- Visual Studio 2022 with "Visual Studio extension development" workload
- .NET 8 SDK (for BasicLang compiler)

### Install

```bash
cd VSExtensionAgent
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
python vsextension_agent.py "Fix template not appearing in New Project dialog"
python vsextension_agent.py -v "Add a Game project template"
```

### Task Profiles

Profiles configure the agent with domain-specific instructions and tool access:

| Profile | Description | Tools |
|---------|-------------|-------|
| `vsix` | VSIX manifest, build pipeline, MSBuild targets, packaging | Read, Edit, Write, Bash, Glob, Grep |
| `cps` | CPS project system: factory, capabilities, unconfigured/configured project | Read, Edit, Write, Bash, Glob, Grep |
| `lsp` | LSP client, language server discovery, content types, TextMate grammar | Read, Edit, Write, Bash, Glob, Grep |
| `templates` | Project/item templates, vstemplates, vstman manifests, template zips | Read, Edit, Write, Bash, Glob, Grep |
| `pkgdef` | Registry entries: package, menus, project factory, auto-load, templates | Read, Edit, Write, Bash, Glob, Grep |
| `commands` | VSCT commands, command handlers, options pages | Read, Edit, Write, Bash, Glob, Grep |
| `review` | Read-only code review for VS 2022 compatibility issues | Read, Glob, Grep |

```bash
python vsextension_agent.py --profile vsix "Rebuild VSIX with updated manifest"
python vsextension_agent.py --profile templates "Check all vstemplates have correct ProjectType"
python vsextension_agent.py --profile pkgdef "Audit registry entries"
python vsextension_agent.py --profile review

python vsextension_agent.py --list-profiles
```

### Interactive Mode

```bash
python vsextension_agent.py --interactive
python vsextension_agent.py --interactive --profile templates
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
| `build_vsix` | Build VSIX via MSBuild (auto-discovers VS 2022 path) |
| `validate_pkgdef` | Parse pkgdef for syntax errors and missing registrations |
| `check_vsix_contents` | List all files inside the built VSIX zip |
| `check_templates` | Verify template files have correct ProjectType and vstman refs |
| `get_extension_architecture` | Return extension architecture overview |
| `check_manifest` | Validate vsixmanifest for Dependencies/Prerequisites issues |

## Architecture

```
vsextension_agent.py
├── SYSTEM_PROMPT          # Deep VS 2022 extension knowledge
├── TASK_PROFILES          # 7 domain-specific configurations
├── Custom Tools (MCP)     # build, validate, check contents/templates/manifest
├── run_agent()            # One-shot execution via query()
├── interactive_mode()     # REPL-style interaction loop
└── main_cli()             # CLI with argparse
```

## Examples

```bash
# Fix templates not appearing in New Project dialog
python vsextension_agent.py -v --profile templates \
  "Templates aren't showing up in VS 2022 - check ProjectType and vstman"

# Audit pkgdef for missing registrations
python vsextension_agent.py -v --profile pkgdef \
  "Cross-reference package attributes with pkgdef entries"

# Check VSIX contents after build
python vsextension_agent.py -v --profile vsix \
  "Build the VSIX and verify all expected files are included"

# Review LSP client for issues
python vsextension_agent.py -v --profile lsp \
  "Check the language client for proper server lifecycle management"

# Full extension review
python vsextension_agent.py -v --profile review \
  "Review for VS 2022 compatibility, threading, and missing registrations"
```
