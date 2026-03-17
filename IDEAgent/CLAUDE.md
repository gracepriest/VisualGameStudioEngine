# IDE Agent — Claude Code Instructions

This directory contains a Claude Agent SDK application for the Visual Game Studio IDE.

## What This Is

A Python application that uses the Claude Agent SDK to create an AI agent specialized for working with the Visual Game Studio IDE codebase. The agent has:

- **7 task profiles** for different IDE subsystems (editor, shell, debugger, project, ui, refactoring, review)
- **6 custom MCP tools** for building, testing, launching, and querying architecture
- **Interactive mode** for conversational IDE development sessions

## Running the Agent

```bash
pip install -r requirements.txt

python ide_agent.py "Fix the breakpoint margin rendering"
python ide_agent.py --profile editor "Add word wrap toggle"
python ide_agent.py --interactive
python ide_agent.py --list-profiles
```

## Project Context

The agent operates on the parent directory (VisualGameStudioEngine), which contains:
- **VisualGameStudio.Core/** — 29 service interfaces, models, events
- **VisualGameStudio.ProjectSystem/** — 29 service implementations (LSP, DAP, build)
- **VisualGameStudio.Editor/** — Avalonia code editor, 7 renderers, margins, multi-cursor
- **VisualGameStudio.Shell/** — IDE shell, 17 panel VMs, 40+ dialog VMs, MainWindow
- **VisualGameStudio.Tests/** — 1636 xUnit tests

## Key Conventions

- UI framework: Avalonia 11.x (NOT WPF), uses .axaml files (NOT .xaml)
- Editor: AvaloniaEdit (NOT AvalonEdit)
- MVVM: CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
- DI: Microsoft.Extensions.DependencyInjection, register in ServiceConfiguration.cs
- Events: IEventAggregator pub/sub for cross-VM communication
- New services: interface in Core, implementation in ProjectSystem
- New panels: ViewModel in Shell/ViewModels/Panels/, View in Shell/Views/Panels/
