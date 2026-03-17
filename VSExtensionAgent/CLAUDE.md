# VS Extension Agent — Claude Code Instructions

This directory contains a Claude Agent SDK application for the BasicLang Visual Studio 2022 extension.

## What This Is

A Python application that uses the Claude Agent SDK to create an AI agent specialized for maintaining the BasicLang VS 2022 VSIX extension. The agent has:

- **7 task profiles** for each VS extensibility subsystem (vsix, cps, lsp, templates, pkgdef, commands, review)
- **6 custom MCP tools** for building, validating, and inspecting the VSIX
- **Interactive mode** for conversational extension development sessions

## Running the Agent

```bash
pip install -r requirements.txt

python vsextension_agent.py "Fix template not appearing in New Project"
python vsextension_agent.py --profile vsix "Rebuild VSIX"
python vsextension_agent.py --profile templates "Verify all vstemplates"
python vsextension_agent.py --interactive
python vsextension_agent.py --list-profiles
```

## Project Context

The agent operates on the parent directory (VisualGameStudioEngine), targeting:
- **BasicLang.VisualStudio/src/BasicLang.VisualStudio/** — CPS-based VSIX extension
- 12 C# source files: Package, ProjectSystem, LanguageService, Commands, Options
- Templates: 4 project + 3 item templates with vstman manifests
- Manual pkgdef (GeneratePkgDefFile=false)
- VSIX build via VS 2022 MSBuild (NOT dotnet build)

## Key Conventions

- VSIX build requires VS 2022 MSBuild, not `dotnet build`
- Template `<ProjectType>` must be `VisualBasic` (VS ignores custom types)
- vstman `TemplateFileName` references `.vstemplate` inside zip, NOT `.zip` filename
- All pkgdef entries are manual — every [ProvideX] attribute needs a matching section
- Dependencies = VSIX/NDP; Prerequisites = VS Setup components (never mix)
- Template zips created by custom MSBuild `CreateTemplateZips` target
